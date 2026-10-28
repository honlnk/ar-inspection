package httpapi

import (
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/auth"
)

// 会话 Cookie 名；HttpOnly + SameSite=Lax，本地 HTTP 不设 Secure（计划 006 第 4.1 节）。
const sessionCookieName = "inspection_session"

// contextUserKey Gin context 中存放当前账号的键。
const contextUserKey = "auth.user"

// extractToken 按优先级从 Authorization Bearer 头或会话 Cookie 提取令牌。
// Unity 使用 Bearer 头；浏览器使用 Cookie。
func extractToken(c *gin.Context) string {
	if h := c.GetHeader("Authorization"); h != "" {
		parts := strings.SplitN(h, " ", 2)
		if len(parts) == 2 && strings.EqualFold(parts[0], "Bearer") {
			return strings.TrimSpace(parts[1])
		}
	}
	token, err := c.Cookie(sessionCookieName)
	if err != nil {
		return ""
	}
	return token
}

// Authenticate 认证中间件：令牌无效、过期或已撤销统一 401 authentication_required。
func (h *Handler) Authenticate(c *gin.Context) {
	user, err := h.auth.Resolve(c.Request.Context(), extractToken(c))
	if err != nil {
		fail(c, http.StatusUnauthorized, "authentication_required", "请先登录")
		c.Abort()
		return
	}
	c.Set(contextUserKey, user)
	c.Next()
}

// RequireRole 角色中间件：已认证但角色不足统一 403 permission_denied。
func RequireRole(role auth.Role) gin.HandlerFunc {
	return func(c *gin.Context) {
		user := CurrentUser(c)
		if user.Role != role {
			fail(c, http.StatusForbidden, "permission_denied", "当前账号没有执行此操作的权限")
			c.Abort()
			return
		}
		c.Next()
	}
}

// CurrentUser 从请求上下文读取已认证账号；未认证路径返回零值。
func CurrentUser(c *gin.Context) auth.Public {
	v, exists := c.Get(contextUserKey)
	if !exists {
		return auth.Public{}
	}
	user, ok := v.(auth.Public)
	if !ok {
		return auth.Public{}
	}
	return user
}
