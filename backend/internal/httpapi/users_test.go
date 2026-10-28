package httpapi

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/auth"
	"ar-inspection/backend/internal/issue"
)

// 计划 008 阶段 1 测试：人员管理四接口的权限、校验、会话联动与删除语义。
// 复用 handler_test.go 的 newTestRouter / doJSONAs / assertErrorEnvelope 等助手。

// usersEnvelope 列表响应结构。
type usersEnvelope struct {
	Data []struct {
		Username    string `json:"username"`
		DisplayName string `json:"displayName"`
		Role        string `json:"role"`
		CreatedAt   string `json:"createdAt"`
		UpdatedAt   string `json:"updatedAt"`
	} `json:"data"`
}

// listUsersAs 以指定角色拉取账号列表。
func listUsersAs(t *testing.T, env *testEnv, role auth.Role) usersEnvelope {
	t.Helper()
	rec := doJSONAs(env, role, http.MethodGet, "/api/users", "")
	if rec.Code != http.StatusOK {
		t.Fatalf("列表期望 200，实际 %d %s", rec.Code, rec.Body.String())
	}
	var resp usersEnvelope
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("解析列表失败: %v", err)
	}
	return resp
}

// TestUsers_ListIncludesBothRoles 验证列表返回全部账号（含管理员只读展示），
// 不含密码哈希字段。
func TestUsers_ListIncludesBothRoles(t *testing.T) {
	env := newTestRouter(t)

	resp := listUsersAs(t, env, auth.RoleAdmin)
	if len(resp.Data) != 2 {
		t.Fatalf("首启后应有 inspector/admin 两个账号，实际 %d 个: %+v", len(resp.Data), resp.Data)
	}
	byName := map[string]string{}
	for _, u := range resp.Data {
		byName[u.Username] = u.Role
		if u.CreatedAt == "" || u.UpdatedAt == "" {
			t.Fatalf("列表应含创建/更新时间: %+v", u)
		}
	}
	if byName["inspector"] != "inspector" || byName["admin"] != "admin" {
		t.Fatalf("角色不符: %+v", byName)
	}
	if strings.Contains(strings.ToLower(recBodyString(t, env, auth.RoleAdmin)), "password") {
		t.Fatal("列表响应不得出现密码或哈希字段")
	}
}

// recBodyString 拉取列表原文（供敏感字段扫描）。
func recBodyString(t *testing.T, env *testEnv, role auth.Role) string {
	t.Helper()
	rec := doJSONAs(env, role, http.MethodGet, "/api/users", "")
	return rec.Body.String()
}

// TestUsers_PermissionMatrix 覆盖四接口权限负例：匿名 401、巡检员 403。
func TestUsers_PermissionMatrix(t *testing.T) {
	env := newTestRouter(t)

	for _, tc := range []struct {
		name   string
		method string
		path   string
		body   string
	}{
		{"匿名列表", http.MethodGet, "/api/users", ""},
		{"匿名创建", http.MethodPost, "/api/users", `{"username":"zhangsan","displayName":"张三","password":"12345678"}`},
		{"匿名修改", http.MethodPatch, "/api/users/zhangsan", `{"displayName":"张三"}`},
		{"匿名删除", http.MethodDelete, "/api/users/zhangsan", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			rec := doJSON(env.Router, tc.method, tc.path, tc.body)
			assertErrorEnvelope(t, rec, http.StatusUnauthorized, "authentication_required")
		})
	}
	for _, tc := range []struct {
		name   string
		method string
		path   string
		body   string
	}{
		{"巡检员列表", http.MethodGet, "/api/users", ""},
		{"巡检员创建", http.MethodPost, "/api/users", `{"username":"zhangsan","displayName":"张三","password":"12345678"}`},
		{"巡检员修改", http.MethodPatch, "/api/users/inspector", `{"displayName":"张三"}`},
		{"巡检员删除", http.MethodDelete, "/api/users/inspector", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			rec := doJSONAs(env, auth.RoleInspector, tc.method, tc.path, tc.body)
			assertErrorEnvelope(t, rec, http.StatusForbidden, "permission_denied")
		})
	}
}

// TestUsers_CreateValidation 覆盖创建的领域校验正反例：非法用户名（过短/过长/
// 非法字符）、重复用户名（含管理员）、空显示名、短密码与超长密码。
func TestUsers_CreateValidation(t *testing.T) {
	env := newTestRouter(t)

	for name, body := range map[string]string{
		"用户名过短":   `{"username":"a","displayName":"甲","password":"12345678"}`,
		"用户名过长":   `{"username":"` + strings.Repeat("a", 33) + `","displayName":"甲","password":"12345678"}`,
		"用户名非法字符": `{"username":"张三!","displayName":"甲","password":"12345678"}`,
		"用户名重复":   `{"username":"inspector","displayName":"甲","password":"12345678"}`,
		"用户名撞管理员": `{"username":"admin","displayName":"甲","password":"12345678"}`,
		"显示名为空":   `{"username":"zhangsan","displayName":"  ","password":"12345678"}`,
		"密码过短":    `{"username":"zhangsan","displayName":"甲","password":"1234567"}`,
		"密码超72":   `{"username":"zhangsan","displayName":"甲","password":"` + strings.Repeat("p", 73) + `"}`,
		"密码全空格":   `{"username":"zhangsan","displayName":"甲","password":"        "}`,
		"未知字段":    `{"username":"zhangsan","displayName":"甲","password":"12345678","x":1}`,
		"缺字段":     `{"username":"zhangsan","displayName":"甲"}`,
	} {
		t.Run(name, func(t *testing.T) {
			rec := doJSONAs(env, auth.RoleAdmin, http.MethodPost, "/api/users", body)
			assertErrorEnvelope(t, rec, http.StatusBadRequest, "invalid_argument")
		})
	}

	// 正例：合法创建返回 201，新账号立即可登录
	okRec := doJSONAs(env, auth.RoleAdmin, http.MethodPost, "/api/users",
		`{"username":"zhangsan","displayName":"  张三  ","password":"zhangsan-pw-1"}`)
	if okRec.Code != http.StatusCreated {
		t.Fatalf("创建期望 201，实际 %d %s", okRec.Code, okRec.Body.String())
	}
	var created struct {
		Data struct {
			Username    string `json:"username"`
			DisplayName string `json:"displayName"`
			Role        string `json:"role"`
		} `json:"data"`
	}
	if err := json.Unmarshal(okRec.Body.Bytes(), &created); err != nil {
		t.Fatalf("解析失败: %v", err)
	}
	if created.Data.Username != "zhangsan" || created.Data.DisplayName != "张三" || created.Data.Role != "inspector" {
		t.Fatalf("创建结果不符（显示名应去空格、角色固定巡检员）: %+v", created.Data)
	}
	login := tryLogin(env.Router, "zhangsan", "zhangsan-pw-1")
	if login.Code != http.StatusOK {
		t.Fatalf("新账号应立即可登录，实际 %d %s", login.Code, login.Body.String())
	}
}

// TestUsers_UpdateAndSessionKick 覆盖修改：改显示名保留会话；重置密码清除
// 全部会话（旧令牌下一步请求即 401）；目标不存在 404；目标为管理员 403；
// 空请求体（无字段）400。
func TestUsers_UpdateAndSessionKick(t *testing.T) {
	env := newTestRouter(t)
	mustCreateZhangsan(t, env)

	// zhangsan 登录拿会话
	login := tryLogin(env.Router, "zhangsan", "zhangsan-pw-1")
	if login.Code != http.StatusOK {
		t.Fatalf("登录失败: %d %s", login.Code, login.Body.String())
	}
	token := extractSessionCookie(login)

	// 仅改显示名：会话保留
	patchName := doJSONAs(env, auth.RoleAdmin, http.MethodPatch, "/api/users/zhangsan", `{"displayName":"张三丰"}`)
	if patchName.Code != http.StatusOK {
		t.Fatalf("改显示名期望 200，实际 %d %s", patchName.Code, patchName.Body.String())
	}
	if rec := requestWithBearer(env.Router, http.MethodGet, "/api/auth/me", "", token); rec.Code != http.StatusOK {
		t.Fatalf("仅改显示名后会话应保留，实际 %d", rec.Code)
	}

	// 重置密码：旧会话立即失效
	patchPW := doJSONAs(env, auth.RoleAdmin, http.MethodPatch, "/api/users/zhangsan", `{"password":"new-pass-123"}`)
	if patchPW.Code != http.StatusOK {
		t.Fatalf("重置密码期望 200，实际 %d %s", patchPW.Code, patchPW.Body.String())
	}
	if rec := requestWithBearer(env.Router, http.MethodGet, "/api/auth/me", "", token); rec.Code != http.StatusUnauthorized {
		t.Fatalf("重置密码后旧会话应立即 401，实际 %d", rec.Code)
	}
	// 新密码可登录
	if rec := tryLogin(env.Router, "zhangsan", "new-pass-123"); rec.Code != http.StatusOK {
		t.Fatalf("新密码应可登录，实际 %d", rec.Code)
	}

	// 目标不存在 → 404
	notFound := doJSONAs(env, auth.RoleAdmin, http.MethodPatch, "/api/users/nobody", `{"displayName":"无名"}`)
	assertErrorEnvelope(t, notFound, http.StatusNotFound, "user_not_found")

	// 目标为管理员 → 403（改名与重置密码都拒绝）
	for _, body := range []string{`{"displayName":"超级管理员"}`, `{"password":"admin-pass-99"}`} {
		rec := doJSONAs(env, auth.RoleAdmin, http.MethodPatch, "/api/users/admin", body)
		assertErrorEnvelope(t, rec, http.StatusForbidden, "admin_account_protected")
	}

	// 无任何修改字段 → 400
	emptyPatch := doJSONAs(env, auth.RoleAdmin, http.MethodPatch, "/api/users/zhangsan", `{}`)
	assertErrorEnvelope(t, emptyPatch, http.StatusBadRequest, "invalid_argument")
}

// TestUsers_DeleteSemantics 覆盖删除：删除后登录失败、会话失效、历史问题
// 保留且归属转为仅管理员可见；管理员账号不可删；目标不存在 404。
func TestUsers_DeleteSemantics(t *testing.T) {
	env := newTestRouter(t)
	mustCreateZhangsan(t, env)

	// zhangsan 登录并上报一个问题
	login := tryLogin(env.Router, "zhangsan", "zhangsan-pw-1")
	if login.Code != http.StatusOK {
		t.Fatalf("登录失败: %d", login.Code)
	}
	token := extractSessionCookie(login)
	createBody := `{"title":"张三上报的问题","priority":"low","position":{"x":1,"y":2,"z":3}}`
	createRec := requestWithBearer(env.Router, http.MethodPost, "/api/issues", createBody, token)
	if createRec.Code != http.StatusCreated {
		t.Fatalf("上报失败: %d %s", createRec.Code, createRec.Body.String())
	}
	var created struct {
		Data struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	if err := json.Unmarshal(createRec.Body.Bytes(), &created); err != nil {
		t.Fatalf("解析失败: %v", err)
	}
	issueID := created.Data.ID

	// 删除 zhangsan
	delRec := doJSONAs(env, auth.RoleAdmin, http.MethodDelete, "/api/users/zhangsan", "")
	if delRec.Code != http.StatusNoContent {
		t.Fatalf("删除期望 204，实际 %d %s", delRec.Code, delRec.Body.String())
	}

	// 旧会话失效；登录报用户名或密码错误（不暴露存在性）
	if rec := requestWithBearer(env.Router, http.MethodGet, "/api/auth/me", "", token); rec.Code != http.StatusUnauthorized {
		t.Fatalf("删除后旧会话应立即 401，实际 %d", rec.Code)
	}
	deletedLogin := tryLogin(env.Router, "zhangsan", "zhangsan-pw-1")
	assertErrorEnvelope(t, deletedLogin, http.StatusUnauthorized, "invalid_credentials")

	// 问题本体保留：管理员可见
	adminList := doJSONAs(env, auth.RoleAdmin, http.MethodGet, "/api/issues", "")
	if !strings.Contains(adminList.Body.String(), issueID) {
		t.Fatalf("删除账号后问题应保留且管理员可见: %s", adminList.Body.String())
	}
	// 归属转未知：其他巡检员（inspector）不可见
	inspList := doJSONAs(env, auth.RoleInspector, http.MethodGet, "/api/issues", "")
	if strings.Contains(inspList.Body.String(), issueID) {
		t.Fatalf("归属已转未知，巡检员不应再看到该问题: %s", inspList.Body.String())
	}

	// 管理员账号不可删 → 403
	adminDel := doJSONAs(env, auth.RoleAdmin, http.MethodDelete, "/api/users/admin", "")
	assertErrorEnvelope(t, adminDel, http.StatusForbidden, "admin_account_protected")

	// 目标不存在 → 404（重复删除）
	reDel := doJSONAs(env, auth.RoleAdmin, http.MethodDelete, "/api/users/zhangsan", "")
	assertErrorEnvelope(t, reDel, http.StatusNotFound, "user_not_found")

	// 列表只剩 inspector/admin
	resp := listUsersAs(t, env, auth.RoleAdmin)
	if len(resp.Data) != 2 {
		t.Fatalf("删除后列表应只剩 2 个账号，实际 %d: %+v", len(resp.Data), resp.Data)
	}
}

// TestUsers_NoResurrectAfterRestart 验证删除巡检员后重启（等效：关库重开+
// 重跑 EnsureAccounts）不会复活该账号；管理员仍按环境变量同步。
func TestUsers_NoResurrectAfterRestart(t *testing.T) {
	env := newTestRouter(t)
	mustCreateZhangsan(t, env)

	// 删除 zhangsan
	delRec := doJSONAs(env, auth.RoleAdmin, http.MethodDelete, "/api/users/zhangsan", "")
	if delRec.Code != http.StatusNoContent {
		t.Fatalf("删除期望 204，实际 %d", delRec.Code)
	}

	// 等效重启：关闭两库连接，同一文件重建并重跑账号初始化
	if err := env.Store.Close(); err != nil {
		t.Fatalf("关闭问题库失败: %v", err)
	}
	if err := env.AuthStore.Close(); err != nil {
		t.Fatalf("关闭账号库失败: %v", err)
	}
	rebuilt := rebuildRouterWithEnsure(t, env.dbPath)

	// zhangsan 不复活
	relaunchLogin := tryLogin(rebuilt, "zhangsan", "zhangsan-pw-1")
	assertErrorEnvelope(t, relaunchLogin, http.StatusUnauthorized, "invalid_credentials")
	// inspector/admin 均保持可登录
	if rec := tryLogin(rebuilt, "inspector", testInspectorPassword); rec.Code != http.StatusOK {
		t.Fatalf("重启后 inspector 应可登录，实际 %d %s", rec.Code, rec.Body.String())
	}
	if rec := tryLogin(rebuilt, "admin", testAdminPassword); rec.Code != http.StatusOK {
		t.Fatalf("重启后 admin 应可登录（环境变量同步），实际 %d", rec.Code)
	}
}

// TestUsers_InspectorEnvNoLongerForceSyncs 验证环境变量不再强制同步巡检员：
// 页面改密后重启（重跑 EnsureAccounts），巡检员密码保持页面设置值。
func TestUsers_InspectorEnvNoLongerForceSyncs(t *testing.T) {
	env := newTestRouter(t)

	// 管理员重置 inspector 密码
	patchRec := doJSONAs(env, auth.RoleAdmin, http.MethodPatch, "/api/users/inspector", `{"password":"page-set-pw-1"}`)
	if patchRec.Code != http.StatusOK {
		t.Fatalf("重置密码期望 200，实际 %d %s", patchRec.Code, patchRec.Body.String())
	}

	// 等效重启
	if err := env.Store.Close(); err != nil {
		t.Fatalf("关闭问题库失败: %v", err)
	}
	if err := env.AuthStore.Close(); err != nil {
		t.Fatalf("关闭账号库失败: %v", err)
	}
	rebuilt := rebuildRouterWithEnsure(t, env.dbPath)

	// 旧环境变量密码不再生效，页面设置的密码保留
	if rec := tryLogin(rebuilt, "inspector", testInspectorPassword); rec.Code == http.StatusOK {
		t.Fatalf("环境变量密码重启后应已被页面密码取代，但旧密码仍可登录（语义错误）")
	}
	if rec := tryLogin(rebuilt, "inspector", "page-set-pw-1"); rec.Code != http.StatusOK {
		t.Fatalf("页面设置的密码重启后应保留，实际 %d %s", rec.Code, rec.Body.String())
	}
}

// mustCreateZhangsan 创建测试用巡检员 zhangsan（密码 zhangsan-pw-1）。
func mustCreateZhangsan(t *testing.T, env *testEnv) {
	t.Helper()
	rec := doJSONAs(env, auth.RoleAdmin, http.MethodPost, "/api/users",
		`{"username":"zhangsan","displayName":"张三","password":"zhangsan-pw-1"}`)
	if rec.Code != http.StatusCreated {
		t.Fatalf("准备：创建 zhangsan 失败 %d %s", rec.Code, rec.Body.String())
	}
}

// requestWithBearer 以显式 Bearer 令牌发起请求（测试自建账号会话用）。
func requestWithBearer(router *gin.Engine, method, path, body, token string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(method, path, strings.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+token)
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	return rec
}

// rebuildRouterWithEnsure 在同一数据库文件上重建路由并重跑账号初始化
// （等效进程重启：关闭连接 → 重开 → EnsureAccounts → 注册路由）。
func rebuildRouterWithEnsure(t *testing.T, dbPath string) *gin.Engine {
	t.Helper()
	gin.SetMode(gin.TestMode)
	store, err := issue.OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("重开问题库失败: %v", err)
	}
	t.Cleanup(func() { _ = store.Close() })
	authStore, err := auth.OpenSQLite(dbPath)
	if err != nil {
		t.Fatalf("重开账号库失败: %v", err)
	}
	t.Cleanup(func() { _ = authStore.Close() })
	authService := auth.NewService(authStore, []byte(testSessionSecret))
	if err := authService.EnsureAccounts(context.Background(), testInspectorPassword, testAdminPassword); err != nil {
		t.Fatalf("重跑账号初始化失败: %v", err)
	}
	return NewRouter(store, authService, []string{"http://localhost:5173"}, issue.DefaultFieldGenerator())
}
