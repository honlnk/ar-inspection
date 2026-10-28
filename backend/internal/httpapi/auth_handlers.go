package httpapi

import (
	"errors"
	"net/http"
	"time"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/auth"
)

// sessionCookieMaxAge 与会话 TTL 一致（12 小时）；退出时置 -1 清除浏览器 Cookie。
const sessionCookieMaxAge = int(auth.SessionTTL / time.Second)

// login 处理 POST /api/auth/login：严格解析请求体，凭据校验通过后
// 设置会话 Cookie 并返回当前用户显示名与角色。
func (h *Handler) login(c *gin.Context) {
	body, readable := readBody(c)
	if !readable {
		return
	}
	req, err := auth.DecodeLoginRequest(body)
	if err != nil {
		badRequest(c, err)
		return
	}
	token, user, err := h.auth.Login(c.Request.Context(), req.Username, req.Password)
	if err != nil {
		if ve, ok := err.(*auth.ValidationError); ok {
			fail(c, http.StatusUnauthorized, "invalid_credentials", ve.Message)
			return
		}
		_ = c.Error(err)
		fail(c, http.StatusInternalServerError, "internal_error", "登录失败，请稍后重试")
		return
	}
	setSessionCookie(c, token)
	ok(c, http.StatusOK, user)
}

// quickLogin 处理 POST /api/auth/quick-login（仅开发期显式启用时注册）：
// 按角色为内置演示账号签发会话，不校验也不接收密码，避免客户端与仓库保存明文口令。
// 只允许 inspector/admin 两个内置账号，限制该接口可签发的权限面。
func (h *Handler) quickLogin(c *gin.Context) {
	body, readable := readBody(c)
	if !readable {
		return
	}
	req, err := auth.DecodeQuickLoginRequest(body)
	if err != nil {
		badRequest(c, err)
		return
	}
	token, user, err := h.auth.QuickLogin(c.Request.Context(), quickLoginUsername(req.Role))
	if err != nil {
		if errors.Is(err, auth.ErrQuickLoginUnavailable) {
			fail(c, http.StatusUnauthorized, "quick_login_unavailable", err.Error())
			return
		}
		_ = c.Error(err)
		fail(c, http.StatusInternalServerError, "internal_error", "快捷登录失败，请稍后重试")
		return
	}
	setSessionCookie(c, token)
	ok(c, http.StatusOK, user)
}

// quickLoginUsername 把角色映射到唯一的内置演示账号；解码阶段已保证角色合法。
func quickLoginUsername(role auth.Role) string {
	switch role {
	case auth.RoleInspector:
		return "inspector"
	case auth.RoleAdmin:
		return "admin"
	default:
		return ""
	}
}

// setSessionCookie 统一写入会话 Cookie（登录与开发快捷登录共用）。
// SetSameSite 需在 SetCookie 之前调用才会写入 Cookie 属性。
func setSessionCookie(c *gin.Context, token string) {
	c.SetSameSite(http.SameSiteLaxMode)
	c.SetCookie(sessionCookieName, token, sessionCookieMaxAge, "/", "", false, true)
}

// logout 处理 POST /api/auth/logout：撤销会话并清除浏览器 Cookie。
// 不要求有效会话：令牌无效时同样返回成功（幂等），始终清除 Cookie。
func (h *Handler) logout(c *gin.Context) {
	if err := h.auth.Logout(c.Request.Context(), extractToken(c)); err != nil {
		_ = c.Error(err)
		fail(c, http.StatusInternalServerError, "internal_error", "退出失败，请稍后重试")
		return
	}
	c.SetSameSite(http.SameSiteLaxMode)
	c.SetCookie(sessionCookieName, "", -1, "/", "", false, true)
	ok(c, http.StatusOK, gin.H{"loggedOut": true})
}

// me 处理 GET /api/auth/me：恢复登录状态，有效返回当前用户，无效返回未认证。
// 挂在认证中间件之后，能到达这里即已认证。
func (h *Handler) me(c *gin.Context) {
	ok(c, http.StatusOK, CurrentUser(c))
}
