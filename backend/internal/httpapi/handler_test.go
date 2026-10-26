package httpapi

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/issue"
)

// newTestRouter 用临时 SQLite 存储构建路由，返回可关闭的存储用于模拟故障。
func newTestRouter(t *testing.T) (*gin.Engine, *issue.SQLiteStore) {
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
	router := NewRouter(store, []string{"http://localhost:5173", "http://127.0.0.1:5173"})
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
	// 关闭底层连接，模拟存储故障；后续 Ping 失败。
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
		t.Fatalf("解析响应失败: %v", err)
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
