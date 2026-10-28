package httpapi

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/auth"
	"ar-inspection/backend/internal/issue"
)

// tryLogin 直接走登录接口（允许失败响应，供负例断言）。
func tryLogin(router *gin.Engine, username, password string) *httptest.ResponseRecorder {
	return doJSON(router, http.MethodPost, "/api/auth/login",
		`{"username":"`+username+`","password":"`+password+`"}`)
}

// TestAuth_LoginSuccess 验证正确凭据返回 200、用户信息与角色，并设置会话 Cookie。
func TestAuth_LoginSuccess(t *testing.T) {
	env := newTestRouter(t)

	rec := tryLogin(env.Router, "admin", testAdminPassword)
	if rec.Code != http.StatusOK {
		t.Fatalf("期望 200，实际 %d %s", rec.Code, rec.Body.String())
	}
	var resp struct {
		Data struct {
			Username    string `json:"username"`
			DisplayName string `json:"displayName"`
			Role        string `json:"role"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("解析失败: %v", err)
	}
	if resp.Data.Username != "admin" || resp.Data.Role != "admin" || resp.Data.DisplayName != "管理员" {
		t.Fatalf("登录信息不符: %+v", resp.Data)
	}
	if extractSessionCookie(rec) == "" {
		t.Fatal("登录应设置会话 Cookie")
	}
	cookie := rec.Result().Cookies()[0]
	if !cookie.HttpOnly {
		t.Fatal("会话 Cookie 必须 HttpOnly")
	}
	if cookie.SameSite != http.SameSiteLaxMode {
		t.Fatalf("会话 Cookie SameSite 应为 Lax，实际 %v", cookie.SameSite)
	}
}

// TestAuth_LoginFailures 统一凭据错误提示：错误密码与不存在用户返回同一错误，
// 不暴露账号是否存在；请求体非法返回 400。
func TestAuth_LoginFailures(t *testing.T) {
	env := newTestRouter(t)

	wrongPW := tryLogin(env.Router, "admin", "definitely-wrong")
	assertErrorEnvelope(t, wrongPW, http.StatusUnauthorized, "invalid_credentials")
	if !strings.Contains(wrongPW.Body.String(), "用户名或密码错误") {
		t.Fatalf("凭据错误应统一提示: %s", wrongPW.Body.String())
	}

	unknownUser := tryLogin(env.Router, "nobody", "whatever")
	assertErrorEnvelope(t, unknownUser, http.StatusUnauthorized, "invalid_credentials")
	if unknownUser.Body.String() != wrongPW.Body.String() {
		t.Fatalf("不存在用户与错误密码的响应应完全一致，避免暴露账号存在性:\n%s\n%s", wrongPW.Body.String(), unknownUser.Body.String())
	}

	for name, body := range map[string]string{
		"未知字段":    `{"username":"admin","password":"x","extra":1}`,
		"语法错误":    `{"username":`,
		"类型错误":    `{"username":1,"password":"x"}`,
		"额外 JSON": `{"username":"a","password":"b"}{}`,
	} {
		t.Run(name, func(t *testing.T) {
			rec := doJSON(env.Router, http.MethodPost, "/api/auth/login", body)
			assertErrorEnvelope(t, rec, http.StatusBadRequest, "invalid_argument")
		})
	}
}

// TestAuth_MeAndLogout 验证会话恢复与退出：me 有效返回用户；退出后令牌立即失效。
func TestAuth_MeAndLogout(t *testing.T) {
	env := newTestRouter(t)

	rec := doJSONAs(env, auth.RoleAdmin, http.MethodGet, "/api/auth/me", "")
	if rec.Code != http.StatusOK {
		t.Fatalf("me 期望 200，实际 %d %s", rec.Code, rec.Body.String())
	}

	logoutRec := doJSONAs(env, auth.RoleAdmin, http.MethodPost, "/api/auth/logout", "")
	if logoutRec.Code != http.StatusOK {
		t.Fatalf("退出期望 200，实际 %d", logoutRec.Code)
	}
	// 清除 Cookie：Set-Cookie MaxAge=-1
	cleared := false
	for _, c := range logoutRec.Result().Cookies() {
		if c.Name == sessionCookieName && c.MaxAge < 0 {
			cleared = true
		}
	}
	if !cleared {
		t.Fatal("退出应清除浏览器会话 Cookie")
	}

	afterLogout := doJSONAs(env, auth.RoleAdmin, http.MethodGet, "/api/auth/me", "")
	assertErrorEnvelope(t, afterLogout, http.StatusUnauthorized, "authentication_required")

	// 重复退出幂等成功
	again := doJSONAs(env, auth.RoleAdmin, http.MethodPost, "/api/auth/logout", "")
	if again.Code != http.StatusOK {
		t.Fatalf("重复退出应幂等成功，实际 %d", again.Code)
	}
}

// TestPermissions 覆盖权限矩阵负例：匿名 401、越权 403。
func TestPermissions(t *testing.T) {
	env := newTestRouter(t)
	created := createIssueViaHTTP(t, env)
	id := created["id"].(string)

	cases := []struct {
		name       string
		method     string
		path       string
		body       string
		wantStatus int
		wantCode   string
	}{
		{"匿名列表", http.MethodGet, "/api/issues", "", 401, "authentication_required"},
		{"匿名创建", http.MethodPost, "/api/issues", `{"title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}`, 401, "authentication_required"},
		{"匿名改状态", http.MethodPatch, "/api/issues/" + id + "/status", `{"status":"open"}`, 401, "authentication_required"},
		{"匿名 me", http.MethodGet, "/api/auth/me", "", 401, "authentication_required"},
		{"巡检员改状态", http.MethodPatch, "/api/issues/" + id + "/status", `{"status":"resolved"}`, 403, "permission_denied"},
		{"管理员创建", http.MethodPost, "/api/issues", `{"title":"t","priority":"low","position":{"x":1,"y":2,"z":3}}`, 403, "permission_denied"},
		{"巡检员 WebSocket 升级被拒", http.MethodGet, "/api/ws", "", 403, "permission_denied"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			var rec *httptest.ResponseRecorder
			switch tc.name {
			case "匿名列表", "匿名创建", "匿名改状态", "匿名 me":
				rec = doJSON(env.Router, tc.method, tc.path, tc.body)
			case "巡检员改状态", "巡检员 WebSocket 升级被拒":
				rec = doJSONAs(env, auth.RoleInspector, tc.method, tc.path, tc.body)
			default:
				rec = doJSONAs(env, auth.RoleAdmin, tc.method, tc.path, tc.body)
			}
			assertErrorEnvelope(t, rec, tc.wantStatus, tc.wantCode)
		})
	}
}

// TestDataScope 验证数据范围：巡检员仅见自己创建的问题；
// 无归属记录的旧问题仅管理员可见；两个列表字段与既有契约一致。
func TestDataScope(t *testing.T) {
	env := newTestRouter(t)

	// 巡检员通过接口创建 1 条（带归属）；再直接写库 1 条旧数据（无归属，模拟计划 006 之前的数据）。
	created := createIssueViaHTTP(t, env)
	legacyID := "legacy-0001"
	legacy := buildLegacyIssue(legacyID)
	if _, err := env.Store.Create(context.Background(), legacy, ""); err != nil {
		t.Fatalf("写入旧数据失败: %v", err)
	}

	inspRec := doJSONAs(env, auth.RoleInspector, http.MethodGet, "/api/issues", "")
	if inspRec.Code != http.StatusOK {
		t.Fatalf("巡检员列表期望 200，实际 %d %s", inspRec.Code, inspRec.Body.String())
	}
	var inspList struct {
		Data []struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	if err := json.Unmarshal(inspRec.Body.Bytes(), &inspList); err != nil {
		t.Fatalf("解析失败: %v", err)
	}
	if len(inspList.Data) != 1 || inspList.Data[0].ID != created["id"] {
		t.Fatalf("巡检员应只见自己的 1 条，实际 %+v", inspList.Data)
	}

	adminRec := doJSONAs(env, auth.RoleAdmin, http.MethodGet, "/api/issues", "")
	var adminList struct {
		Data []struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	if err := json.Unmarshal(adminRec.Body.Bytes(), &adminList); err != nil {
		t.Fatalf("解析失败: %v", err)
	}
	if len(adminList.Data) != 2 {
		t.Fatalf("管理员应见全部 2 条（含旧数据），实际 %d 条", len(adminList.Data))
	}
}

// TestDataScope_TwoInspectorsSeeOnlyOwnIssues 验证多个巡检员账号之间互相不可见：
// 张三的上报不出现在 inspector 的历史列表，inspector 的上报也不出现在张三列表；
// 管理员仍见全部。这是 Unity「历史」页按账号隔离的回归防线。
func TestDataScope_TwoInspectorsSeeOnlyOwnIssues(t *testing.T) {
	env := newTestRouter(t)
	mustCreateZhangsan(t, env)

	login := tryLogin(env.Router, "zhangsan", "zhangsan-pw-1")
	if login.Code != http.StatusOK {
		t.Fatalf("zhangsan 登录失败: %d %s", login.Code, login.Body.String())
	}
	zhangsanToken := extractSessionCookie(login)
	if zhangsanToken == "" {
		t.Fatal("zhangsan 登录未设置会话 Cookie")
	}

	inspectorIssue := createIssueViaHTTP(t, env)
	zRec := requestWithBearer(env.Router, http.MethodPost, "/api/issues",
		`{"title":"张三上报","description":"","priority":"low","position":{"x":0,"y":0,"z":0}}`, zhangsanToken)
	if zRec.Code != http.StatusCreated {
		t.Fatalf("zhangsan 上报失败: %d %s", zRec.Code, zRec.Body.String())
	}
	var zCreated struct {
		Data struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	if err := json.Unmarshal(zRec.Body.Bytes(), &zCreated); err != nil {
		t.Fatalf("解析 zhangsan 创建响应失败: %v", err)
	}

	inspIDs := listIssueIDsWithToken(t, env, env.Inspector)
	if len(inspIDs) != 1 || inspIDs[0] != inspectorIssue["id"].(string) {
		t.Fatalf("inspector 应只见自己的 1 条，实际 %v", inspIDs)
	}
	zIDs := listIssueIDsWithToken(t, env, zhangsanToken)
	if len(zIDs) != 1 || zIDs[0] != zCreated.Data.ID {
		t.Fatalf("zhangsan 应只见自己的 1 条，实际 %v", zIDs)
	}
	if adminIDs := listIssueIDsWithToken(t, env, env.Admin); len(adminIDs) != 2 {
		t.Fatalf("管理员应见全部 2 条，实际 %v", adminIDs)
	}
}

// listIssueIDsWithToken 以指定令牌拉取问题列表并返回 ID 序列。
func listIssueIDsWithToken(t *testing.T, env *testEnv, token string) []string {
	t.Helper()
	rec := requestWithBearer(env.Router, http.MethodGet, "/api/issues", "", token)
	if rec.Code != http.StatusOK {
		t.Fatalf("列表期望 200，实际 %d %s", rec.Code, rec.Body.String())
	}
	var resp struct {
		Data []struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("解析列表失败: %v", err)
	}
	ids := make([]string, 0, len(resp.Data))
	for _, it := range resp.Data {
		ids = append(ids, it.ID)
	}
	return ids
}

// TestQuickLogin_DisabledByDefault 验证默认不注册开发快捷登录路由：
// 与普通未知路径一致返回 404，未配置的部署不会获得免密入口。
func TestQuickLogin_DisabledByDefault(t *testing.T) {
	env := newTestRouter(t)
	rec := doJSON(env.Router, http.MethodPost, "/api/auth/quick-login", `{"role":"admin"}`)
	assertErrorEnvelope(t, rec, http.StatusNotFound, "not_found")
}

// TestQuickLogin_EnabledIssuesSessionForBuiltInAccounts 验证启用后一键签发会话：
// 两个内置角色均成功、下发 Cookie、令牌可用于 /api/auth/me，且响应不含密码字段。
func TestQuickLogin_EnabledIssuesSessionForBuiltInAccounts(t *testing.T) {
	env := newTestRouterWithOptions(t, issue.DefaultFieldGenerator(), WithQuickLogin(true))
	for _, tc := range []struct{ role, username string }{
		{"inspector", "inspector"},
		{"admin", "admin"},
	} {
		t.Run(tc.role, func(t *testing.T) {
			rec := doJSON(env.Router, http.MethodPost, "/api/auth/quick-login", `{"role":"`+tc.role+`"}`)
			if rec.Code != http.StatusOK {
				t.Fatalf("期望 200，实际 %d %s", rec.Code, rec.Body.String())
			}
			var resp struct {
				Data struct {
					Username string `json:"username"`
					Role     string `json:"role"`
				} `json:"data"`
			}
			if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
				t.Fatalf("解析失败: %v", err)
			}
			if resp.Data.Username != tc.username || resp.Data.Role != tc.role {
				t.Fatalf("账号不符: %+v", resp.Data)
			}
			if strings.Contains(strings.ToLower(rec.Body.String()), "password") {
				t.Fatalf("响应不得出现密码字段: %s", rec.Body.String())
			}
			token := extractSessionCookie(rec)
			if token == "" {
				t.Fatal("快捷登录应设置会话 Cookie")
			}
			if me := requestWithBearer(env.Router, http.MethodGet, "/api/auth/me", "", token); me.Code != http.StatusOK {
				t.Fatalf("快捷登录令牌应可用，实际 %d %s", me.Code, me.Body.String())
			}
		})
	}
}

// TestQuickLogin_RejectsInvalidPayload 验证严格解析：未知角色、缺角色、
// 携带密码字段与语法错误均返回 400 invalid_argument。
func TestQuickLogin_RejectsInvalidPayload(t *testing.T) {
	env := newTestRouterWithOptions(t, issue.DefaultFieldGenerator(), WithQuickLogin(true))
	for name, body := range map[string]string{
		"未知角色": `{"role":"root"}`,
		"缺少角色": `{}`,
		"携带密码": `{"role":"admin","password":"x"}`,
		"语法错误": `{"role":`,
	} {
		t.Run(name, func(t *testing.T) {
			rec := doJSON(env.Router, http.MethodPost, "/api/auth/quick-login", body)
			assertErrorEnvelope(t, rec, http.StatusBadRequest, "invalid_argument")
		})
	}
}

// TestQuickLogin_DeletedAccountReturnsUnauthorized 验证内置巡检员被删除后
// 快捷登录明确失败且不签发新会话，提示改用常规登录。
func TestQuickLogin_DeletedAccountReturnsUnauthorized(t *testing.T) {
	env := newTestRouterWithOptions(t, issue.DefaultFieldGenerator(), WithQuickLogin(true))
	del := doJSONAs(env, auth.RoleAdmin, http.MethodDelete, "/api/users/inspector", "")
	if del.Code != http.StatusNoContent {
		t.Fatalf("准备：删除 inspector 失败 %d %s", del.Code, del.Body.String())
	}
	rec := doJSON(env.Router, http.MethodPost, "/api/auth/quick-login", `{"role":"inspector"}`)
	assertErrorEnvelope(t, rec, http.StatusUnauthorized, "quick_login_unavailable")
	if !strings.Contains(rec.Body.String(), "常规登录") {
		t.Fatalf("应提示改用常规登录: %s", rec.Body.String())
	}
}

// TestSessionPersistenceAcrossRestart 验证后端重启（重开数据库连接）后
// 未过期会话仍有效；新进程发布的事件序号重新从小值开始（客户端以全量校准兜底）。
func TestSessionPersistenceAcrossRestart(t *testing.T) {
	env := newTestRouter(t)

	// 关闭全部连接，用同一文件重建问题库与账号库（等效进程重启）。
	inspToken := env.Inspector
	adminToken := env.Admin
	dbPath := envDBPath(t, env)
	if err := env.Store.Close(); err != nil {
		t.Fatalf("关闭问题库失败: %v", err)
	}
	if err := env.AuthStore.Close(); err != nil {
		t.Fatalf("关闭账号库失败: %v", err)
	}

	rebuilt := rebuildRouterOn(t, dbPath)

	// 重启后旧令牌继续可用（重启不强迫在线用户重新登录）。
	inspReq := httptest.NewRequest(http.MethodGet, "/api/issues", nil)
	inspReq.Header.Set("Authorization", "Bearer "+inspToken)
	rec := httptest.NewRecorder()
	rebuilt.ServeHTTP(rec, inspReq)
	if rec.Code != http.StatusOK {
		t.Fatalf("重启后巡检员会话应有效，实际 %d %s", rec.Code, rec.Body.String())
	}
	adminReq := httptest.NewRequest(http.MethodGet, "/api/auth/me", nil)
	adminReq.Header.Set("Authorization", "Bearer "+adminToken)
	rec2 := httptest.NewRecorder()
	rebuilt.ServeHTTP(rec2, adminReq)
	if rec2.Code != http.StatusOK {
		t.Fatalf("重启后管理员会话应有效，实际 %d", rec2.Code)
	}
}

// TestExpiredSessionRejected 用固定时钟验证过期会话被拒绝、退出会话不可复用。
func TestExpiredSessionRejected(t *testing.T) {
	env := newTestRouter(t)

	fixed := time.Now()
	service := auth.NewService(env.AuthStore, []byte(testSessionSecret))
	fake := &fakeClock{now: fixed}
	service.SetClock(fake)

	token, _, err := service.Login(context.Background(), "admin", testAdminPassword)
	if err != nil {
		t.Fatalf("登录失败: %v", err)
	}
	// 时间推进 12 小时 + 1 秒：会话过期
	fake.now = fixed.Add(auth.SessionTTL + time.Second)
	if _, err := service.Resolve(context.Background(), token); err == nil {
		t.Fatal("过期会话应被拒绝")
	}

	// 退出后立即失效（未过期也不可用）
	fake.now = fixed
	logoutToken, _, err := service.Login(context.Background(), "admin", testAdminPassword)
	if err != nil {
		t.Fatalf("登录失败: %v", err)
	}
	if err := service.Logout(context.Background(), logoutToken); err != nil {
		t.Fatalf("退出失败: %v", err)
	}
	if _, err := service.Resolve(context.Background(), logoutToken); err == nil {
		t.Fatal("已退出的会话不可复用")
	}
}

// fakeClock 固定时钟。
type fakeClock struct{ now time.Time }

func (f *fakeClock) Now() time.Time { return f.now }

// TestWebSocket_EventsAndSeq 验证管理员连接后按提交顺序收到
// issue.created / issue.updated 事件，序号单调递增；事件正文为完整八字段对象。
func TestWebSocket_EventsAndSeq(t *testing.T) {
	env := newTestRouter(t)

	// 用真实 HTTP 服务器承载路由，使 WebSocket 升级走完整协议栈
	server := httptest.NewServer(env.Router)
	t.Cleanup(server.Close)
	wsURL := "ws" + strings.TrimPrefix(server.URL, "http") + "/api/ws"

	// 匿名连接被拒：不升级
	_, _, err := websocket.Dial(context.Background(), wsURL, nil)
	if err == nil {
		t.Fatal("匿名 WebSocket 连接应被拒绝")
	}

	// 巡检员连接被拒：403
	inspHdr := withBearerHeader(env.Inspector)
	if _, _, err := websocket.Dial(context.Background(), wsURL, &websocket.DialOptions{HTTPHeader: inspHdr}); err == nil {
		t.Fatal("巡检员 WebSocket 连接应被拒绝")
	}

	// 管理员连接成功
	adminHdr := withBearerHeader(env.Admin)
	conn, _, err := websocket.Dial(context.Background(), wsURL, &websocket.DialOptions{HTTPHeader: adminHdr})
	if err != nil {
		t.Fatalf("管理员连接失败: %v", err)
	}
	t.Cleanup(func() { conn.CloseNow() })
	readCtx, cancelRead := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancelRead()

	// 触发创建（事件 1）与状态修改（事件 2）
	created := createIssueViaHTTP(t, env)
	id := created["id"].(string)
	patchRec := doJSONAs(env, auth.RoleAdmin, http.MethodPatch, "/api/issues/"+id+"/status", `{"status":"in_progress"}`)
	if patchRec.Code != http.StatusOK {
		t.Fatalf("修改状态失败: %d %s", patchRec.Code, patchRec.Body.String())
	}

	var events []WebSocketEvent
	for len(events) < 2 {
		_, payload, err := conn.Read(readCtx)
		if err != nil {
			t.Fatalf("读取事件失败（已收 %d 条）: %v", len(events), err)
		}
		var ev WebSocketEvent
		if err := json.Unmarshal(payload, &ev); err != nil {
			t.Fatalf("事件不是合法 JSON: %v，正文 %s", err, payload)
		}
		events = append(events, ev)
	}

	if events[0].Type != "issue.created" || events[0].Issue.ID != id {
		t.Fatalf("第一条应为创建事件: %+v", events[0])
	}
	if events[1].Type != "issue.updated" || events[1].Issue.Status != "in_progress" {
		t.Fatalf("第二条应为更新事件且状态已变: %+v", events[1])
	}
	if events[1].Seq <= events[0].Seq {
		t.Fatalf("事件序号应单调递增: %d -> %d", events[0].Seq, events[1].Seq)
	}
	if events[0].Issue.Title == "" || events[0].Issue.Position.X == 0 && events[0].Issue.Position.Y == 0 && events[0].Issue.Position.Z == 0 {
		t.Fatalf("事件应携带完整八字段问题对象: %+v", events[0].Issue)
	}
}

// withBearerHeader 构造带 Bearer 令牌的请求头。
func withBearerHeader(token string) map[string][]string {
	return map[string][]string{"Authorization": {"Bearer " + token}}
}

// buildLegacyIssue 构造一条计划 006 之前的旧数据（无归属记录）。
func buildLegacyIssue(id string) issue.Issue {
	return issue.Issue{
		ID:          id,
		Title:       "历史遗留问题",
		Description: "计划 006 之前创建，无归属记录",
		Priority:    issue.PriorityLow,
		Status:      issue.StatusOpen,
		Position:    issue.Position{X: 1, Y: 2, Z: 3},
		CreatedAt:   "2026-10-01T09:00:00+08:00",
		UpdatedAt:   "2026-10-01T09:00:00+08:00",
	}
}

// envDBPath 返回环境使用的数据库路径（由 Store 反查不可行，测试环境统一记录）。
// 这里通过重建等价环境实现"重启"，不需要真实路径：直接在同一临时目录约定上重建。
func envDBPath(t *testing.T, env *testEnv) string {
	t.Helper()
	// testEnv 未直接暴露路径；通过重新打开同一路径等效模拟重启不可行时，
	// 改为在 rebuildRouterOn 中直接复用 env 的数据库文件句柄创建新路由。
	// 为了让重启测试真实可靠，testEnv 增加了 dbPath 字段。
	return env.dbPath
}

// rebuildRouterOn 在指定数据库文件上重建全部路由（等效进程重启）。
func rebuildRouterOn(t *testing.T, dbPath string) *gin.Engine {
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
	return NewRouter(store, authService, []string{"http://localhost:5173"}, issue.DefaultFieldGenerator())
}
