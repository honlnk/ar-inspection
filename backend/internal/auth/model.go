// Package auth 提供本地演示账号、会话与角色授权的领域逻辑。
// 计划 006 新增：用户名固定 inspector/admin，密码从环境注入，会话令牌随机不透明。
// 计划 008 新增：巡检员账号由管理员在人员管理页增删改查；管理员账号仍由环境变量维护。
package auth

import (
	"errors"
	"strings"
	"time"
)

// Role 账号角色；权限矩阵见计划 006 第 4.1 节。
type Role string

const (
	RoleInspector Role = "inspector"
	RoleAdmin     Role = "admin"
)

// Valid 判断角色是否为合法枚举值。
func (r Role) Valid() bool {
	return r == RoleInspector || r == RoleAdmin
}

// User 账号信息；密码哈希只在存储层出现，不进入任何 JSON 响应。
type User struct {
	ID           string
	Username     string
	DisplayName  string
	Role         Role
	PasswordHash string
	CreatedAt    string
	UpdatedAt    string
}

// Public 不含密码哈希的对外视图。
type Public struct {
	Username    string `json:"username"`
	DisplayName string `json:"displayName"`
	Role        Role   `json:"role"`
}

// ToPublic 转为对外视图。
func (u User) ToPublic() Public {
	return Public{Username: u.Username, DisplayName: u.DisplayName, Role: u.Role}
}

// Session 一条持久化会话记录；令牌本体不落库，只存 HMAC 摘要。
// 时间字段为带 +08:00 偏移的 RFC 3339 字符串，与问题库约定一致。
type Session struct {
	TokenHash string
	UserID    string
	CreatedAt string
	ExpiresAt string
	RevokedAt *string
}

// UserID 返回用户名对应的账号 ID； UpsertAccount 与问题归属写入共用此约定。
func UserID(username string) string { return "user-" + username }

// SessionTTL 会话有效期：12 小时（计划 006 第 4.1 节）。
const SessionTTL = 12 * time.Hour

// 领域错误：HTTP 层据此映射统一中文响应。
var (
	// ErrInvalidCredentials 用户名或密码错误；统一提示，不区分账号是否存在。
	ErrInvalidCredentials = &ValidationError{"用户名或密码错误"}
	// ErrSessionNotFound 会话不存在、已过期或已撤销。
	ErrSessionNotFound = &ValidationError{"会话不存在或已过期"}
)

// ValidationError 参数或状态校验错误，Message 直接面向用户。
type ValidationError struct {
	Message string
}

func (e *ValidationError) Error() string { return e.Message }

// 计划 008 账号管理领域错误：HTTP 层据此映射 404/403/400。
var (
	// ErrUserNotFound 目标账号不存在。
	ErrUserNotFound = errors.New("账号不存在")
	// ErrAdminProtected 目标是管理员账号：环境变量维护，页面只读。
	ErrAdminProtected = errors.New("管理员账号由环境变量维护，不支持此操作")
	// ErrUsernameExists 用户名已被占用（含管理员）。
	ErrUsernameExists = errors.New("用户名已存在")
)

// 用户名长度边界与允许字符集（计划 008 第 4.3 节）；仅 URL 安全字符，
// 作为路径资源标识不需要转义。
const (
	usernameMinLength    = 2
	usernameMaxLength    = 32
	passwordMinLength    = 8
	PasswordMaxLength    = 72 // bcrypt 算法上限，超出会拒绝哈希
	displayNameMaxLength = 32
)

// ValidateUsername 校验新增用户名：2～32 位，仅英文字母、数字、下划线、连字符。
func ValidateUsername(username string) error {
	n := len(username)
	if n < usernameMinLength || n > usernameMaxLength {
		return &ValidationError{Message: "用户名需为 2～32 位字符"}
	}
	for _, r := range username {
		switch {
		case r >= 'a' && r <= 'z', r >= 'A' && r <= 'Z', r >= '0' && r <= '9', r == '_', r == '-':
		default:
			return &ValidationError{Message: "用户名仅支持英文字母、数字、下划线、连字符"}
		}
	}
	return nil
}

// ValidateDisplayName 校验显示名：去首尾空格后 1～32 个字符。
func ValidateDisplayName(displayName string) error {
	trimmed := strings.TrimSpace(displayName)
	if trimmed == "" {
		return &ValidationError{Message: "显示名不能为空"}
	}
	if len([]rune(trimmed)) > displayNameMaxLength {
		return &ValidationError{Message: "显示名需为 1～32 个字符"}
	}
	return nil
}

// ValidatePassword 校验密码：8～72 位，不能全空格。
func ValidatePassword(password string) error {
	if strings.TrimSpace(password) == "" {
		return &ValidationError{Message: "密码不能为空"}
	}
	n := len(password)
	if n < passwordMinLength {
		return &ValidationError{Message: "密码需至少 8 位"}
	}
	if n > PasswordMaxLength {
		return &ValidationError{Message: "密码不能超过 72 位"}
	}
	return nil
}

// ManagedAccount 人员管理页的账号视图：不含密码哈希；
// 与登录响应的 Public 相比，增加创建/更新时间供列表展示。
type ManagedAccount struct {
	Username    string `json:"username"`
	DisplayName string `json:"displayName"`
	Role        Role   `json:"role"`
	CreatedAt   string `json:"createdAt"`
	UpdatedAt   string `json:"updatedAt"`
}

// ManagementFilter 存储层管理操作的目标过滤：管理员账号只读，不可作为
// 修改/删除目标（403），但创建时仍需按用户名判重。
type ManagementFilter struct {
	// ForbidAdminTarget 为 true 时，管理操作目标命中管理员账号返回 ErrAdminProtected。
	ForbidAdminTarget bool
}
