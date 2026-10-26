package issue

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

// newTempStore 在临时目录创建数据库并完成初始化，测试结束后自动关闭。
func newTempStore(t *testing.T) *SQLiteStore {
	t.Helper()
	store, err := OpenSQLite(filepath.Join(t.TempDir(), "inspection.db"))
	if err != nil {
		t.Fatalf("打开临时数据库失败: %v", err)
	}
	t.Cleanup(func() { _ = store.Close() })
	if err := store.Init(context.Background()); err != nil {
		t.Fatalf("初始化临时数据库失败: %v", err)
	}
	return store
}

// TestOpenSQLite_CreatesParentDir 验证打开不存在的嵌套路径时会自动创建父目录和数据库文件。
func TestOpenSQLite_CreatesParentDir(t *testing.T) {
	dbPath := filepath.Join(t.TempDir(), "data", "nested", "inspection.db")
	store, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("打开数据库失败: %v", err)
	}
	defer store.Close()

	if err := store.Init(context.Background()); err != nil {
		t.Fatalf("初始化数据库失败: %v", err)
	}
	if _, err := os.Stat(dbPath); err != nil {
		t.Fatalf("数据库文件未创建: %v", err)
	}
}

// TestInit_CreatesTableAndIndex 验证全新数据库自动建表和查询索引。
func TestInit_CreatesTableAndIndex(t *testing.T) {
	store := newTempStore(t)

	var tableCount, indexCount int
	if err := store.db.QueryRow(
		`SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'issues'`,
	).Scan(&tableCount); err != nil {
		t.Fatalf("查询 sqlite_master 失败: %v", err)
	}
	if tableCount != 1 {
		t.Fatalf("issues 表未创建，tableCount = %d", tableCount)
	}
	if err := store.db.QueryRow(
		`SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_issues_created_at_id'`,
	).Scan(&indexCount); err != nil {
		t.Fatalf("查询索引失败: %v", err)
	}
	if indexCount != 1 {
		t.Fatalf("idx_issues_created_at_id 索引未创建，indexCount = %d", indexCount)
	}
}

// TestList_EmptyDatabaseReturnsEmptySlice 验证空库返回空切片而非 nil。
func TestList_EmptyDatabaseReturnsEmptySlice(t *testing.T) {
	store := newTempStore(t)

	issues, err := store.List(context.Background())
	if err != nil {
		t.Fatalf("查询空库列表失败: %v", err)
	}
	if issues == nil {
		t.Fatal("空库应返回空切片，不应返回 nil")
	}
	if len(issues) != 0 {
		t.Fatalf("空库应返回 0 条，实际 %d 条", len(issues))
	}
}

// TestReopen_PersistsAcrossReconnect 验证关闭连接后重新打开同一文件，数据仍在（重启持久化基础）。
func TestReopen_PersistsAcrossReconnect(t *testing.T) {
	dbPath := filepath.Join(t.TempDir(), "inspection.db")

	first, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("首次打开数据库失败: %v", err)
	}
	if err := first.Init(context.Background()); err != nil {
		t.Fatalf("首次初始化失败: %v", err)
	}
	// 本期尚无创建接口，直接写入一条样例验证持久化与字段映射。
	_, err = first.db.ExecContext(context.Background(), `
		INSERT INTO issues (id, title, description, priority, status,
		                    position_x, position_y, position_z, created_at, updated_at)
		VALUES ('issue-1', '入口墙面破损', '左侧墙体存在裂缝', 'high', 'open',
		        0.42, 0.03, 1.26,
		        '2026-10-27T10:00:00+08:00', '2026-10-27T10:00:00+08:00')
	`)
	if err != nil {
		t.Fatalf("写入样例数据失败: %v", err)
	}
	if err := first.Close(); err != nil {
		t.Fatalf("关闭数据库失败: %v", err)
	}

	second, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("重新打开数据库失败: %v", err)
	}
	defer second.Close()
	if err := second.Init(context.Background()); err != nil {
		t.Fatalf("重新初始化失败: %v", err)
	}

	issues, err := second.List(context.Background())
	if err != nil {
		t.Fatalf("重连后查询失败: %v", err)
	}
	if len(issues) != 1 {
		t.Fatalf("重连后应仍有 1 条数据，实际 %d 条", len(issues))
	}
	got := issues[0]
	if got.ID != "issue-1" || got.Title != "入口墙面破损" || got.Description != "左侧墙体存在裂缝" {
		t.Fatalf("文本字段回读不一致: %+v", got)
	}
	if got.Priority != PriorityHigh || got.Status != StatusOpen {
		t.Fatalf("枚举字段回读不一致: priority=%s status=%s", got.Priority, got.Status)
	}
	if got.Position.X != 0.42 || got.Position.Y != 0.03 || got.Position.Z != 1.26 {
		t.Fatalf("坐标回读不一致: %+v", got.Position)
	}
	if got.CreatedAt != "2026-10-27T10:00:00+08:00" || got.UpdatedAt != "2026-10-27T10:00:00+08:00" {
		t.Fatalf("时间字符串未原样保留: createdAt=%s updatedAt=%s", got.CreatedAt, got.UpdatedAt)
	}
}
