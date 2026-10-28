package httpapi

import (
	"errors"
	"io"
	"net/http"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/auth"
	"ar-inspection/backend/internal/issue"
)

// maxRequestBody 创建与状态修改请求体的上限（1 MiB）：超限直接拒绝，
// 通过 LimitReader 不读取无限请求体。
const maxRequestBody = 1 << 20

// Handler 持有路由依赖、问题服务端字段生成器、账号服务与实时事件中心。
type Handler struct {
	store             issue.Store
	fields            issue.FieldGenerator
	auth              *auth.Service
	hub               *Hub
	wsOriginPatterns  []string
	quickLoginEnabled bool
}

// Option 可选的开发期路由配置；不传时保持既有生产行为。
type Option func(*Handler)

// WithQuickLogin 启用 POST /api/auth/quick-login（默认不注册该路由）。
// 仅供本地开发：只对内置 inspector/admin 签发会话，不校验密码。
func WithQuickLogin(enabled bool) Option {
	return func(h *Handler) { h.quickLoginEnabled = enabled }
}

// NewRouter 构建 Gin 引擎：统一响应、CORS（含凭据）、健康检查、账号接口、
// 受保护的问题业务接口、人员管理接口、管理员 WebSocket 与 404/405 统一错误。
// 权限矩阵（计划 006 第 4.1 节 + 计划 008 增补）：
//   - POST /api/issues            仅巡检员（服务端绑定创建人）
//   - GET  /api/issues            登录即可；巡检员只见自己的，管理员见全部
//   - PATCH /api/issues/:id/status 仅管理员
//   - /api/users（GET/POST/PATCH/DELETE） 仅管理员
//   - GET  /api/ws                仅管理员
func NewRouter(store issue.Store, authService *auth.Service, corsOrigins []string, fields issue.FieldGenerator, opts ...Option) *gin.Engine {
	h := &Handler{
		store:            store,
		fields:           fields,
		auth:             authService,
		hub:              NewHub(),
		wsOriginPatterns: originToPatterns(corsOrigins),
	}
	for _, opt := range opts {
		opt(h)
	}

	engine := gin.New()
	engine.Use(gin.Logger(), gin.Recovery(), CORSMiddleware(corsOrigins))
	engine.HandleMethodNotAllowed = true
	engine.NoRoute(func(c *gin.Context) {
		fail(c, http.StatusNotFound, "not_found", "接口路径不存在")
	})
	engine.NoMethod(func(c *gin.Context) {
		fail(c, http.StatusMethodNotAllowed, "method_not_allowed", "请求方法不被支持")
	})

	api := engine.Group("/api")
	api.GET("/health", h.health)

	authGroup := api.Group("/auth")
	authGroup.POST("/login", h.login)
	// 开发快捷登录仅在显式启用时注册；未启用时该路径与普通未知路径一致返回 404
	if h.quickLoginEnabled {
		authGroup.POST("/quick-login", h.quickLogin)
	}
	// logout 不要求有效会话：令牌已过期/已撤销时重复退出幂等成功（目标状态已达成）
	authGroup.POST("/logout", h.logout)
	authGroup.GET("/me", h.Authenticate, h.me)

	issues := api.Group("/issues", h.Authenticate)
	issues.GET("", h.listIssues)
	issues.POST("", RequireRole(auth.RoleInspector), h.createIssue)
	issues.PATCH("/:id/status", RequireRole(auth.RoleAdmin), h.updateStatus)

	// 人员管理（计划 008）：全部仅管理员
	users := api.Group("/users", h.Authenticate, RequireRole(auth.RoleAdmin))
	users.GET("", h.listUsers)
	users.POST("", h.createUser)
	users.PATCH("/:username", h.updateUser)
	users.DELETE("/:username", h.deleteUser)

	api.GET("/ws", h.Authenticate, RequireRole(auth.RoleAdmin), h.handleWS)
	return engine
}

// Hub 返回事件中心（测试与外部观测用）。
func (h *Handler) Hub() *Hub { return h.hub }

// health 健康检查：除进程存活外还实际执行存储轻量检查，存储不可用返回 503。
func (h *Handler) health(c *gin.Context) {
	if err := h.store.HealthCheck(c.Request.Context()); err != nil {
		fail(c, http.StatusServiceUnavailable, "service_unavailable", "存储暂不可用，请稍后重试")
		return
	}
	ok(c, http.StatusOK, gin.H{"status": "ok"})
}

// listIssues 按当前角色返回授权范围列表：巡检员仅见自己创建的问题，
// 管理员见全部（含无归属记录的旧数据）。字段与既有问题契约完全一致。
// 角色判断失败关闭：未知角色返回 403，绝不因未匹配到分支而退化为返回全量。
func (h *Handler) listIssues(c *gin.Context) {
	user := CurrentUser(c)
	var ownerID string
	switch user.Role {
	case auth.RoleAdmin:
		ownerID = "" // 空 ownerID 表示全量，仅供管理员
	case auth.RoleInspector:
		ownerID = auth.UserID(user.Username)
	default:
		fail(c, http.StatusForbidden, "permission_denied", "当前账号没有查看问题列表的权限")
		return
	}
	issues, err := h.store.List(c.Request.Context(), ownerID)
	if err != nil {
		h.internalError(c, "获取问题列表失败，请稍后重试", err)
		return
	}
	ok(c, http.StatusOK, issues)
}

// createIssue 创建问题（仅巡检员）：限制请求体大小、严格解析 JSON、由领域逻辑
// 校验并生成服务端字段，事务写库（含创建人关联）成功后发布实时事件并返回 201。
func (h *Handler) createIssue(c *gin.Context) {
	body, readable := readBody(c)
	if !readable {
		return
	}
	req, err := issue.DecodeCreateRequest(body)
	if err != nil {
		badRequest(c, err)
		return
	}
	newIssue, err := req.BuildNewIssue(h.fields)
	if err != nil {
		h.mapDomainError(c, err, "创建问题失败，请稍后重试")
		return
	}
	user := CurrentUser(c)
	stored, err := h.store.Create(c.Request.Context(), newIssue, auth.UserID(user.Username))
	if err != nil {
		h.mapDomainError(c, err, "创建问题失败，请稍后重试")
		return
	}
	// 存储事务提交成功后才发布事件；无连接时事件丢弃，由客户端全量校准兜底
	h.hub.Publish("issue.created", stored)
	ok(c, http.StatusCreated, stored)
}

// updateStatus 修改处理状态：先严格校验 JSON 与状态值，再查询 ID，
// 因此“非法状态 + 不存在的 ID”返回 400 而不是 404。
// 空路径段（如 /api/issues//status，客户端拼 URL 时 ID 为空）被 Gin 绑定为
// 空参数而非路由不匹配，这里显式拦截为 404 not_found——与主流框架及 GitHub 等
// API 的“空段=路径未命中”行为一致；判断放在读请求体之前，等同路由层拦截。
func (h *Handler) updateStatus(c *gin.Context) {
	id := c.Param("id")
	if id == "" {
		fail(c, http.StatusNotFound, "not_found", "接口路径不存在")
		return
	}
	body, readable := readBody(c)
	if !readable {
		return
	}
	status, err := issue.DecodeStatusUpdateRequest(body)
	if err != nil {
		badRequest(c, err)
		return
	}
	updatedAt, err := h.fields.NewNow()
	if err != nil {
		h.internalError(c, "修改状态失败，请稍后重试", err)
		return
	}
	stored, err := h.store.UpdateStatus(c.Request.Context(), id, status, updatedAt)
	if err != nil {
		h.mapDomainError(c, err, "修改状态失败，请稍后重试")
		return
	}
	// 存储事务提交成功后才发布事件
	h.hub.Publish("issue.updated", stored)
	ok(c, http.StatusOK, stored)
}

// readBody 读取不超过 1 MiB 的请求体；超限或读取失败返回 400 invalid_argument。
func readBody(c *gin.Context) ([]byte, bool) {
	body, err := io.ReadAll(io.LimitReader(c.Request.Body, maxRequestBody+1))
	if err != nil {
		fail(c, http.StatusBadRequest, "invalid_argument", "读取请求体失败")
		return nil, false
	}
	if len(body) > maxRequestBody {
		fail(c, http.StatusBadRequest, "invalid_argument", "请求体不能超过 1 MiB")
		return nil, false
	}
	return body, true
}

// badRequest 输出参数错误响应；err 为领域校验错误，信息已是明确中文。
func badRequest(c *gin.Context, err error) {
	fail(c, http.StatusBadRequest, "invalid_argument", err.Error())
}

// mapDomainError 把领域层错误映射为统一响应：校验错误 400、目标不存在 404、
// 其余为内部错误 500；内部详细错误不返回给客户端。
func (h *Handler) mapDomainError(c *gin.Context, err error, internalMessage string) {
	var vErr *issue.ValidationError
	if errors.As(err, &vErr) {
		fail(c, http.StatusBadRequest, "invalid_argument", vErr.Message)
		return
	}
	if errors.Is(err, issue.ErrNotFound) {
		fail(c, http.StatusNotFound, "issue_not_found", "指定的问题不存在")
		return
	}
	h.internalError(c, internalMessage, err)
}

// internalError 输出统一 500 响应；详细错误通过 c.Error 附到本机 gin 日志，
// 响应中只保留中文提示，不包含 SQL、路径或堆栈。
func (h *Handler) internalError(c *gin.Context, message string, err error) {
	_ = c.Error(err)
	fail(c, http.StatusInternalServerError, "internal_error", message)
}

// ok 输出统一成功响应 { "data": ... }。
func ok(c *gin.Context, status int, data any) {
	c.JSON(status, gin.H{"data": data})
}

// fail 输出统一错误响应 { "error": { "code": ..., "message": ... } }。
func fail(c *gin.Context, status int, code, message string) {
	c.JSON(status, gin.H{"error": gin.H{"code": code, "message": message}})
}
