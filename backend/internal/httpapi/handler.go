package httpapi

import (
	"errors"
	"io"
	"net/http"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/issue"
)

// maxRequestBody 创建与状态修改请求体的上限（1 MiB）：超限直接拒绝，
// 通过 LimitReader 不读取无限请求体。
const maxRequestBody = 1 << 20

// Handler 持有路由依赖与问题服务端字段生成器。
type Handler struct {
	store  issue.Store
	fields issue.FieldGenerator
}

// NewRouter 构建 Gin 引擎：统一响应、CORS、健康检查、问题业务接口与 404/405 统一错误。
// fields 为服务端字段生成器，传入以便测试注入失败路径。
func NewRouter(store issue.Store, corsOrigins []string, fields issue.FieldGenerator) *gin.Engine {
	h := &Handler{store: store, fields: fields}

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
	api.GET("/issues", h.listIssues)
	api.POST("/issues", h.createIssue)
	api.PATCH("/issues/:id/status", h.updateStatus)
	return engine
}

// health 健康检查：除进程存活外还实际执行存储轻量检查，存储不可用返回 503。
func (h *Handler) health(c *gin.Context) {
	if err := h.store.HealthCheck(c.Request.Context()); err != nil {
		fail(c, http.StatusServiceUnavailable, "service_unavailable", "存储暂不可用，请稍后重试")
		return
	}
	ok(c, http.StatusOK, gin.H{"status": "ok"})
}

// listIssues 查询全部问题；查询失败返回 500 internal_error，不得用空数组伪装成功。
func (h *Handler) listIssues(c *gin.Context) {
	issues, err := h.store.List(c.Request.Context())
	if err != nil {
		h.internalError(c, "获取问题列表失败，请稍后重试", err)
		return
	}
	ok(c, http.StatusOK, issues)
}

// createIssue 创建问题：限制请求体大小、严格解析 JSON、由领域逻辑校验并生成
// 服务端字段，事务写库成功后返回 201 与完整记录。
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
	stored, err := h.store.Create(c.Request.Context(), newIssue)
	if err != nil {
		h.mapDomainError(c, err, "创建问题失败，请稍后重试")
		return
	}
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
