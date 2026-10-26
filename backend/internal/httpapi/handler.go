package httpapi

import (
	"net/http"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/issue"
)

// Handler 持有路由依赖，本期只实现健康检查；问题相关接口留给后续业务计划。
type Handler struct {
	store issue.Store
}

// NewRouter 构建 Gin 引擎：统一响应、CORS、健康检查与 404/405 统一错误。
func NewRouter(store issue.Store, corsOrigins []string) *gin.Engine {
	h := &Handler{store: store}

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

// ok 输出统一成功响应 { "data": ... }。
func ok(c *gin.Context, status int, data any) {
	c.JSON(status, gin.H{"data": data})
}

// fail 输出统一错误响应 { "error": { "code": ..., "message": ... } }。
func fail(c *gin.Context, status int, code, message string) {
	c.JSON(status, gin.H{"error": gin.H{"code": code, "message": message}})
}
