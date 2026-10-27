// HTTP 行为与响应契约测试：通过本地测试服务验证请求方法、路径、请求头、
// 请求体字段、错误链路、超时与“失败不得伪装为空列表/成功”。
package main

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

const sampleIssueJSON = `{
  "id": "018f3c2e-5b1a-7c3d-9e4f-0a1b2c3d4e5f",
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "status": "open",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 },
  "createdAt": "2026-10-27T10:00:00+08:00",
  "updatedAt": "2026-10-27T10:00:00+08:00"
}`

func runTool(t *testing.T, args ...string) (int, string, string) {
	t.Helper()
	var stdout, stderr bytes.Buffer
	code := run(args, &stdout, &stderr)
	return code, stdout.String(), stderr.String()
}

// captureServer 记录请求方法、路径、头部与请求体，并按配置返回响应。
type captured struct {
	Method      string
	Path        string
	ContentType string
	Accept      string
	Body        string
}

func newCaptureServer(status int, response string, capturedOut *captured, countOut *int) *httptest.Server {
	return httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if capturedOut != nil {
			bodyBytes, _ := io.ReadAll(r.Body)
			capturedOut.Method = r.Method
			capturedOut.Path = r.URL.Path
			capturedOut.ContentType = r.Header.Get("Content-Type")
			capturedOut.Accept = r.Header.Get("Accept")
			capturedOut.Body = string(bodyBytes)
		}
		if countOut != nil {
			*countOut++
		}
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(status)
		_, _ = w.Write([]byte(response))
	}))
}

func TestCreateSendsContractRequest(t *testing.T) {
	var cap captured
	server := newCaptureServer(http.StatusCreated, `{"data":`+sampleIssueJSON+`}`, &cap, nil)
	defer server.Close()

	code, stdout, stderr := runTool(t, "create", "-base-url", server.URL,
		"-title", "入口墙面破损", "-description", "左侧墙体存在裂缝", "-priority", "high",
		"-x=0.42", "-y=0.03", "-z=1.26")

	if code != exitOK {
		t.Fatalf("期望退出码 0，实际 %d，stderr=%s", code, stderr)
	}
	if cap.Method != http.MethodPost {
		t.Errorf("期望 POST，实际 %s", cap.Method)
	}
	if cap.Path != "/api/issues" {
		t.Errorf("期望路径 /api/issues，实际 %s", cap.Path)
	}
	if cap.ContentType != "application/json" {
		t.Errorf("期望 Content-Type application/json，实际 %q", cap.ContentType)
	}
	if cap.Accept != "application/json" {
		t.Errorf("期望 Accept application/json，实际 %q", cap.Accept)
	}

	var body map[string]json.RawMessage
	if err := json.Unmarshal([]byte(cap.Body), &body); err != nil {
		t.Fatalf("请求体不是合法 JSON：%v，body=%s", err, cap.Body)
	}
	// 只发送四类客户端字段，不得携带服务端字段
	wantKeys := []string{"title", "description", "priority", "position"}
	if len(body) != len(wantKeys) {
		t.Errorf("请求体字段集合应为 %v，实际 %d 个：%s", wantKeys, len(body), cap.Body)
	}
	for _, key := range wantKeys {
		if _, ok := body[key]; !ok {
			t.Errorf("请求体缺少字段 %s", key)
		}
	}
	var pos struct {
		X float64 `json:"x"`
		Y float64 `json:"y"`
		Z float64 `json:"z"`
	}
	if err := json.Unmarshal(body["position"], &pos); err != nil {
		t.Fatalf("position 不是嵌套对象：%v", err)
	}
	if pos.X != 0.42 || pos.Y != 0.03 || pos.Z != 1.26 {
		t.Errorf("坐标数值未保持：%+v", pos)
	}

	if !strings.Contains(stdout, `"data"`) || !strings.Contains(stdout, `"id": "018f3c2e-5b1a-7c3d-9e4f-0a1b2c3d4e5f"`) {
		t.Errorf("stdout 应完整展示成功响应，实际：%s", stdout)
	}
}

func TestCreateSendsNegativeAndDecimalCoordinates(t *testing.T) {
	var cap captured
	server := newCaptureServer(http.StatusCreated, `{"data":`+sampleIssueJSON+`}`, &cap, nil)
	defer server.Close()

	code, _, stderr := runTool(t, "create", "-base-url", server.URL, "-title", "负坐标", "-x=-1.5", "-y=0", "-z=3.25")
	if code != exitOK {
		t.Fatalf("期望退出码 0，实际 %d，stderr=%s", code, stderr)
	}
	var pos struct {
		X float64 `json:"x"`
		Y float64 `json:"y"`
		Z float64 `json:"z"`
	}
	var body struct {
		Position struct {
			X float64 `json:"x"`
			Y float64 `json:"y"`
			Z float64 `json:"z"`
		} `json:"position"`
	}
	if err := json.Unmarshal([]byte(cap.Body), &body); err != nil {
		t.Fatalf("请求体解析失败：%v", err)
	}
	pos = body.Position
	if pos.X != -1.5 || pos.Y != 0 || pos.Z != 3.25 {
		t.Errorf("负数/零/小数坐标应原样发送：%+v", pos)
	}
}

func TestCreateInvalidInputsPassThroughAsBackendErrors(t *testing.T) {
	cases := []struct {
		name     string
		args     []string
		wantBody string
	}{
		{
			name:     "空标题原样发送",
			args:     []string{"create", "-base-url", "REPLACE", "-title", "", "-priority", "high"},
			wantBody: `"title":""`,
		},
		{
			name:     "非法优先级原样发送",
			args:     []string{"create", "-base-url", "REPLACE", "-title", "有标题", "-priority", "urgent"},
			wantBody: `"priority":"urgent"`,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			var cap captured
			server := newCaptureServer(http.StatusBadRequest,
				`{"error":{"code":"invalid_argument","message":"标题不能为空"}}`, &cap, nil)
			defer server.Close()

			args := make([]string, len(tc.args))
			copy(args, tc.args)
			for i, arg := range args {
				if arg == "REPLACE" {
					args[i] = server.URL
				}
			}
			code, stdout, stderr := runTool(t, args...)

			if code != exitRequestFailed {
				t.Fatalf("接口错误应退出码 1，实际 %d", code)
			}
			if !strings.Contains(cap.Body, tc.wantBody) {
				t.Errorf("请求体应原样包含 %s，实际 %s", tc.wantBody, cap.Body)
			}
			if !strings.Contains(stderr, "HTTP 400") || !strings.Contains(stderr, "invalid_argument") {
				t.Errorf("stderr 应含状态码与错误码，实际：%s", stderr)
			}
			if !strings.Contains(stdout, "invalid_argument") || !strings.Contains(stdout, "标题不能为空") {
				t.Errorf("stdout 应完整展示后端错误 JSON，实际：%s", stdout)
			}
		})
	}
}

func TestCreateDoesNotAutoRetry(t *testing.T) {
	requests := 0
	server := newCaptureServer(http.StatusInternalServerError,
		`{"error":{"code":"internal_error","message":"内部错误"}}`, nil, &requests)
	defer server.Close()

	code, _, _ := runTool(t, "create", "-base-url", server.URL, "-title", "不重试")

	if code != exitRequestFailed {
		t.Fatalf("期望退出码 1，实际 %d", code)
	}
	if requests != 1 {
		t.Errorf("创建失败不应自动重试，服务端收到 %d 次请求", requests)
	}
}

func TestListRequestsAndOutputs(t *testing.T) {
	var cap captured
	server := newCaptureServer(http.StatusOK, `{"data":[`+sampleIssueJSON+`]}`, &cap, nil)
	defer server.Close()

	code, stdout, stderr := runTool(t, "list", "-base-url", server.URL)

	if code != exitOK {
		t.Fatalf("期望退出码 0，实际 %d，stderr=%s", code, stderr)
	}
	if cap.Method != http.MethodGet {
		t.Errorf("期望 GET，实际 %s", cap.Method)
	}
	if cap.Path != "/api/issues" {
		t.Errorf("期望路径 /api/issues，实际 %s", cap.Path)
	}
	if !strings.Contains(stdout, "入口墙面破损") {
		t.Errorf("stdout 应完整展示列表响应，实际：%s", stdout)
	}
}

func TestListEmptyArraySucceeds(t *testing.T) {
	server := newCaptureServer(http.StatusOK, `{"data":[]}`, nil, nil)
	defer server.Close()

	code, stdout, stderr := runTool(t, "list", "-base-url", server.URL)

	if code != exitOK {
		t.Fatalf("空数组合法应退出码 0，实际 %d，stderr=%s", code, stderr)
	}
	if !strings.Contains(stdout, "[]") {
		t.Errorf("stdout 应展示空数组，实际：%s", stdout)
	}
}

func TestFailuresNeverDisguisedAsEmptyListOrSuccess(t *testing.T) {
	cases := []struct {
		name     string
		status   int
		response string
	}{
		{"data 非数组", http.StatusOK, `{"data":{"id":"x"}}`},
		{"data 缺失", http.StatusOK, `{"error":null}`},
		{"接口错误", http.StatusInternalServerError, `{"error":{"code":"internal_error","message":"内部错误"}}`},
		{"非 JSON 响应", http.StatusOK, `not-json`},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			server := newCaptureServer(tc.status, tc.response, nil, nil)
			defer server.Close()

			code, stdout, stderr := runTool(t, "list", "-base-url", server.URL)

			if code != exitRequestFailed {
				t.Fatalf("list 失败应退出码 1，实际 %d，stdout=%s", code, stdout)
			}
			if !strings.Contains(stderr, "失败：") {
				t.Errorf("stderr 应有中文诊断，实际：%s", stderr)
			}
		})
	}
}

func TestCreateBadSuccessContractFails(t *testing.T) {
	cases := []struct {
		name     string
		response string
	}{
		{
			name:     "缺少 id",
			response: `{"data":{"title":"t","description":"","priority":"high","status":"open","position":{"x":1,"y":2,"z":3},"createdAt":"2026-10-27T10:00:00+08:00","updatedAt":"2026-10-27T10:00:00+08:00"}}`,
		},
		{
			name:     "默认状态不是 open",
			response: `{"data":{"id":"i-1","title":"t","description":"","priority":"high","status":"resolved","position":{"x":1,"y":2,"z":3},"createdAt":"2026-10-27T10:00:00+08:00","updatedAt":"2026-10-27T10:00:00+08:00"}}`,
		},
		{
			name:     "缺少服务端时间",
			response: `{"data":{"id":"i-1","title":"t","description":"","priority":"high","status":"open","position":{"x":1,"y":2,"z":3},"createdAt":"","updatedAt":""}}`,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			server := newCaptureServer(http.StatusCreated, tc.response, nil, nil)
			defer server.Close()

			code, _, stderr := runTool(t, "create", "-base-url", server.URL, "-title", "t")

			if code != exitRequestFailed {
				t.Fatalf("契约错误应退出码 1，实际 %d，stderr=%s", code, stderr)
			}
			if !strings.Contains(stderr, "失败：") {
				t.Errorf("stderr 应有中文诊断，实际：%s", stderr)
			}
		})
	}
}

func TestListBadItemContractFails(t *testing.T) {
	response := `{"data":[{"id":"i-1","title":"t","description":"","priority":"urgent","status":"open","position":{"x":1,"y":2,"z":3},"createdAt":"2026-10-27T10:00:00+08:00","updatedAt":"2026-10-27T10:00:00+08:00"}]}`
	server := newCaptureServer(http.StatusOK, response, nil, nil)
	defer server.Close()

	code, _, stderr := runTool(t, "list", "-base-url", server.URL)

	if code != exitRequestFailed {
		t.Fatalf("列表含非法枚举项应退出码 1，实际 %d", code)
	}
	if !strings.Contains(stderr, "urgent") {
		t.Errorf("stderr 应指出非法枚举，实际：%s", stderr)
	}
}

func TestConnectionFailed(t *testing.T) {
	server := newCaptureServer(http.StatusOK, `{"data":[]}`, nil, nil)
	url := server.URL
	server.Close() // 立即关闭，制造连接失败

	code, _, stderr := runTool(t, "create", "-base-url", url, "-title", "连接失败")

	if code != exitRequestFailed {
		t.Fatalf("连接失败应退出码 1，实际 %d", code)
	}
	if !strings.Contains(stderr, "无法连接后端") {
		t.Errorf("stderr 应有连接失败诊断，实际：%s", stderr)
	}
}

func TestRequestTimeout(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		time.Sleep(200 * time.Millisecond)
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte(`{"data":[]}`))
	}))
	defer server.Close()

	code, _, stderr := runTool(t, "list", "-base-url", server.URL, "-timeout", "50ms")

	if code != exitRequestFailed {
		t.Fatalf("超时应退出码 1，实际 %d", code)
	}
	if !strings.Contains(stderr, "请求超时") {
		t.Errorf("stderr 应有超时诊断，实际：%s", stderr)
	}
}
