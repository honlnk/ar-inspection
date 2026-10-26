package issue

import (
	"context"
	"database/sql"
	"embed"
	"fmt"
	"os"
	"path/filepath"
	"time"

	_ "modernc.org/sqlite" // 纯 Go SQLite 驱动，注册 database/sql 的 "sqlite" 驱动名
)

//go:embed schema.sql
var schemaFS embed.FS

// SQLiteStore 是 Store 的 SQLite 实现，通过 database/sql 访问，不引入 ORM。
type SQLiteStore struct {
	db     *sql.DB
	dbPath string
}

// sqliteDSN 统一连接参数：busy_timeout 缓解写锁竞争，WAL 提升重启与并发读取的稳定性。
func sqliteDSN(path string) string {
	return "file:" + path + "?_pragma=busy_timeout(5000)&_pragma=journal_mode(WAL)"
}

// OpenSQLite 打开（必要时创建）数据库文件并创建父目录。
func OpenSQLite(path string) (*SQLiteStore, error) {
	if dir := filepath.Dir(path); dir != "" && dir != "." {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return nil, fmt.Errorf("创建数据库目录失败: %w", err)
		}
	}
	db, err := sql.Open("sqlite", sqliteDSN(path))
	if err != nil {
		return nil, fmt.Errorf("打开 SQLite 失败: %w", err)
	}
	return &SQLiteStore{db: db, dbPath: path}, nil
}

// Init 执行建表 SQL 并做轻量检查，保证全新环境可自动初始化。
func (s *SQLiteStore) Init(ctx context.Context) error {
	schema, err := schemaFS.ReadFile("schema.sql")
	if err != nil {
		return fmt.Errorf("读取建表 SQL 失败: %w", err)
	}
	if _, err := s.db.ExecContext(ctx, string(schema)); err != nil {
		return fmt.Errorf("执行建表 SQL 失败: %w", err)
	}
	return s.HealthCheck(ctx)
}

// HealthCheck 做真实读库检查，只探测连接（Ping）无法发现主文件内容损坏：
// 先在现有连接上读取 SQLite 系统表（覆盖连接已关闭、被锁等情况），
// 再用全新连接读一次——复用旧连接会命中 SQLite 页缓存，检测不到运行中主文件被替换。
func (s *SQLiteStore) HealthCheck(ctx context.Context) error {
	checkCtx, cancel := context.WithTimeout(ctx, 3*time.Second)
	defer cancel()
	var tableCount int
	if err := s.db.QueryRowContext(checkCtx, `SELECT COUNT(*) FROM sqlite_master`).Scan(&tableCount); err != nil {
		return fmt.Errorf("存储健康检查失败: %w", err)
	}
	return s.probeWithFreshConnection(ctx)
}

// probeWithFreshConnection 用独立的新连接读取 SQLite 系统表，确认主文件内容可解析。
func (s *SQLiteStore) probeWithFreshConnection(ctx context.Context) error {
	db, err := sql.Open("sqlite", sqliteDSN(s.dbPath))
	if err != nil {
		return fmt.Errorf("打开健康检查探测连接失败: %w", err)
	}
	defer db.Close()

	probeCtx, cancel := context.WithTimeout(ctx, 3*time.Second)
	defer cancel()
	var tableCount int
	if err := db.QueryRowContext(probeCtx, `SELECT COUNT(*) FROM sqlite_master`).Scan(&tableCount); err != nil {
		return fmt.Errorf("存储健康检查失败: %w", err)
	}
	return nil
}

// List 查询全部问题，按 created_at 从新到旧、同时间按 id 排序。
func (s *SQLiteStore) List(ctx context.Context) ([]Issue, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, title, description, priority, status,
		       position_x, position_y, position_z, created_at, updated_at
		FROM issues
		ORDER BY created_at DESC, id
	`)
	if err != nil {
		return nil, fmt.Errorf("查询问题列表失败: %w", err)
	}
	defer rows.Close()

	issues := make([]Issue, 0)
	for rows.Next() {
		var it Issue
		if err := rows.Scan(
			&it.ID, &it.Title, &it.Description, &it.Priority, &it.Status,
			&it.Position.X, &it.Position.Y, &it.Position.Z,
			&it.CreatedAt, &it.UpdatedAt,
		); err != nil {
			return nil, fmt.Errorf("读取问题记录失败: %w", err)
		}
		issues = append(issues, it)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("遍历问题列表失败: %w", err)
	}
	return issues, nil
}

// Close 关闭数据库连接。
func (s *SQLiteStore) Close() error {
	return s.db.Close()
}
