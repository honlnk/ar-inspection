package auth

import (
	"context"
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"database/sql"
	"encoding/hex"
	"errors"
	"fmt"

	_ "modernc.org/sqlite" // 纯 Go SQLite 驱动
)

// authSchema 建表 SQL：与问题库同库；幂等创建。
// 会话只存令牌摘要（HMAC-SHA256），泄漏数据库也无法还原令牌本体。
const authSchema = `
CREATE TABLE IF NOT EXISTS users (
    id            TEXT PRIMARY KEY,
    username      TEXT NOT NULL UNIQUE,
    display_name  TEXT NOT NULL,
    role          TEXT NOT NULL CHECK (role IN ('inspector', 'admin')),
    password_hash TEXT NOT NULL,
    created_at    TEXT NOT NULL,
    updated_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sessions (
    token_hash TEXT PRIMARY KEY,
    user_id    TEXT NOT NULL REFERENCES users(id),
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    revoked_at TEXT
);
CREATE INDEX IF NOT EXISTS idx_sessions_user ON sessions (user_id);

-- 问题归属关联表：不改既有 issues 表结构；旧问题无记录 → 仅管理员可见。
CREATE TABLE IF NOT EXISTS issue_owners (
    issue_id   TEXT PRIMARY KEY REFERENCES issues(id) ON DELETE CASCADE,
    user_id    TEXT NOT NULL REFERENCES users(id),
    created_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_issue_owners_user ON issue_owners (user_id);
`

// SQLiteStore 是 auth.Store 的 SQLite 实现；与问题存储共用同一数据库文件，
// 但独立持有连接，便于职责分离。
type SQLiteStore struct{ db *sql.DB }

// OpenSQLite 打开（或创建）指定路径的数据库。
func OpenSQLite(path string) (*SQLiteStore, error) {
	db, err := sql.Open("sqlite", "file:"+path+"?_pragma=busy_timeout(5000)&_pragma=journal_mode(WAL)&_pragma=foreign_keys(1)")
	if err != nil {
		return nil, fmt.Errorf("打开账号库失败: %w", err)
	}
	if err := db.Ping(); err != nil {
		db.Close()
		return nil, fmt.Errorf("连接账号库失败: %w", err)
	}
	return &SQLiteStore{db: db}, nil
}

// OpenWithDB 用既有连接构建（与问题存储共用连接池时使用）。
func OpenWithDB(db *sql.DB) *SQLiteStore { return &SQLiteStore{db: db} }

// Init 建表；issue_owners 依赖 issues 表，须在问题库初始化之后执行。
func (s *SQLiteStore) Init(ctx context.Context) error {
	if _, err := s.db.ExecContext(ctx, authSchema); err != nil {
		return fmt.Errorf("建立账号/会话表失败: %w", err)
	}
	return nil
}

// Close 关闭连接。
func (s *SQLiteStore) Close() error { return s.db.Close() }

// UpsertAccount 按用户名幂等建立或更新账号。
func (s *SQLiteStore) UpsertAccount(ctx context.Context, username, displayName string, role Role, passwordHash string, now string) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("开启账号事务失败: %w", err)
	}
	defer tx.Rollback()

	var id string
	err = tx.QueryRowContext(ctx, `SELECT id FROM users WHERE username = ?`, username).Scan(&id)
	switch {
	case errors.Is(err, sql.ErrNoRows):
		if _, err := tx.ExecContext(ctx, `
			INSERT INTO users (id, username, display_name, role, password_hash, created_at, updated_at)
			VALUES (?, ?, ?, ?, ?, ?, ?)
		`, UserID(username), username, displayName, role, passwordHash, now, now); err != nil {
			return fmt.Errorf("创建账号失败: %w", err)
		}
	case err != nil:
		return fmt.Errorf("查询账号失败: %w", err)
	default:
		if _, err := tx.ExecContext(ctx, `
			UPDATE users SET display_name = ?, role = ?, password_hash = ?, updated_at = ? WHERE id = ?
		`, displayName, role, passwordHash, now, id); err != nil {
			return fmt.Errorf("更新账号失败: %w", err)
		}
	}
	return tx.Commit()
}

// FindByUsername 按用户名查询账号；不存在时返回 ErrInvalidCredentials 语义错误，
// 与密码错误不可区分，避免暴露账号是否存在。
func (s *SQLiteStore) FindByUsername(ctx context.Context, username string) (User, error) {
	var u User
	err := s.db.QueryRowContext(ctx, `
		SELECT id, username, display_name, role, password_hash, created_at, updated_at
		FROM users WHERE username = ?
	`, username).Scan(&u.ID, &u.Username, &u.DisplayName, &u.Role, &u.PasswordHash, &u.CreatedAt, &u.UpdatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return User{}, ErrInvalidCredentials
	}
	if err != nil {
		return User{}, fmt.Errorf("查询账号失败: %w", err)
	}
	return u, nil
}

// CreateSession 写入新会话记录。
func (s *SQLiteStore) CreateSession(ctx context.Context, sess Session) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO sessions (token_hash, user_id, created_at, expires_at, revoked_at)
		VALUES (?, ?, ?, ?, NULL)
	`, sess.TokenHash, sess.UserID, sess.CreatedAt, sess.ExpiresAt)
	if err != nil {
		return fmt.Errorf("写入会话失败: %w", err)
	}
	return nil
}

// FindSession 按令牌摘要查询会话与账号。
func (s *SQLiteStore) FindSession(ctx context.Context, tokenHash string) (Session, User, error) {
	var (
		sess      Session
		revokedAt sql.NullString
		u         User
	)
	err := s.db.QueryRowContext(ctx, `
		SELECT s.token_hash, s.user_id, s.created_at, s.expires_at, s.revoked_at,
		       u.id, u.username, u.display_name, u.role, u.password_hash, u.created_at, u.updated_at
		FROM sessions s JOIN users u ON u.id = s.user_id
		WHERE s.token_hash = ?
	`, tokenHash).Scan(&sess.TokenHash, &sess.UserID, &sess.CreatedAt, &sess.ExpiresAt, &revokedAt,
		&u.ID, &u.Username, &u.DisplayName, &u.Role, &u.PasswordHash, &u.CreatedAt, &u.UpdatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return Session{}, User{}, ErrSessionNotFound
	}
	if err != nil {
		return Session{}, User{}, fmt.Errorf("查询会话失败: %w", err)
	}
	if revokedAt.Valid {
		revoked := revokedAt.String
		sess.RevokedAt = &revoked
	}
	return sess, u, nil
}

// RevokeSession 撤销会话；目标不存在不算错误（重复退出幂等）。
func (s *SQLiteStore) RevokeSession(ctx context.Context, tokenHash string, now string) error {
	_, err := s.db.ExecContext(ctx, `UPDATE sessions SET revoked_at = ? WHERE token_hash = ? AND revoked_at IS NULL`, now, tokenHash)
	if err != nil {
		return fmt.Errorf("撤销会话失败: %w", err)
	}
	return nil
}

// DeleteExpiredSessions 清理过期会话。
func (s *SQLiteStore) DeleteExpiredSessions(ctx context.Context, now string) error {
	if _, err := s.db.ExecContext(ctx, `DELETE FROM sessions WHERE expires_at <= ?`, now); err != nil {
		return fmt.Errorf("清理过期会话失败: %w", err)
	}
	return nil
}

// ---- 计划 008：人员管理存储实现 ----

// sqliteErrCode 提取 modernc.org/sqlite 错误码；非驱动错误返回 0。
func sqliteErrCode(err error) int {
	var coded interface{ Code() int }
	if errors.As(err, &coded) {
		return coded.Code()
	}
	return 0
}

// sqliteConstraintUnique 为 UNIQUE 约束冲突的主错误码（2067 = SQLITE_CONSTRAINT_UNIQUE）。
const sqliteConstraintUnique = 2067

// CountUsers 返回账号总数。
func (s *SQLiteStore) CountUsers(ctx context.Context) (int, error) {
	var n int
	if err := s.db.QueryRowContext(ctx, `SELECT COUNT(*) FROM users`).Scan(&n); err != nil {
		return 0, fmt.Errorf("统计账号失败: %w", err)
	}
	return n, nil
}

// ListUsers 返回全部账号，按用户名排序。
func (s *SQLiteStore) ListUsers(ctx context.Context) ([]User, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, username, display_name, role, password_hash, created_at, updated_at
		FROM users ORDER BY username ASC
	`)
	if err != nil {
		return nil, fmt.Errorf("查询账号列表失败: %w", err)
	}
	defer rows.Close()
	var users []User
	for rows.Next() {
		var u User
		if err := rows.Scan(&u.ID, &u.Username, &u.DisplayName, &u.Role, &u.PasswordHash, &u.CreatedAt, &u.UpdatedAt); err != nil {
			return nil, fmt.Errorf("读取账号行失败: %w", err)
		}
		users = append(users, u)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("遍历账号列表失败: %w", err)
	}
	return users, nil
}

// CreateUser 创建账号；用户名 UNIQUE 冲突映射为领域错误 ErrUsernameExists。
func (s *SQLiteStore) CreateUser(ctx context.Context, username, displayName, passwordHash, now string) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO users (id, username, display_name, role, password_hash, created_at, updated_at)
		VALUES (?, ?, ?, ?, ?, ?, ?)
	`, UserID(username), username, displayName, RoleInspector, passwordHash, now, now)
	if sqliteErrCode(err) == sqliteConstraintUnique {
		return ErrUsernameExists
	}
	if err != nil {
		return fmt.Errorf("创建账号失败: %w", err)
	}
	return nil
}

// findUserTx 事务内按用户名查询账号行（含 id）；不存在返回 sql.ErrNoRows。
func findUserTx(ctx context.Context, tx *sql.Tx, username string) (User, error) {
	var u User
	err := tx.QueryRowContext(ctx, `
		SELECT id, username, display_name, role, password_hash, created_at, updated_at
		FROM users WHERE username = ?
	`, username).Scan(&u.ID, &u.Username, &u.DisplayName, &u.Role, &u.PasswordHash, &u.CreatedAt, &u.UpdatedAt)
	return u, err
}

// guardManagedTarget 校验管理操作目标：不存在 → ErrUserNotFound；管理员 → ErrAdminProtected。
func guardManagedTarget(u User, err error) error {
	if errors.Is(err, sql.ErrNoRows) {
		return ErrUserNotFound
	}
	if err != nil {
		return fmt.Errorf("查询目标账号失败: %w", err)
	}
	if u.Role == RoleAdmin {
		return ErrAdminProtected
	}
	return nil
}

// FindManagedUser 按用户名查询账号；不存在返回 ErrUserNotFound。
func (s *SQLiteStore) FindManagedUser(ctx context.Context, username string) (User, error) {
	var u User
	err := s.db.QueryRowContext(ctx, `
		SELECT id, username, display_name, role, password_hash, created_at, updated_at
		FROM users WHERE username = ?
	`, username).Scan(&u.ID, &u.Username, &u.DisplayName, &u.Role, &u.PasswordHash, &u.CreatedAt, &u.UpdatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return User{}, ErrUserNotFound
	}
	if err != nil {
		return User{}, fmt.Errorf("查询账号失败: %w", err)
	}
	return u, nil
}

// UpdateUserFields 按用户名更新显示名与/或密码；resetSessions 时同事务清会话。
func (s *SQLiteStore) UpdateUserFields(ctx context.Context, username string, displayName *string, passwordHash string, resetSessions bool, now string) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("开启账号更新事务失败: %w", err)
	}
	defer tx.Rollback()

	u, err := findUserTx(ctx, tx, username)
	if err := guardManagedTarget(u, err); err != nil {
		return err
	}
	if displayName != nil {
		if _, err := tx.ExecContext(ctx, `UPDATE users SET display_name = ?, updated_at = ? WHERE id = ?`, *displayName, now, u.ID); err != nil {
			return fmt.Errorf("更新显示名失败: %w", err)
		}
	}
	if passwordHash != "" {
		if _, err := tx.ExecContext(ctx, `UPDATE users SET password_hash = ?, updated_at = ? WHERE id = ?`, passwordHash, now, u.ID); err != nil {
			return fmt.Errorf("更新密码失败: %w", err)
		}
	}
	if resetSessions {
		if _, err := tx.ExecContext(ctx, `DELETE FROM sessions WHERE user_id = ?`, u.ID); err != nil {
			return fmt.Errorf("清除账号会话失败: %w", err)
		}
	}
	return tx.Commit()
}

// DeleteUser 删除巡检员账号：清归属 → 清会话 → 删账号，同一事务，任一步失败回滚。
func (s *SQLiteStore) DeleteUser(ctx context.Context, username string) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("开启账号删除事务失败: %w", err)
	}
	defer tx.Rollback()

	u, err := findUserTx(ctx, tx, username)
	if err := guardManagedTarget(u, err); err != nil {
		return err
	}
	if _, err := tx.ExecContext(ctx, `DELETE FROM issue_owners WHERE user_id = ?`, u.ID); err != nil {
		return fmt.Errorf("清除问题归属记录失败: %w", err)
	}
	if _, err := tx.ExecContext(ctx, `DELETE FROM sessions WHERE user_id = ?`, u.ID); err != nil {
		return fmt.Errorf("清除账号会话失败: %w", err)
	}
	if _, err := tx.ExecContext(ctx, `DELETE FROM users WHERE id = ?`, u.ID); err != nil {
		return fmt.Errorf("删除账号失败: %w", err)
	}
	return tx.Commit()
}

// NewToken 生成 32 字节随机令牌（hex 编码）与对应的 HMAC 摘要。
// secret 为空时退化为裸 SHA-256（仅测试用；生产启动强制要求 secret）。
func NewToken(secret []byte) (token, tokenHash string, err error) {
	raw := make([]byte, 32)
	if _, err = rand.Read(raw); err != nil {
		return "", "", fmt.Errorf("生成会话令牌失败: %w", err)
	}
	token = hex.EncodeToString(raw)
	return token, HashToken(secret, token), nil
}

// HashToken 计算令牌摘要；secret 非空时为 HMAC-SHA256，否则为裸 SHA-256。
func HashToken(secret []byte, token string) string {
	if len(secret) > 0 {
		mac := hmac.New(sha256.New, secret)
		mac.Write([]byte(token))
		return hex.EncodeToString(mac.Sum(nil))
	}
	sum := sha256.Sum256([]byte(token))
	return hex.EncodeToString(sum[:])
}
