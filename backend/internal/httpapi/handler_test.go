package httpapi

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/issue"
)

// newTestRouter 用临时 SQLite 存储与默认字段生成器构建路由，返回可关闭的存储用于模拟故障。
func newTestRouter(t *testing.T) (*gin.Engine, *issue.SQLiteStore) {
	t.Helper()
	return newTestRouterWithFields(t, issue.DefaultFieldGenerator())
}

// newTestRouterWithFields 用指定字段生成器构建路由，用于注入 ID/时间生成失败路径。
func newTestRouterWithFields(t *testing.T, fields issue.FieldGenerator) (*gin.Engine, *issue.SQLiteStore) {
	t.Helper()
	gin.SetMode(gin.TestMode)
	store, err := issue.OpenSQLite(filepath.Join(t.TempDir(), "inspection.db"))
	if err != nil {
		t.Fatalf("打开临时数据库失败: %v", err)
	}
	t.Cleanup(func() { _ = store.Close() })
	if err := store.Init(context.Background()); err != nil {
		t.Fatalf("初始化临时数据库失败: %v", err)
	}
	router := NewRouter(store, []string{"http://localhost:5173", "http://127.0.0.1:5173"}, fields)
	return router, store
}

// TestHealth_OK 验证存储可用时健康检查返回 200 与统一成功结构。
func TestHealth_OK(t *testing.T) {
	router, _ := newTestRouter(t)

	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/health", nil))

	if rec.Code != http.StatusOK {
		t.Fatalf("期望 200，实际 %d，响应 %s", rec.Code, rec.Body.String())
	}
	if ct := rec.Header().Get("Content-Type"); !strings.HasPrefix(ct, "application/json") {
		t.Fatalf("期望 JSON Content-Type，实际 %q", ct)
	}
	var body struct {
		Data struct {
			Status string `json:"status"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("解析响应失败: %v，响应 %s", err, rec.Body.String())
	}
	if body.Data.Status != "ok" {
		t.Fatalf("期望 data.status=ok，实际 %q", body.Data.Status)
	}
}

// TestHealth_StorageUnavailable 验证存储不可用时返回 503 service_unavailable，而非仅报进程存活。
func TestHealth_StorageUnavailable(t *testing.T) {
	router, store := newTestRouter(t)
	// 关闭底层连接，模拟存储故障；后续健康检查失败。
	if err := store.Close(); err != nil {
		t.Fatalf("关闭存储失败: %v", err)
	}

	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/health", nil))

	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("期望 503，实际 %d，响应 %s", rec.Code, rec.Body.String())
	}
	var body struct {
		Error struct {
			Code    string `json:"code"`
			Message string `json:"message"`
		} `json:"error"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("解析响应失败: %v，响应 %s", err, rec.Body.String())
	}
	if body.Error.Code != "service_unavailable" {
		t.Fatalf("期望 error.code=service_unavailable，实际 %q", body.Error.Code)
	}
}

// TestHealth_CorruptedStorageReturns503 验证数据库文件内容损坏（连接仍可建立）时，
// 健康检查接口同样返回 503，不把不可用的存储误报为正常。
func TestHealth_CorruptedStorageReturns503(t *testing.T) {
	gin.SetMode(gin.TestMode)
	dbPath := filepath.Join(t.TempDir(), "inspection.db")
	store, err := issue.OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("打开临时数据库失败: %v", err)
	}
	if err := store.Init(context.Background()); err != nil {
		t.Fatalf("初始化临时数据库失败: %v", err)
	}
	if err := store.Close(); err != nil {
		t.Fatalf("关闭存储失败: %v", err)
	}
	// 覆盖主文件为非 SQLite 文本，模拟重启后磁盘内容损坏。
	if err := os.WriteFile(dbPath, []byte("corrupted: not a sqlite database"), 0o644); err != nil {
		t.Fatalf("覆盖数据库文件失败: %v", err)
	}
	reopened, err := issue.OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("重新打开损坏数据库失败: %v", err)
	}
	t.Cleanup(func() { _ = reopened.Close() })
	router := NewRouter(reopened, []string{"http://localhost:5173"}, issue.DefaultFieldGenerator())

	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/health", nil))

	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("期望 503，实际 %d，响应 %s", rec.Code, rec.Body.String())
	}
	var body struct {
		Error struct {
			Code string `json:"code"`
		} `json:"error"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("解析响应失败: %v，响应 %s", err, rec.Body.String())
	}
	if body.Error.Code != "service_unavailable" {
		t.Fatalf("期望 error.code=service_unavailable，实际 %q", body.Error.Code)
	}
}

// TestUnknownRoute 验证未知路径返回统一 404 错误结构。
func TestUnknownRoute(t *testing.T) {
	router, _ := newTestRouter(t)

	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/nope", nil))

	if rec.Code != http.StatusNotFound {
		t.Fatalf("期望 404，实际 %d", rec.Code)
	}
	var body struct {
		Error struct {
			Code string `json:"code"`
		} `json:"error"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("解析响应失败: %v", err)
	}
	if body.Error.Code != "not_found" {
		t.Fatalf("期望 error.code=not_found，实际 %q", body.Error.Code)
	}
}

// TestMethodNotAllowed 验证已知路径用不支持的方法返回统一 405 错误结构。
func TestMethodNotAllowed(t *testing.T) {
	router, _ := newTestRouter(t)

	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, httptest.NewRequest(http.MethodPost, "/api/health", nil))

	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("期望 405，实际 %d", rec.Code)
	}
	var body struct {
		Error struct {
			Code string `json:"code"`
		} `json:"error"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("解析响应失败: %v", err)
	}
	if body.Error.Code != "method_not_allowed" {
		t.Fatalf("期望 error.code=method_not_allowed，实际 %q", body.Error.Code)
	}
}

// TestCORS_PreflightAllowedOrigin 验证允许来源的预检返回 204 与放行头。
func TestCORS_PreflightAllowedOrigin(t *testing.T) {
	router, _ := newTestRouter(t)

	req := httptest.NewRequest(http.MethodOptions, "/api/health", nil)
	req.Header.Set("Origin", "http://localhost:5173")
	req.Header.Set("Access-Control-Request-Method", http.MethodGet)
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)

	if rec.Code != http.StatusNoContent {
		t.Fatalf("期望预检 204，实际 %d", rec.Code)
	}
	if got := rec.Header().Get("Access-Control-Allow-Origin"); got != "http://localhost:5173" {
		t.Fatalf("期望 ACAO 为允许来源，实际 %q", got)
	}
	if got := rec.Header().Get("Access-Control-Allow-Methods"); !strings.Contains(got, "PATCH") {
		t.Fatalf("期望 Allow-Methods 含 PATCH，实际 %q", got)
	}
	if got := rec.Header().Get("Vary"); !strings.Contains(got, "Origin") {
		t.Fatalf("期望 Vary 含 Origin，实际 %q", got)
	}
}

// TestCORS_DisallowedOriginNoExpose 验证未配置来源不回写 ACAO 头。
func TestCORS_DisallowedOriginNoExpose(t *testing.T) {
	router, _ := newTestRouter(t)

	req := httptest.NewRequest(http.MethodOptions, "/api/health", nil)
	req.Header.Set("Origin", "http://evil.example.com")
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)

	if got := rec.Header().Get("Access-Control-Allow-Origin"); got != "" {
		t.Fatalf("不允许的来源不应回写 ACAO，实际 %q", got)
	}
}

// doJSON 以 JSON 请求体发起 HTTP 请求并返回响应记录器。
func doJSON(router *gin.Engine, method, path, body string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(method, path, strings.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	return rec
}

// assertErrorEnvelope 断言统一错误结构：error.code 符合预期、message 非空、
// 响应不包含 data 字段（成功与错误结构互斥）。
func assertErrorEnvelope(t *testing.T, rec *httptest.ResponseRecorder, wantStatus int, wantCode string) {
	t.Helper()
	if rec.Code != wantStatus {
		t.Fatalf("期望 HTTP %d，实际 %d，响应 %s", wantStatus, rec.Code, rec.Body.String())
	}
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(rec.Body.Bytes(), &raw); err != nil {
		t.Fatalf("解析响应失败: %v，响应 %s", err, rec.Body.String())
	}
	if _, hasData := raw["data"]; hasData {
		t.Fatalf("错误响应不应包含 data 字段: %s", rec.Body.String())
	}
	var body struct {
		Error struct {
			Code    string `json:"code"`
			Message string `json:"message"`
		} `json:"error"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("解析错误结构失败: %v，响应 %s", err, rec.Body.String())
	}
	if body.Error.Code != wantCode {
		t.Fatalf("期望 error.code=%s，实际 %q，响应 %s", wantCode, body.Error.Code, rec.Body.String())
	}
	if strings.TrimSpace(body.Error.Message) == "" {
		t.Fatalf("error.message 不能为空: %s", rec.Body.String())
	}
}

// assertNoErrorKey 断言成功响应不包含 error 字段。
func assertNoErrorKey(t *testing.T, rec *httptest.ResponseRecorder) {
	t.Helper()
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(rec.Body.Bytes(), &raw); err != nil {
		t.Fatalf("解析响应失败: %v，响应 %s", err, rec.Body.String())
	}
	if _, hasErr := raw["error"]; hasErr {
		t.Fatalf("成功响应不应包含 error 字段: %s", rec.Body.String())
	}
}

// createIssueViaHTTP 通过真实 HTTP 请求创建一条问题并返回响应中的记录。
func createIssueViaHTTP(t *testing.T, router *gin.Engine) map[string]any {
	t.Helper()
	body := `{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}`
	rec := doJSON(router, http.MethodPost, "/api/issues", body)
	if rec.Code != http.StatusCreated {
		t.Fatalf("创建问题期望 201，实际 %d，响应 %s", rec.Code, rec.Body.String())
	}
	var resp struct {
		Data map[string]any `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("解析创建响应失败: %v，响应 %s", err, rec.Body.String())
	}
	return resp.Data
}

// TestCreateIssue_Created 验证合法创建返回 201、完整字段、服务端字段由后端生成
// （标题 trim 保存、默认 open、初始时间相同且带 +08:00）。
func TestCreateIssue_Created(t *testing.T) {
	router, _ := newTestRouter(t)

	rec := doJSON(router, http.MethodPost, "/api/issues",
		`{"title":"  入口墙面破损  ","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0,"z":-1.26}}`)
	if rec.Code != http.StatusCreated {
		t.Fatalf("期望 201，实际 %d，响应 %s", rec.Code, rec.Body.String())
	}
	if ct := rec.Header().Get("Content-Type"); !strings.HasPrefix(ct, "application/json") {
		t.Fatalf("期望 JSON Content-Type，实际 %q", ct)
	}
	assertNoErrorKey(t, rec)

	var resp struct {
		Data struct {
			ID          string `json:"id"`
			Title       string `json:"title"`
			Description string `json:"description"`
			Priority    string `json:"priority"`
			Status      string `json:"status"`
			Position    struct {
				X float64 `json:"x"`
				Y float64 `json:"y"`
				Z float64 `json:"z"`
			} `json:"position"`
			CreatedAt string `json:"createdAt"`
			UpdatedAt string `json:"updatedAt"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("解析响应失败: %v，响应 %s", err, rec.Body.String())
	}
	d := resp.Data
	if d.Title != "入口墙面破损" {
		t.Fatalf("标题应保存 trim 后的值，实际 %q", d.Title)
	}
	if d.Description != "左侧墙体存在裂缝" || d.Priority != "high" {
		t.Fatalf("描述或优先级不一致: %+v", d)
	}
	if d.Status != "open" {
		t.Fatalf("新问题状态必须为 open，实际 %q", d.Status)
	}
	if d.Position.X != 0.42 || d.Position.Y != 0 || d.Position.Z != -1.26 {
		t.Fatalf("零与负数坐标应原样保存: %+v", d.Position)
	}
	if d.ID == "" {
		t.Fatal("ID 必须由后端生成且非空")
	}
	ts, err := time.Parse(time.RFC3339, d.CreatedAt)
	if err != nil {
		t.Fatalf("createdAt 应为 RFC 3339: %v", err)
	}
	if _, offset := ts.Zone(); offset != 8*60*60 {
		t.Fatalf("时间偏移应为 +08:00，实际 %d 秒", offset)
	}
	if d.UpdatedAt != d.CreatedAt {
		t.Fatalf("初始 updatedAt 应等于 createdAt: %s != %s", d.UpdatedAt, d.CreatedAt)
	}
}

// TestCreateIssue_InvalidRequests 覆盖创建负例矩阵：全部返回 400 invalid_argument，
// 且请求后问题总数不增加。
func TestCreateIssue_InvalidRequests(t *testing.T) {
	cases := []struct {
		name string
		body string
	}{
		{"缺失标题", `{"priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"空标题", `{"title":"","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"全空格标题", `{"title":"   ","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"缺失优先级", `{"title":"t","position":{"x":1,"y":2,"z":3}}`},
		{"大写枚举", `{"title":"t","priority":"LOW","position":{"x":1,"y":2,"z":3}}`},
		{"未知枚举", `{"title":"t","priority":"urgent","position":{"x":1,"y":2,"z":3}}`},
		{"枚举类型错误", `{"title":"t","priority":2,"position":{"x":1,"y":2,"z":3}}`},
		{"缺失位置", `{"title":"t","priority":"low"}`},
		{"缺失坐标轴", `{"title":"t","priority":"low","position":{"x":1,"y":2}}`},
		{"坐标为字符串", `{"title":"t","priority":"low","position":{"x":"1","y":2,"z":3}}`},
		{"语法错误", `{"title":`},
		{"数组代替对象", `[]`},
		{"未知字段", `{"foo":1}`},
		{"传入 id", `{"id":"x","title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"传入 status", `{"status":"open","title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"两个连续对象", `{"title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}{"title":"b"}`},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			router, _ := newTestRouter(t)
			rec := doJSON(router, http.MethodPost, "/api/issues", tc.body)
			assertErrorEnvelope(t, rec, http.StatusBadRequest, "invalid_argument")

			listRec := doJSON(router, http.MethodGet, "/api/issues", "")
			var listResp struct {
				Data []json.RawMessage `json:"data"`
			}
			if err := json.Unmarshal(listRec.Body.Bytes(), &listResp); err != nil {
				t.Fatalf("解析列表失败: %v", err)
			}
			if len(listResp.Data) != 0 {
				t.Fatalf("非法请求不应新增问题，实际 %d 条", len(listResp.Data))
			}
		})
	}
}

// TestCreateIssue_BodyTooLarge 验证超过 1 MiB 的请求体返回 400，不会全部读取。
func TestCreateIssue_BodyTooLarge(t *testing.T) {
	router, _ := newTestRouter(t)

	padding := strings.Repeat("A", (1<<20)+16)
	body := `{"title":"t","description":"` + padding + `","priority":"low","position":{"x":1,"y":2,"z":3}}`
	rec := doJSON(router, http.MethodPost, "/api/issues", body)
	assertErrorEnvelope(t, rec, http.StatusBadRequest, "invalid_argument")
}

// TestList_EmptyIsJSONArray 验证空库列表返回 { "data": [] }，不返回 null。
func TestList_EmptyIsJSONArray(t *testing.T) {
	router, _ := newTestRouter(t)

	rec := doJSON(router, http.MethodGet, "/api/issues", "")
	if rec.Code != http.StatusOK {
		t.Fatalf("期望 200，实际 %d，响应 %s", rec.Code, rec.Body.String())
	}
	if strings.TrimSpace(rec.Body.String()) != `{"data":[]}` {
		t.Fatalf("空列表应返回 {\"data\":[]}，实际 %s", rec.Body.String())
	}
}

// TestIssueChain_CreateThenPatchThenList 验证后端内部链路使用同一条记录：
// 创建返回的 ID 可直接用于状态接口与列表接口（阶段 3 门禁）。
func TestIssueChain_CreateThenPatchThenList(t *testing.T) {
	router, _ := newTestRouter(t)
	created := createIssueViaHTTP(t, router)
	id, _ := created["id"].(string)
	if id == "" {
		t.Fatalf("创建响应缺少 id: %v", created)
	}

	// 任意状态转换：open → in_progress → resolved → open，重复设置同一状态也成功。
	for _, status := range []string{"in_progress", "resolved", "open", "open"} {
		rec := doJSON(router, http.MethodPatch, "/api/issues/"+id+"/status", `{"status":"`+status+`"}`)
		if rec.Code != http.StatusOK {
			t.Fatalf("更新为 %s 期望 200，实际 %d，响应 %s", status, rec.Code, rec.Body.String())
		}
		var resp struct {
			Data struct {
				ID        string `json:"id"`
				Status    string `json:"status"`
				CreatedAt string `json:"createdAt"`
				UpdatedAt string `json:"updatedAt"`
			} `json:"data"`
		}
		if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
			t.Fatalf("解析更新响应失败: %v，响应 %s", err, rec.Body.String())
		}
		if resp.Data.ID != id || resp.Data.Status != status {
			t.Fatalf("更新结果不一致: %+v", resp.Data)
		}
		if resp.Data.UpdatedAt < resp.Data.CreatedAt {
			t.Fatalf("updatedAt 不应早于 createdAt: %s < %s", resp.Data.UpdatedAt, resp.Data.CreatedAt)
		}
	}

	listRec := doJSON(router, http.MethodGet, "/api/issues", "")
	var listResp struct {
		Data []struct {
			ID     string `json:"id"`
			Status string `json:"status"`
		} `json:"data"`
	}
	if err := json.Unmarshal(listRec.Body.Bytes(), &listResp); err != nil {
		t.Fatalf("解析列表失败: %v", err)
	}
	if len(listResp.Data) != 1 || listResp.Data[0].ID != id || listResp.Data[0].Status != "open" {
		t.Fatalf("列表应反映最终状态 open: %+v", listResp.Data)
	}
}

// TestUpdateStatus_Errors 覆盖状态接口负例：非法枚举/未知字段/额外 JSON/缺失状态
// 返回 400；合法格式但不存在的 ID 返回 404 issue_not_found；
// 非法状态配合不存在的 ID 返回 400（先校验后查库）；空 ID 路径由路由统一 404。
func TestUpdateStatus_Errors(t *testing.T) {
	router, _ := newTestRouter(t)
	created := createIssueViaHTTP(t, router)
	id, _ := created["id"].(string)

	cases := []struct {
		name       string
		path       string
		body       string
		wantStatus int
		wantCode   string
	}{
		{"非法枚举", "/api/issues/" + id + "/status", `{"status":"done"}`, 400, "invalid_argument"},
		{"大写枚举", "/api/issues/" + id + "/status", `{"status":"OPEN"}`, 400, "invalid_argument"},
		{"未知字段", "/api/issues/" + id + "/status", `{"status":"open","title":"x"}`, 400, "invalid_argument"},
		{"额外 JSON", "/api/issues/" + id + "/status", `{"status":"open"}{}`, 400, "invalid_argument"},
		{"缺失状态", "/api/issues/" + id + "/status", `{}`, 400, "invalid_argument"},
		{"语法错误", "/api/issues/" + id + "/status", `{`, 400, "invalid_argument"},
		{"非法状态配不存在 ID", "/api/issues/not-exist/status", `{"status":"done"}`, 400, "invalid_argument"},
		{"不存在的 ID", "/api/issues/00000000-0000-4000-8000-000000000000/status", `{"status":"open"}`, 404, "issue_not_found"},
		// 空 ID 路径（客户端拼 URL 时 ID 为空）按“空段=路径未命中”返回 404 not_found，
		// 与主流框架及 GitHub 等 API 行为一致；判断在读请求体之前，等同路由层拦截。
		{"空 ID 路径", "/api/issues//status", `{"status":"open"}`, 404, "not_found"},
		{"空 ID 配非法请求体", "/api/issues//status", `{`, 404, "not_found"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			rec := doJSON(router, http.MethodPatch, tc.path, tc.body)
			assertErrorEnvelope(t, rec, tc.wantStatus, tc.wantCode)
		})
	}

	// 负例不应改动已创建记录的状态。
	listRec := doJSON(router, http.MethodGet, "/api/issues", "")
	if !strings.Contains(listRec.Body.String(), `"status":"open"`) {
		t.Fatalf("负例不应改变现有记录状态: %s", listRec.Body.String())
	}
}

// TestIssues_StorageFailureReturns500 验证存储故障时三个业务接口都返回
// 500 internal_error（列表不得用空数组伪装成功），且响应不泄露 SQL、路径或堆栈。
func TestIssues_StorageFailureReturns500(t *testing.T) {
	router, store := newTestRouter(t)
	if err := store.Close(); err != nil {
		t.Fatalf("关闭存储失败: %v", err)
	}

	requests := []struct {
		name   string
		method string
		path   string
		body   string
	}{
		{"创建", http.MethodPost, "/api/issues", `{"title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"列表", http.MethodGet, "/api/issues", ""},
		{"改状态", http.MethodPatch, "/api/issues/any-id/status", `{"status":"open"}`},
	}
	for _, req := range requests {
		t.Run(req.name, func(t *testing.T) {
			rec := doJSON(router, req.method, req.path, req.body)
			assertErrorEnvelope(t, rec, http.StatusInternalServerError, "internal_error")
			for _, leak := range []string{"sql", "database", ".db", "/Users", "goroutine", "modernc"} {
				if strings.Contains(rec.Body.String(), leak) {
					t.Fatalf("错误响应不应包含 %q: %s", leak, rec.Body.String())
				}
			}
		})
	}
}

// TestCreateIssue_GeneratorFailureReturns500 验证 ID 生成失败返回 500，
// 不降级为可预测 ID、不返回虚假成功。
func TestCreateIssue_GeneratorFailureReturns500(t *testing.T) {
	failGen := issue.FieldGenerator{
		NewID:  func() (string, error) { return "", assertFailErr },
		NewNow: func() (string, error) { return "2026-10-27T12:00:00+08:00", nil },
	}
	router, _ := newTestRouterWithFields(t, failGen)

	rec := doJSON(router, http.MethodPost, "/api/issues",
		`{"title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}`)
	assertErrorEnvelope(t, rec, http.StatusInternalServerError, "internal_error")
}

// assertFailErr 是生成器失败注入用的固定错误。
var assertFailErr = errors.New("注入的生成失败")

// TestCORS_IssuesPreflightAllowedOrigin 验证问题接口的预检返回 204、
// 放行 PATCH/POST，并带 Vary: Origin。
func TestCORS_IssuesPreflightAllowedOrigin(t *testing.T) {
	router, _ := newTestRouter(t)

	req := httptest.NewRequest(http.MethodOptions, "/api/issues", nil)
	req.Header.Set("Origin", "http://localhost:5173")
	req.Header.Set("Access-Control-Request-Method", http.MethodPost)
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)

	if rec.Code != http.StatusNoContent {
		t.Fatalf("期望预检 204，实际 %d", rec.Code)
	}
	for _, want := range []string{"POST", "PATCH"} {
		if !strings.Contains(rec.Header().Get("Access-Control-Allow-Methods"), want) {
			t.Fatalf("Allow-Methods 应含 %s，实际 %q", want, rec.Header().Get("Access-Control-Allow-Methods"))
		}
	}
	if !strings.Contains(rec.Header().Get("Vary"), "Origin") {
		t.Fatalf("Vary 应含 Origin，实际 %q", rec.Header().Get("Vary"))
	}
}

// TestCORS_AllowedOriginActualRequest 验证允许来源的实际跨域请求回写 ACAO 头。
func TestCORS_AllowedOriginActualRequest(t *testing.T) {
	router, _ := newTestRouter(t)

	req := httptest.NewRequest(http.MethodPost, "/api/issues",
		strings.NewReader(`{"title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}`))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Origin", "http://127.0.0.1:5173")
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)

	if rec.Code != http.StatusCreated {
		t.Fatalf("期望 201，实际 %d，响应 %s", rec.Code, rec.Body.String())
	}
	if got := rec.Header().Get("Access-Control-Allow-Origin"); got != "http://127.0.0.1:5173" {
		t.Fatalf("期望 ACAO 回写允许来源，实际 %q", got)
	}
}
