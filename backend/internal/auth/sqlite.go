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
