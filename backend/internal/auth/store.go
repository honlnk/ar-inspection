package auth

import "context"

// Store 定义账号与会话的存储操作；HTTP 层通过 Service 访问，不直接拼 SQL。
type Store interface {
	// Init 建表并执行轻量检查；与问题库同库初始化，幂等执行。
	Init(ctx context.Context) error

	// UpsertAccount 按用户名幂等建立或更新账号：不存在则创建，存在则更新
	// 显示名与密码哈希（密码环境变化时同步），不输出密码。
	UpsertAccount(ctx context.Context, username, displayName string, role Role, passwordHash string, now string) error

	// FindByUsername 按用户名查询账号；不存在返回 ErrInvalidCredentials 语义由 Service 决定。
	FindByUsername(ctx context.Context, username string) (User, error)

	// CreateSession 写入新会话记录。
	CreateSession(ctx context.Context, s Session) error

	// FindSession 按令牌摘要查询会话（含账号信息）。
	FindSession(ctx context.Context, tokenHash string) (Session, User, error)

	// RevokeSession 将会话标记为撤销；退出立即失效。目标不存在不算错误。
	RevokeSession(ctx context.Context, tokenHash string, now string) error

	// DeleteExpiredSessions 清理已过期会话记录，随启动执行；失败不阻断启动。
	DeleteExpiredSessions(ctx context.Context, now string) error

	// ---- 计划 008：人员管理存储操作 ----

	// CountUsers 返回账号总数；用于首启判定（库中无任何账号时才按环境变量
	// 创建初始巡检员，之后完全由管理页维护，删除后重启不复活）。
	CountUsers(ctx context.Context) (int, error)

	// ListUsers 返回全部账号（含管理员），按用户名排序；供人员管理页展示。
	ListUsers(ctx context.Context) ([]User, error)

	// CreateUser 创建巡检员账号；用户名冲突返回 ErrUsernameExists。
	CreateUser(ctx context.Context, username, displayName, passwordHash, now string) error

	// FindManagedUser 按用户名查询账号；不存在返回 ErrUserNotFound
	//（管理操作目标需与登录探测语义区分：页面列表本就可见全部用户名）。
	FindManagedUser(ctx context.Context, username string) (User, error)

	// UpdateUserFields 按用户名更新显示名与/或密码哈希；至少一项。
	// 目标不存在返回 ErrUserNotFound；目标为管理员返回 ErrAdminProtected。
	// resetSessions 为 true（重置密码）时同事务清除该账号全部会话。
	UpdateUserFields(ctx context.Context, username string, displayName *string, passwordHash string, resetSessions bool, now string) error

	// DeleteUser 删除巡检员账号，单事务内按「清归属记录 → 清会话 → 删账号」
	// 顺序执行，任一步失败整体回滚；问题本体不动（归属转未知，仅管理员可见）。
	// 目标不存在返回 ErrUserNotFound；目标为管理员返回 ErrAdminProtected。
	DeleteUser(ctx context.Context, username string) error
}
