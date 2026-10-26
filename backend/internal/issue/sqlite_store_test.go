package issue

import (
	"context"
	"errors"
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

// TestHealthCheck_CorruptedFileDetected 验证数据库主文件损坏且 WAL 无有效数据时，
// 无论连接是否保持打开，健康检查都必须返回错误，而不是误报存储正常。
// 说明：WAL 模式下仅覆盖主文件而 -wal 仍完好时，SQLite 会继续从 WAL 提供数据，
// 主连接关闭时 checkpoint 还会把有效数据写回主文件——那不是可检出的存储故障。
func TestHealthCheck_CorruptedFileDetected(t *testing.T) {
	dbPath := filepath.Join(t.TempDir(), "inspection.db")
	store, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("打开数据库失败: %v", err)
	}
	if err := store.Init(context.Background()); err != nil {
		t.Fatalf("初始化数据库失败: %v", err)
	}

	// 场景一：运行中主文件被损坏且 WAL 丢失。此时旧连接的页缓存仍可能
	// 让既有连接上的查询"看起来正常"，必须靠全新连接才能发现。
	if err := os.WriteFile(dbPath, []byte("这不是 SQLite 数据库：主文件内容已损坏。"), 0o644); err != nil {
		t.Fatalf("覆盖数据库文件失败: %v", err)
	}
	for _, suffix := range []string{"-wal", "-shm"} {
		if err := os.Remove(dbPath + suffix); err != nil && !os.IsNotExist(err) {
			t.Fatalf("移除 %s 失败: %v", suffix, err)
		}
	}
	if err := store.HealthCheck(context.Background()); err == nil {
		t.Fatal("运行中主文件损坏且 WAL 丢失，健康检查应返回错误")
	}

	// 关闭旧连接；此时主文件已是损坏内容，重新打开模拟重启。
	if err := store.Close(); err != nil {
		t.Fatalf("关闭数据库失败: %v", err)
	}
	if err := os.WriteFile(dbPath, []byte("corrupted: not a sqlite database"), 0o644); err != nil {
		t.Fatalf("再次覆盖数据库文件失败: %v", err)
	}

	// 场景二：带着损坏的主文件重启进程。
	reopened, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("重新打开损坏数据库失败: %v", err)
	}
	defer reopened.Close()
	if err := reopened.HealthCheck(context.Background()); err == nil {
		t.Fatal("重启后主文件损坏，健康检查应返回错误")
	}
	if err := reopened.Init(context.Background()); err == nil {
		t.Fatal("损坏数据库上执行初始化也应失败，不得把建表失败当作可用存储")
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
	// 直接写入一条固定样例，独立验证列名与 JSON 字段的映射及重启持久化基础。
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

// buildTestIssue 构造带固定业务字段的测试问题，服务端字段由参数注入。
func buildTestIssue(id string) Issue {
	return Issue{
		ID:          id,
		Title:       "入口墙面破损",
		Description: "左侧墙体存在裂缝",
		Priority:    PriorityHigh,
		Status:      StatusOpen,
		Position:    Position{X: 0.42, Y: 0.03, Z: 1.26},
		CreatedAt:   "2026-10-27T10:00:00+08:00",
		UpdatedAt:   "2026-10-27T10:00:00+08:00",
	}
}

// TestOpen_PragmasApplied 验证连接参数真实生效：WAL 日志模式与 5000ms busy timeout
// （计划阶段 2：只记录实测生效的参数，不虚构配置）。
func TestOpen_PragmasApplied(t *testing.T) {
	store := newTempStore(t)

	var journalMode string
	if err := store.db.QueryRow(`PRAGMA journal_mode`).Scan(&journalMode); err != nil {
		t.Fatalf("读取 journal_mode 失败: %v", err)
	}
	if journalMode != "wal" {
		t.Fatalf("期望 journal_mode=wal，实际 %q", journalMode)
	}
	var busyTimeout int
	if err := store.db.QueryRow(`PRAGMA busy_timeout`).Scan(&busyTimeout); err != nil {
		t.Fatalf("读取 busy_timeout 失败: %v", err)
	}
	if busyTimeout != 5000 {
		t.Fatalf("期望 busy_timeout=5000，实际 %d", busyTimeout)
	}
}

// TestCreate_ReturnsCommittedRecord 验证创建后返回提交入库的完整记录。
func TestCreate_ReturnsCommittedRecord(t *testing.T) {
	store := newTempStore(t)

	want := buildTestIssue("issue-create-1")
	got, err := store.Create(context.Background(), want)
	if err != nil {
		t.Fatalf("创建问题失败: %v", err)
	}
	if got != want {
		t.Fatalf("返回记录与写入不一致:\n得到 %+v\n期望 %+v", got, want)
	}

	issues, err := store.List(context.Background())
	if err != nil {
		t.Fatalf("查询列表失败: %v", err)
	}
	if len(issues) != 1 || issues[0] != want {
		t.Fatalf("列表应包含刚创建的记录，实际 %+v", issues)
	}
}

// TestCreate_DuplicateIDFailsNoPartial 验证写入失败（主键冲突）时事务回滚，
// 不留下部分成功结果，原有记录不被覆盖。
func TestCreate_DuplicateIDFailsNoPartial(t *testing.T) {
	store := newTempStore(t)

	first := buildTestIssue("issue-dup")
	if _, err := store.Create(context.Background(), first); err != nil {
		t.Fatalf("首次创建失败: %v", err)
	}
	second := buildTestIssue("issue-dup")
	second.Title = "另一条同 ID 记录"
	if _, err := store.Create(context.Background(), second); err == nil {
		t.Fatal("重复 ID 创建必须失败")
	}

	issues, err := store.List(context.Background())
	if err != nil {
		t.Fatalf("查询列表失败: %v", err)
	}
	if len(issues) != 1 || issues[0].Title != first.Title {
		t.Fatalf("失败写入不应留下部分结果，实际 %+v", issues)
	}
}

// TestCreate_ClosedStoreFailsCleanly 验证存储故障（连接关闭）时创建失败，
// 且重新打开后没有任何新增记录。
func TestCreate_ClosedStoreFailsCleanly(t *testing.T) {
	dbPath := filepath.Join(t.TempDir(), "inspection.db")
	store, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("打开数据库失败: %v", err)
	}
	if err := store.Init(context.Background()); err != nil {
		t.Fatalf("初始化数据库失败: %v", err)
	}
	if err := store.Close(); err != nil {
		t.Fatalf("关闭数据库失败: %v", err)
	}

	if _, err := store.Create(context.Background(), buildTestIssue("issue-closed")); err == nil {
		t.Fatal("存储关闭后创建必须失败")
	}

	reopened, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("重新打开数据库失败: %v", err)
	}
	defer reopened.Close()
	issues, err := reopened.List(context.Background())
	if err != nil {
		t.Fatalf("重开查询失败: %v", err)
	}
	if len(issues) != 0 {
		t.Fatalf("失败创建不应留下记录，实际 %d 条", len(issues))
	}
}

// TestUpdateStatus_AllTransitionsAndRepeat 验证三种状态任意直接转换
// 与重复设置同一状态：均成功并返回完整记录，业务字段与 createdAt 不变，
// updatedAt 采用服务端提供的新值。
func TestUpdateStatus_AllTransitionsAndRepeat(t *testing.T) {
	store := newTempStore(t)

	created, err := store.Create(context.Background(), buildTestIssue("issue-status"))
	if err != nil {
		t.Fatalf("创建问题失败: %v", err)
	}

	steps := []struct {
		status    Status
		updatedAt string
	}{
		{StatusInProgress, "2026-10-27T11:00:00+08:00"},
		{StatusResolved, "2026-10-27T12:00:00+08:00"},
		{StatusOpen, "2026-10-27T13:00:00+08:00"},
		{StatusOpen, "2026-10-27T13:30:00+08:00"}, // 重复设置同一状态
	}
	for _, step := range steps {
		got, err := store.UpdateStatus(context.Background(), created.ID, step.status, step.updatedAt)
		if err != nil {
			t.Fatalf("更新为 %s 失败: %v", step.status, err)
		}
		if got.Status != step.status || got.UpdatedAt != step.updatedAt {
			t.Fatalf("状态或更新时间不一致: %+v", got)
		}
		if got.Title != created.Title || got.Description != created.Description ||
			got.Priority != created.Priority || got.Position != created.Position ||
			got.CreatedAt != created.CreatedAt || got.ID != created.ID {
			t.Fatalf("其他字段不应改变:\n得到 %+v\n原始 %+v", got, created)
		}
	}
}

// TestUpdateStatus_NotFound 验证不存在的 ID 返回 ErrNotFound 且不改动现有数据。
func TestUpdateStatus_NotFound(t *testing.T) {
	store := newTempStore(t)

	if _, err := store.Create(context.Background(), buildTestIssue("issue-exist")); err != nil {
		t.Fatalf("创建问题失败: %v", err)
	}
	if _, err := store.UpdateStatus(context.Background(), "00000000-0000-4000-8000-000000000000", StatusResolved, "2026-10-27T14:00:00+08:00"); !errors.Is(err, ErrNotFound) {
		t.Fatalf("期望 ErrNotFound，实际 %v", err)
	}

	issues, err := store.List(context.Background())
	if err != nil {
		t.Fatalf("查询列表失败: %v", err)
	}
	if len(issues) != 1 || issues[0].Status != StatusOpen || issues[0].UpdatedAt != issues[0].CreatedAt {
		t.Fatalf("不存在的 ID 不应改动任何记录，实际 %+v", issues)
	}
}

// TestList_OrdersByCreatedAtDescThenIDASC 验证列表顺序：created_at 从新到旧，
// 相同创建时间按 id 升序。通过直接注入 ID 与创建时间测试，不依赖真实时间恰好相同。
func TestList_OrdersByCreatedAtDescThenIDASC(t *testing.T) {
	store := newTempStore(t)

	rows := []struct{ id, createdAt string }{
		{"c-id", "2026-10-27T10:00:00+08:00"},
		{"a-id", "2026-10-27T10:00:00+08:00"},
		{"b-id", "2026-10-27T10:00:00+08:00"},
		{"old-id", "2026-10-26T09:00:00+08:00"},
	}
	for _, r := range rows {
		it := buildTestIssue(r.id)
		it.CreatedAt = r.createdAt
		it.UpdatedAt = r.createdAt
		if _, err := store.Create(context.Background(), it); err != nil {
			t.Fatalf("写入 %s 失败: %v", r.id, err)
		}
	}

	issues, err := store.List(context.Background())
	if err != nil {
		t.Fatalf("查询列表失败: %v", err)
	}
	wantOrder := []string{"a-id", "b-id", "c-id", "old-id"}
	if len(issues) != len(wantOrder) {
		t.Fatalf("期望 %d 条，实际 %d 条", len(wantOrder), len(issues))
	}
	for i, it := range issues {
		if it.ID != wantOrder[i] {
			t.Fatalf("第 %d 条应为 %s，实际 %s；完整顺序 %+v", i, wantOrder[i], it.ID, issues)
		}
	}
}

// TestCreateReopen_PersistsIssueAndStatus 验证创建并改状态后关闭重开同一文件，
// 问题、最终状态与时间字段全部保留（重启持久化的存储层基础）。
func TestCreateReopen_PersistsIssueAndStatus(t *testing.T) {
	dbPath := filepath.Join(t.TempDir(), "inspection.db")

	first, err := OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("打开数据库失败: %v", err)
	}
	if err := first.Init(context.Background()); err != nil {
		t.Fatalf("初始化数据库失败: %v", err)
	}
	created, err := first.Create(context.Background(), buildTestIssue("issue-reopen"))
	if err != nil {
		t.Fatalf("创建问题失败: %v", err)
	}
	if _, err := first.UpdateStatus(context.Background(), created.ID, StatusResolved, "2026-10-27T15:00:00+08:00"); err != nil {
		t.Fatalf("更新状态失败: %v", err)
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
		t.Fatalf("重开查询失败: %v", err)
	}
	if len(issues) != 1 {
		t.Fatalf("重开后应保留 1 条，实际 %d 条", len(issues))
	}
	got := issues[0]
	if got.ID != created.ID || got.Status != StatusResolved || got.UpdatedAt != "2026-10-27T15:00:00+08:00" ||
		got.CreatedAt != created.CreatedAt || got.Title != created.Title || got.Position != created.Position {
		t.Fatalf("重开后数据不一致:\n得到 %+v\n期望 id=%s status=resolved createdAt=%s", got, created.ID, created.CreatedAt)
	}
}

// TestStore_SQLInjectionTitleTreatedAsText 验证注入样式字符串仅作为普通文本保存，
// 不改变查询结构，表结构完好。
func TestStore_SQLInjectionTitleTreatedAsText(t *testing.T) {
	store := newTempStore(t)

	injectTitle := `破损'); DROP TABLE issues;-- `
	it := buildTestIssue("issue-inject")
	it.Title = injectTitle
	got, err := store.Create(context.Background(), it)
	if err != nil {
		t.Fatalf("创建含注入样式标题的问题失败: %v", err)
	}
	if got.Title != injectTitle {
		t.Fatalf("标题应原样保存，实际 %q", got.Title)
	}

	var tableCount int
	if err := store.db.QueryRow(
		`SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'issues'`,
	).Scan(&tableCount); err != nil {
		t.Fatalf("查询 sqlite_master 失败: %v", err)
	}
	if tableCount != 1 {
		t.Fatal("issues 表不应被注入破坏")
	}

	issues, err := store.List(context.Background())
	if err != nil {
		t.Fatalf("查询列表失败: %v", err)
	}
	if len(issues) != 1 || issues[0].Title != injectTitle {
		t.Fatalf("注入样式标题应作为文本回读，实际 %+v", issues)
	}

	// 带引号的 ID 只应得到“不存在”，不产生 SQL 错误。
	if _, err := store.UpdateStatus(context.Background(), "'; DELETE FROM issues;--", StatusOpen, "2026-10-27T16:00:00+08:00"); !errors.Is(err, ErrNotFound) {
		t.Fatalf("带引号 ID 应返回 ErrNotFound，实际 %v", err)
	}
}

// TestStore_CheckConstraintsRejectInvalidEnums 验证表级 CHECK 约束作为
// 领域校验之外的存储防御：非法枚举无法直接写入。
func TestStore_CheckConstraintsRejectInvalidEnums(t *testing.T) {
	store := newTempStore(t)

	insert := func(priority, status string) error {
		_, err := store.db.ExecContext(context.Background(), `
			INSERT INTO issues (id, title, description, priority, status,
			                    position_x, position_y, position_z, created_at, updated_at)
			VALUES ('constraint-test', 't', '', ?, ?, 0, 0, 0,
			        '2026-10-27T10:00:00+08:00', '2026-10-27T10:00:00+08:00')
		`, priority, status)
		return err
	}
	if err := insert("urgent", "open"); err == nil {
		t.Fatal("非法优先级应被 CHECK 约束拒绝")
	}
	if err := insert("low", "done"); err == nil {
		t.Fatal("非法状态应被 CHECK 约束拒绝")
	}

	issues, err := store.List(context.Background())
	if err != nil {
		t.Fatalf("查询列表失败: %v", err)
	}
	if len(issues) != 0 {
		t.Fatalf("被约束拒绝的写入不应留下记录，实际 %d 条", len(issues))
	}
}
