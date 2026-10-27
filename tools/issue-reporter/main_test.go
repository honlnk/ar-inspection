// 命令入口测试：子命令分发、参数校验、退出码与帮助文案边界说明。
package main

import (
	"net/http"
	"strings"
	"testing"
)

func TestNoArgsShowsUsage(t *testing.T) {
	code, _, stderr := runTool(t)

	if code != exitUsage {
		t.Fatalf("无参数应退出码 2，实际 %d", code)
	}
	if !strings.Contains(stderr, "issue-reporter") || !strings.Contains(stderr, "create") {
		t.Errorf("无参数应展示用法说明，实际：%s", stderr)
	}
}

func TestUnknownCommand(t *testing.T) {
	code, _, stderr := runTool(t, "delete", "-base-url", "http://localhost:1")

	if code != exitUsage {
		t.Fatalf("未知命令应退出码 2，实际 %d", code)
	}
	if !strings.Contains(stderr, "未知命令") {
		t.Errorf("stderr 应说明未知命令，实际：%s", stderr)
	}
}

func TestHelpExitsZero(t *testing.T) {
	code, stdout, _ := runTool(t, "help")

	if code != exitOK {
		t.Fatalf("help 应退出码 0，实际 %d", code)
	}
	if !strings.Contains(stdout, "模拟坐标，不代表真实 AR 位置") {
		t.Errorf("帮助文案必须标注坐标为人工输入的模拟坐标，实际：%s", stdout)
	}
	if !strings.Contains(stdout, "退出码") {
		t.Errorf("帮助文案应说明退出码，实际：%s", stdout)
	}
}

func TestCreateRejectsNonFiniteCoordinates(t *testing.T) {
	for _, coord := range []string{"NaN", "Inf", "+Inf"} {
		code, _, stderr := runTool(t, "create", "-base-url", "http://localhost:1", "-title", "t", "-x="+coord)

		if code != exitUsage {
			t.Fatalf("-x=%s 应本地拒绝并退出码 2，实际 %d", coord, code)
		}
		if !strings.Contains(stderr, "有限数值") {
			t.Errorf("stderr 应说明坐标必须有限，实际：%s", stderr)
		}
	}
}

func TestBadFlagIsUsageError(t *testing.T) {
	code, _, stderr := runTool(t, "create", "-base-url", "http://localhost:1", "-no-such-flag")

	if code != exitUsage {
		t.Fatalf("未知参数应退出码 2，实际 %d", code)
	}
	if !strings.Contains(stderr, "参数解析失败") {
		t.Errorf("stderr 应说明参数解析失败，实际：%s", stderr)
	}
}

func TestExtraPositionalArgsRejected(t *testing.T) {
	code, _, stderr := runTool(t, "list", "-base-url", "http://localhost:1", "extra")

	if code != exitUsage {
		t.Fatalf("多余参数应退出码 2，实际 %d", code)
	}
	if !strings.Contains(stderr, "不接受多余参数") {
		t.Errorf("stderr 应说明多余参数，实际：%s", stderr)
	}
}

func TestDefaultsPointToLocalBackend(t *testing.T) {
	// 不发起真实请求，仅验证帮助中的默认值说明与常量一致
	_, _, stderr := runTool(t)

	if !strings.Contains(stderr, defaultBaseURL) {
		t.Errorf("用法说明应包含默认后端地址 %s", defaultBaseURL)
	}
	if !strings.Contains(stderr, defaultTimeout.String()) {
		t.Errorf("用法说明应包含默认超时 %s", defaultTimeout)
	}
}

func TestCreateUsesPostAgainstIssuesPath(t *testing.T) {
	var captured captured
	server := newCaptureServer(http.StatusCreated, `{"data":`+sampleIssueJSON+`}`, &captured, nil)
	defer server.Close()

	code, _, stderr := runTool(t, "create", "-base-url", server.URL, "-title", "默认参数")

	if code != exitOK {
		t.Fatalf("期望退出码 0，实际 %d，stderr=%s", code, stderr)
	}
	if captured.Method != http.MethodPost || captured.Path != "/api/issues" {
		t.Errorf("create 应 POST /api/issues，实际 %s %s", captured.Method, captured.Path)
	}
	if !strings.Contains(captured.Body, `"priority":"medium"`) {
		t.Errorf("优先级默认 medium，实际请求体：%s", captured.Body)
	}
	if !strings.Contains(captured.Body, `"description":""`) {
		t.Errorf("创建请求应始终包含 description 字段，实际请求体：%s", captured.Body)
	}
}
