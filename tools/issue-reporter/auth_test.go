// 计划 006 会话流程测试：登录换取令牌、Bearer 携带、登录失败不回退匿名、
// Set-Cookie 令牌提取与会话参数用法约束。
package main

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
)

// authTestServer 模拟后端：/api/auth/login 校验凭据并发会话 Cookie；
// /api/issues 要求 Bearer 令牌（未带或错令牌返回 401），巡检员视角返回其上报列表。
func authTestServer(t *testing.T, validUser, validPass string, issueRequests *[]string, bearerTokens *[]string) *httptest.Server {
	t.Helper()
	var mu sync.Mutex
	return httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/api/auth/login":
			var req struct {
				Username string `json:"username"`
				Password string `json:"password"`
			}
			_ = json.NewDecoder(r.Body).Decode(&req)
			if req.Username != validUser || req.Password != validPass {
				w.Header().Set("Content-Type", "application/json")
				w.WriteHeader(http.StatusUnauthorized)
				_, _ = w.Write([]byte(`{"error":{"code":"invalid_credentials","message":"用户名或密码错误"}}`))
				return
			}
			w.Header().Set("Content-Type", "application/json")
			// 与真实后端一致：HttpOnly Cookie 下发会话令牌
			http.SetCookie(w, &http.Cookie{Name: "inspection_session", Value: "sess-token-1", HttpOnly: true, Path: "/"})
			_, _ = w.Write([]byte(fmt.Sprintf(`{"data":{"username":%q,"displayName":"巡检员","role":"inspector"}}`, validUser)))
		case "/api/issues":
			if r.Method == http.MethodPost {
				mu.Lock()
				*issueRequests = append(*issueRequests, "create")
				mu.Unlock()
			}
			auth := r.Header.Get("Authorization")
			mu.Lock()
			*bearerTokens = append(*bearerTokens, auth)
			mu.Unlock()
			w.Header().Set("Content-Type", "application/json")
			if auth != "Bearer sess-token-1" {
				w.WriteHeader(http.StatusUnauthorized)
				_, _ = w.Write([]byte(`{"error":{"code":"authentication_required","message":"未认证"}}`))
				return
			}
			if r.Method == http.MethodGet {
				_, _ = w.Write([]byte(`{"data":[]}`))
				return
			}
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(`{"data":{"id":"id-1","title":"t","description":"","priority":"medium","status":"open","position":{"x":0,"y":0,"z":0},"createdAt":"2026-10-28T10:00:00+08:00","updatedAt":"2026-10-28T10:00:00+08:00"}}`))
		default:
			http.NotFound(w, r)
		}
	}))
}

func TestLoginThenCreateCarriesBearerToken(t *testing.T) {
	var issueRequests []string
	var bearers []string
	server := authTestServer(t, "inspector", "pw123", &issueRequests, &bearers)
	defer server.Close()

	code, stdout, stderr := runTool(t, "create", "-base-url", server.URL,
		"-username", "inspector", "-password", "pw123", "-title", "登录后创建")

	if code != exitOK {
		t.Fatalf("期望退出码 0，实际 %d，stderr=%s stdout=%s", code, stderr, stdout)
	}
	if len(issueRequests) != 1 {
		t.Fatalf("登录成功后应恰好发起一次创建请求，实际 %d 次", len(issueRequests))
	}
	if len(bearers) == 0 || bearers[len(bearers)-1] != "Bearer sess-token-1" {
		t.Fatalf("创建请求应携带登录换取的 Bearer 令牌，实际 %v", bearers)
	}
	if !strings.Contains(stdout, "登录成功") {
		t.Errorf("stdout 应展示登录成功信息：%s", stdout)
	}
}

func TestLoginFailureDoesNotFallBackToAnonymous(t *testing.T) {
	var issueRequests []string
	var bearers []string
	server := authTestServer(t, "inspector", "pw123", &issueRequests, &bearers)
	defer server.Close()

	code, _, stderr := runTool(t, "list", "-base-url", server.URL,
		"-username", "inspector", "-password", "WRONG")

	if code != exitRequestFailed {
		t.Fatalf("登录失败应退出码 1，实际 %d", code)
	}
	if !strings.Contains(stderr, "用户名或密码错误") {
		t.Errorf("stderr 应展示后端统一凭据错误：%s", stderr)
	}
	if len(issueRequests) != 0 {
		t.Fatalf("登录失败后不得继续发起匿名业务请求（回退匿名被禁止），实际发起了 %d 次", len(issueRequests))
	}
}

func TestTokenFlagSkipsLoginAndCarriesHeader(t *testing.T) {
	var issueRequests []string
	var bearers []string
	server := authTestServer(t, "inspector", "pw123", &issueRequests, &bearers)
	defer server.Close()

	code, _, stderr := runTool(t, "list", "-base-url", server.URL, "-token", "sess-token-1")
	if code != exitOK {
		t.Fatalf("期望退出码 0，实际 %d，stderr=%s", code, stderr)
	}
	if len(bearers) != 1 || bearers[0] != "Bearer sess-token-1" {
		t.Fatalf("-token 应直接以 Bearer 携带且不重复登录，实际 %v", bearers)
	}
}

func TestAnonymousBusinessCallShows401WithHint(t *testing.T) {
	var issueRequests []string
	var bearers []string
	server := authTestServer(t, "inspector", "pw123", &issueRequests, &bearers)
	defer server.Close()

	code, _, stderr := runTool(t, "list", "-base-url", server.URL)
	if code != exitRequestFailed {
		t.Fatalf("匿名调用应失败退出码 1，实际 %d", code)
	}
	if !strings.Contains(stderr, "HTTP 401") || !strings.Contains(stderr, "-username") {
		t.Errorf("401 错误应附带会话参数提示：%s", stderr)
	}
}

func TestTokenAndUsernameTogetherIsUsageError(t *testing.T) {
	code, _, stderr := runTool(t, "list", "-base-url", "http://127.0.0.1:1",
		"-token", "x", "-username", "inspector", "-password", "pw")
	if code != exitUsage {
		t.Fatalf("-token 与 -username 同时提供应为用法错误（2），实际 %d", code)
	}
	if !strings.Contains(stderr, "会话参数用法") {
		t.Errorf("stderr 应说明会话参数用法：%s", stderr)
	}
}

func TestPartialCredentialsIsUsageError(t *testing.T) {
	code, _, _ := runTool(t, "list", "-base-url", "http://127.0.0.1:1", "-username", "inspector")
	if code != exitUsage {
		t.Fatalf("只给用户名不给密码应为用法错误，实际 %d", code)
	}
}

func TestExtractSessionToken(t *testing.T) {
	cases := []struct {
		header string
		want   string
	}{
		{"inspection_session=abc; Path=/; Max-Age=43200; HttpOnly; SameSite=Lax", "abc"},
		{"inspection_session=tok", "tok"},
		{"other=x; Path=/", ""},
		{"", ""},
	}
	for _, c := range cases {
		if got := extractSessionToken(c.header); got != c.want {
			t.Errorf("extractSessionToken(%q) = %q，期望 %q", c.header, got, c.want)
		}
	}
}

func TestLoginNetworkFailureFailsFast(t *testing.T) {
	// 指向不存在的端口：登录网络失败必须直接失败且不发起业务请求
	code, _, stderr := runTool(t, "list", "-base-url", "http://127.0.0.1:1",
		"-username", "inspector", "-password", "pw")
	if code != exitRequestFailed {
		t.Fatalf("网络失败应退出码 1，实际 %d", code)
	}
	if !strings.Contains(stderr, "无法连接后端") {
		t.Errorf("stderr 应提示无法连接后端：%s", stderr)
	}
}
