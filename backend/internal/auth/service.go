package auth

import (
	"context"
	"crypto/subtle"
	"fmt"
	"strings"
	"time"

	"golang.org/x/crypto/bcrypt"
)

// Clock 抽象时间来源，测试可注入固定时钟验证过期行为。
type Clock interface {
	Now() time.Time
}

type realClock struct{}

func (realClock) Now() time.Time { return time.Now() }

// NowString 返回带北京时间偏移的 RFC 3339 字符串。
func NowString(t time.Time) string {
	return t.In(Beijing).Format(time.RFC3339)
}

// Beijing 业务统一时区（UTC+08:00）。
var Beijing = time.FixedZone("Asia/Shanghai", 8*3600)

// Service 账号与会话的领域服务：登录校验、令牌签发与解析、账号初始化。
type Service struct {
	store  Store
	secret []byte
	clock  Clock
}

// NewService 构建服务；secret 为会话密钥（生产必须非空）。
func NewService(store Store, secret []byte) *Service {
	return &Service{store: store, secret: secret, clock: realClock{}}
}

// SetClock 替换时间来源（测试用）。
func (s *Service) SetClock(c Clock) { s.clock = c }

// EnsureAccounts 幂等建立或更新两个本地演示账号。
// 任一密码为空即返回错误：不为开发环境设置可猜测的默认密码。
func (s *Service) EnsureAccounts(ctx context.Context, inspectorPassword, adminPassword string) error {
	if strings.TrimSpace(inspectorPassword) == "" || strings.TrimSpace(adminPassword) == "" {
		return fmt.Errorf("缺少演示账号密码环境变量（INSPECTOR_PASSWORD / ADMIN_PASSWORD），拒绝启动")
	}
	now := NowString(s.clock.Now())
	accounts := []struct {
		username, displayName string
		role                 Role
		password             string
	}{
		{"inspector", "巡检员", RoleInspector, inspectorPassword},
		{"admin", "管理员", RoleAdmin, adminPassword},
	}
	for _, a := range accounts {
		hash, err := bcrypt.GenerateFromPassword([]byte(a.password), bcrypt.DefaultCost)
		if err != nil {
			return fmt.Errorf("生成密码哈希失败: %w", err)
		}
		if err := s.store.UpsertAccount(ctx, a.username, a.displayName, a.role, string(hash), now); err != nil {
			return err
		}
	}
	return nil
}

// CleanupExpiredSessions 清理过期会话记录；失败只记日志语义，不阻断启动。
func (s *Service) CleanupExpiredSessions(ctx context.Context) {
	_ = s.store.DeleteExpiredSessions(ctx, NowString(s.clock.Now()))
}

// Login 校验用户名密码；成功签发新会话并返回令牌与账号公开信息。
// 用户名不存在与密码错误返回同一错误，不暴露账号是否存在。
func (s *Service) Login(ctx context.Context, username, password string) (string, Public, error) {
	username = strings.TrimSpace(username)
	if username == "" || password == "" {
		return "", Public{}, ErrInvalidCredentials
	}
	user, err := s.store.FindByUsername(ctx, username)
	if err != nil {
		// 用户不存在时也执行一次哈希比较，使两条路径耗时接近，不通过时间侧信道暴露账号存在性
		bcrypt.CompareHashAndPassword([]byte("$2a$10$7EqJtq98hPqEX7fNZaFWoOhi5B0C4rP7dyNzXhbbXVEXAMPLEuGXu"), []byte(password))
		return "", Public{}, err
	}
	if bcrypt.CompareHashAndPassword([]byte(user.PasswordHash), []byte(password)) != nil {
		return "", Public{}, ErrInvalidCredentials
	}
	token, tokenHash, err := NewToken(s.secret)
	if err != nil {
		return "", Public{}, err
	}
	now := s.clock.Now()
	sess := Session{
		TokenHash: tokenHash,
		UserID:    user.ID,
		CreatedAt: NowString(now),
		ExpiresAt: NowString(now.Add(SessionTTL)),
	}
	if err := s.store.CreateSession(ctx, sess); err != nil {
		return "", Public{}, err
	}
	return token, user.ToPublic(), nil
}

// Resolve 解析令牌：会话必须存在、未过期且未撤销。
// 过期与撤销统一返回 ErrSessionNotFound，不区分原因。
func (s *Service) Resolve(ctx context.Context, token string) (Public, error) {
	if token == "" {
		return Public{}, ErrSessionNotFound
	}
	sess, user, err := s.store.FindSession(ctx, HashToken(s.secret, token))
	if err != nil {
		return Public{}, err
	}
	if sess.RevokedAt != nil {
		return Public{}, ErrSessionNotFound
	}
	expires, err := time.Parse(time.RFC3339, sess.ExpiresAt)
	if err != nil || !s.clock.Now().Before(expires) {
		return Public{}, ErrSessionNotFound
	}
	return user.ToPublic(), nil
}

// Logout 撤销令牌对应会话；令牌无效时幂等成功。
func (s *Service) Logout(ctx context.Context, token string) error {
	if token == "" {
		return nil
	}
	return s.store.RevokeSession(ctx, HashToken(s.secret, token), NowString(s.clock.Now()))
}

// ConstantTimeEqual 常量时间字符串比较（测试辅助）。
func ConstantTimeEqual(a, b string) bool {
	return subtle.ConstantTimeCompare([]byte(a), []byte(b)) == 1
}
