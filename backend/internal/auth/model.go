// Package auth 提供本地演示账号、会话与角色授权的领域逻辑。
// 计划 006 新增：用户名固定 inspector/admin，密码从环境注入，会话令牌随机不透明。
package auth

import "time"

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
	ID          string
	Username    string
	DisplayName string
	Role        Role
	PasswordHash string
	CreatedAt   string
	UpdatedAt   string
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
