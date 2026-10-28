package httpapi

import (
	"errors"
	"net/http"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/auth"
)

// 计划 008：人员管理接口，全部仅管理员可用（401/403 语义与既有接口一致）。
// 路径以用户名作资源标识；用户名字符集已限为 URL 安全字符，无需转义。

// mapUserError 把人员管理领域错误映射为统一响应：校验错误与用户名重复 400、
// 目标不存在 404、管理员账号保护 403；其余 500。内部错误不返回给客户端。
func mapUserError(c *gin.Context, err error) {
	var vErr *auth.ValidationError
	if errors.As(err, &vErr) {
		fail(c, http.StatusBadRequest, "invalid_argument", vErr.Message)
		return
	}
	switch {
	case errors.Is(err, auth.ErrUserNotFound):
		fail(c, http.StatusNotFound, "user_not_found", "指定的账号不存在")
	case errors.Is(err, auth.ErrAdminProtected):
		fail(c, http.StatusForbidden, "admin_account_protected", "管理员账号由环境变量维护，不支持此操作")
	case errors.Is(err, auth.ErrUsernameExists):
		fail(c, http.StatusBadRequest, "invalid_argument", "用户名已存在")
	default:
		_ = c.Error(err)
		fail(c, http.StatusInternalServerError, "internal_error", "人员管理操作失败，请稍后重试")
	}
}

// listUsers 处理 GET /api/users：返回全部账号（含管理员，页面按角色只读展示）。
func (h *Handler) listUsers(c *gin.Context) {
	accounts, err := h.auth.ListManagedAccounts(c.Request.Context())
	if err != nil {
		h.internalError(c, "获取账号列表失败，请稍后重试", err)
		return
	}
	ok(c, http.StatusOK, accounts)
}

// createUser 处理 POST /api/users：创建巡检员，成功返回 201 与新账号公开信息。
func (h *Handler) createUser(c *gin.Context) {
	body, readable := readBody(c)
	if !readable {
		return
	}
	req, err := auth.DecodeCreateUserRequest(body)
	if err != nil {
		badRequest(c, err)
		return
	}
	created, err := h.auth.CreateInspector(c.Request.Context(), req.Username, req.DisplayName, derefString(req.Password))
	if err != nil {
		mapUserError(c, err)
		return
	}
	ok(c, http.StatusCreated, created)
}

// updateUser 处理 PATCH /api/users/:username：修改显示名与/或重置密码；
// 重置密码后该账号全部会话立即失效。
func (h *Handler) updateUser(c *gin.Context) {
	username := c.Param("username")
	if username == "" {
		fail(c, http.StatusNotFound, "not_found", "接口路径不存在")
		return
	}
	body, readable := readBody(c)
	if !readable {
		return
	}
	req, err := auth.DecodeUpdateUserRequest(body)
	if err != nil {
		badRequest(c, err)
		return
	}
	updated, err := h.auth.UpdateInspector(c.Request.Context(), username, req.DisplayName, req.Password)
	if err != nil {
		mapUserError(c, err)
		return
	}
	ok(c, http.StatusOK, updated)
}

// deleteUser 处理 DELETE /api/users/:username：删除巡检员与其会话；
// 其历史问题保留，归属转为未知（仅管理员可见）。
func (h *Handler) deleteUser(c *gin.Context) {
	username := c.Param("username")
	if username == "" {
		fail(c, http.StatusNotFound, "not_found", "接口路径不存在")
		return
	}
	if err := h.auth.DeleteInspector(c.Request.Context(), username); err != nil {
		mapUserError(c, err)
		return
	}
	c.Status(http.StatusNoContent)
}

// derefString 解引用可空字符串；nil 视为空串（CreateInspector 内再校验非空）。
func derefString(p *string) string {
	if p == nil {
		return ""
	}
	return *p
}
