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
}
