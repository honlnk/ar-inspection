package httpapi

import (
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
)

// CORSMiddleware 按配置的前端来源放行跨域请求。
// 计划 006 起管理端依赖会话 Cookie，因此放行来源同时允许凭据（Allow-Credentials），
// 并放行 Authorization 头（Unity/工具使用 Bearer 令牌）；预检返回 204。
// 不使用通配符来源——凭据模式下浏览器也拒绝 *。
func CORSMiddleware(origins []string) gin.HandlerFunc {
	allowed := make(map[string]struct{}, len(origins))
	for _, o := range origins {
		o = strings.TrimSpace(o)
		if o != "" {
			allowed[o] = struct{}{}
		}
	}

	return func(c *gin.Context) {
		c.Header("Vary", "Origin")
		if origin := c.GetHeader("Origin"); origin != "" {
			if _, isAllowed := allowed[origin]; isAllowed {
				c.Header("Access-Control-Allow-Origin", origin)
				c.Header("Access-Control-Allow-Credentials", "true")
				// DELETE 自计划 008 起由管理端人员管理页使用，需在预检方法列表中放行
				c.Header("Access-Control-Allow-Methods", "GET, POST, PATCH, DELETE, OPTIONS")
				c.Header("Access-Control-Allow-Headers", "Content-Type, Authorization")
			}
		}
		if c.Request.Method == http.MethodOptions {
			c.AbortWithStatus(http.StatusNoContent)
			return
		}
		c.Next()
	}
}
