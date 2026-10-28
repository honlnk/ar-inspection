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

// EnsureAccounts 建立本地演示账号（计划 008 调整语义）：
//   - 管理员：每次启动按环境变量同步显示名与密码（忘记密码时改环境文件重启即可恢复）；
//   - 巡检员：仅库中无任何账号（首次启动）时创建一次；此后完全由人员管理页维护，
//     删除后重启不复活。
//
// 任一密码为空即返回错误：不为开发环境设置可猜测的默认密码。
func (s *Service) EnsureAccounts(ctx context.Context, inspectorPassword, adminPassword string) error {
	if strings.TrimSpace(inspectorPassword) == "" || strings.TrimSpace(adminPassword) == "" {
		return fmt.Errorf("缺少演示账号密码环境变量（INSPECTOR_PASSWORD / ADMIN_PASSWORD），拒绝启动")
	}
	now := NowString(s.clock.Now())

	// 巡检员仅首启创建：判定必须先于管理员写入，否则计数永远大于 0。
	// 库中已有任何账号（含巡检员被删、只剩管理员）都不再插手。
	count, err := s.store.CountUsers(ctx)
	if err != nil {
		return err
	}
	if count == 0 {
		inspHash, err := bcrypt.GenerateFromPassword([]byte(inspectorPassword), bcrypt.DefaultCost)
		if err != nil {
			return fmt.Errorf("生成密码哈希失败: %w", err)
		}
		if err := s.store.UpsertAccount(ctx, "inspector", "巡检员", RoleInspector, string(inspHash), now); err != nil {
			return err
		}
	}

	// 管理员保持每次启动同步
	adminHash, err := bcrypt.GenerateFromPassword([]byte(adminPassword), bcrypt.DefaultCost)
	if err != nil {
		return fmt.Errorf("生成密码哈希失败: %w", err)
	}
	return s.store.UpsertAccount(ctx, "admin", "管理员", RoleAdmin, string(adminHash), now)
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
	return s.issueSession(ctx, user)
}

// QuickLogin 为开发期快捷登录签发会话：按用户名查找账号，不校验密码。
// 仅在 HTTP 层显式启用 QUICK_LOGIN_ENABLED 时才会被调用，且 HTTP 层只允许
// 内置演示账号 inspector/admin；账号已被删除时返回 ErrQuickLoginUnavailable。
func (s *Service) QuickLogin(ctx context.Context, username string) (string, Public, error) {
	user, err := s.store.FindByUsername(ctx, strings.TrimSpace(username))
	if err != nil {
		return "", Public{}, ErrQuickLoginUnavailable
	}
	return s.issueSession(ctx, user)
}

// issueSession 为已确认身份的账号签发新会话（常规登录与开发快捷登录共用）。
func (s *Service) issueSession(ctx context.Context, user User) (string, Public, error) {
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

// ---- 计划 008：人员管理服务层 ----

// ListManagedAccounts 返回全部账号的管理视图（按用户名排序），不含密码哈希。
func (s *Service) ListManagedAccounts(ctx context.Context) ([]ManagedAccount, error) {
	users, err := s.store.ListUsers(ctx)
	if err != nil {
		return nil, err
	}
	accounts := make([]ManagedAccount, 0, len(users))
	for _, u := range users {
		accounts = append(accounts, ManagedAccount{
			Username:    u.Username,
			DisplayName: u.DisplayName,
			Role:        u.Role,
			CreatedAt:   u.CreatedAt,
			UpdatedAt:   u.UpdatedAt,
		})
	}
	return accounts, nil
}

// CreateInspector 创建巡检员账号：领域校验（4.3 节规则）通过后写库；
// 成功后新账号立即可登录，无需重启。返回新账号的管理视图。
func (s *Service) CreateInspector(ctx context.Context, username, displayName, password string) (ManagedAccount, error) {
	username = strings.TrimSpace(username)
	if err := ValidateUsername(username); err != nil {
		return ManagedAccount{}, err
	}
	if err := ValidateDisplayName(displayName); err != nil {
		return ManagedAccount{}, err
	}
	if err := ValidatePassword(password); err != nil {
		return ManagedAccount{}, err
	}
	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return ManagedAccount{}, fmt.Errorf("生成密码哈希失败: %w", err)
	}
	now := NowString(s.clock.Now())
	if err := s.store.CreateUser(ctx, username, strings.TrimSpace(displayName), string(hash), now); err != nil {
		return ManagedAccount{}, err
	}
	created, err := s.store.FindManagedUser(ctx, username)
	if err != nil {
		return ManagedAccount{}, err
	}
	return ManagedAccount{
		Username:    created.Username,
		DisplayName: created.DisplayName,
		Role:        created.Role,
		CreatedAt:   created.CreatedAt,
		UpdatedAt:   created.UpdatedAt,
	}, nil
}

// UpdateInspector 修改巡检员：用户名为路径目标，显示名与/或密码至少一项；
// 重置密码时清除该账号全部会话（旧令牌下次请求即 401，等价立即踢下线）。
// 返回更新后的管理视图。
func (s *Service) UpdateInspector(ctx context.Context, targetUsername string, displayName, password *string) (ManagedAccount, error) {
	if displayName == nil && password == nil {
		return ManagedAccount{}, &ValidationError{Message: "至少提供显示名或密码一项修改"}
	}
	var namePtr *string
	if displayName != nil {
		if err := ValidateDisplayName(*displayName); err != nil {
			return ManagedAccount{}, err
		}
		trimmed := strings.TrimSpace(*displayName)
		namePtr = &trimmed
	}
	var hash string
	resetSessions := false
	if password != nil {
		if err := ValidatePassword(*password); err != nil {
			return ManagedAccount{}, err
		}
		h, err := bcrypt.GenerateFromPassword([]byte(*password), bcrypt.DefaultCost)
		if err != nil {
			return ManagedAccount{}, fmt.Errorf("生成密码哈希失败: %w", err)
		}
		hash = string(h)
		resetSessions = true
	}
	if err := s.store.UpdateUserFields(ctx, targetUsername, namePtr, hash, resetSessions, NowString(s.clock.Now())); err != nil {
		return ManagedAccount{}, err
	}
	updated, err := s.store.FindManagedUser(ctx, targetUsername)
	if err != nil {
		return ManagedAccount{}, err
	}
	return ManagedAccount{
		Username:    updated.Username,
		DisplayName: updated.DisplayName,
		Role:        updated.Role,
		CreatedAt:   updated.CreatedAt,
		UpdatedAt:   updated.UpdatedAt,
	}, nil
}

// DeleteInspector 删除巡检员账号（含其全部会话与问题归属映射）；
// 问题本体保留，归属转为未知（仅管理员可见），与历史遗留数据同语义。
func (s *Service) DeleteInspector(ctx context.Context, username string) error {
	return s.store.DeleteUser(ctx, username)
}

// ConstantTimeEqual 常量时间字符串比较（测试辅助）。
func ConstantTimeEqual(a, b string) bool {
	return subtle.ConstantTimeCompare([]byte(a), []byte(b)) == 1
}
