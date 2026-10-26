package httpapi

import (
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
)

// CORSMiddleware 按配置的前端来源放行跨域请求。
// 允许 GET/POST/PATCH/OPTIONS 方法与 Content-Type 请求头；预检返回 204。
// 首版不使用 Cookie 或浏览器凭据，因此不设置 Allow-Credentials。
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
				c.Header("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
				c.Header("Access-Control-Allow-Headers", "Content-Type")
			}
		}
		if c.Request.Method == http.MethodOptions {
			c.AbortWithStatus(http.StatusNoContent)
			return
		}
		c.Next()
	}
}
