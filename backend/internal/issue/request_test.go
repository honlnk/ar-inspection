package issue

import (
	"errors"
	"math"
	"regexp"
	"strings"
	"testing"
	"time"
)

// validCreateJSON 是合法创建请求基准，负例在其基础上改动单个字段。
const validCreateJSON = `{
	"title": "入口墙面破损",
	"description": "左侧墙体存在裂缝",
	"priority": "high",
	"position": { "x": 0.42, "y": 0.03, "z": 1.26 }
}`

// fixedGen 返回固定 ID 与时间的生成器，断言不受真实随机与时钟影响。
func fixedGen() FieldGenerator {
	return FieldGenerator{
		NewID:  func() (string, error) { return "fixed-id", nil },
		NewNow: func() (string, error) { return "2026-10-27T12:00:00+08:00", nil },
	}
}

// assertValidationError 断言 err 是 *ValidationError 且信息非空。
func assertValidationError(t *testing.T, err error) {
	t.Helper()
	var vErr *ValidationError
	if !errors.As(err, &vErr) {
		t.Fatalf("期望 *ValidationError，实际 %T：%v", err, err)
	}
	if strings.TrimSpace(vErr.Message) == "" {
		t.Fatal("校验错误信息不能为空")
	}
}

// TestDecodeCreateRequest_ValidCases 覆盖合法边界：完整字段、省略描述、中文标题、
// 零与负数小数坐标（阶段 1 门禁：0、负数坐标被接受）。
func TestDecodeCreateRequest_ValidCases(t *testing.T) {
	cases := []struct {
		name string
		body string
	}{
		{"完整字段", validCreateJSON},
		{"省略描述", `{"title":"t","priority":"low","position":{"x":0,"y":-1.5,"z":2}}`},
		{"零与负数坐标", `{"title":"t","priority":"medium","position":{"x":0,"y":-0.1,"z":-99}}`},
		{"描述为空字符串", `{"title":"t","description":"","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"中文标题多行描述", `{"title":"巡检·问题 一","description":"第一行\n第二行","priority":"low","position":{"x":1,"y":2,"z":3}}`},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			req, err := DecodeCreateRequest([]byte(tc.body))
			if err != nil {
				t.Fatalf("合法请求不应报错: %v", err)
			}
			if req.Title == nil || req.Priority == nil || req.Position == nil {
				t.Fatalf("title/priority/position 必须存在: %+v", req)
			}
			if req.Position.X == nil || req.Position.Y == nil || req.Position.Z == nil {
				t.Fatalf("坐标轴必须存在: %+v", req.Position)
			}
		})
	}
}

// TestDecodeCreateRequest_InvalidJSON 覆盖负例：语法错误、数组代替对象、
// 未知字段（含服务端字段）、字段类型错误、对象后额外内容、超出 float64 的数字。
func TestDecodeCreateRequest_InvalidJSON(t *testing.T) {
	cases := []struct {
		name string
		body string
	}{
		{"语法错误", `{"title": "t",`},
		{"空请求体", ``},
		{"数组代替对象", `[{"title":"t"}]`},
		{"字符串代替对象", `"not an object"`},
		{"数字代替对象", `42`},
		{"两个连续对象", `{"title":"a","priority":"low","position":{"x":1,"y":2,"z":3}}{"title":"b"}`},
		{"对象后额外文本", `{"title":"a"} 额外内容`},
		{"未知字段", `{"foo":1,"title":"a","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"传入 id", `{"id":"x","title":"a","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"传入 status", `{"status":"open","title":"a","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"传入 createdAt", `{"createdAt":"2026-01-01T00:00:00+08:00","title":"a","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"传入 updatedAt", `{"updatedAt":"2026-01-01T00:00:00+08:00","title":"a","priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"标题类型错误", `{"title":123,"priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"优先级类型错误", `{"title":"a","priority":1,"position":{"x":1,"y":2,"z":3}}`},
		{"描述类型错误", `{"title":"a","description":true,"priority":"low","position":{"x":1,"y":2,"z":3}}`},
		{"位置类型错误", `{"title":"a","priority":"low","position":"0,0,0"}`},
		{"坐标类型错误", `{"title":"a","priority":"low","position":{"x":"0.1","y":2,"z":3}}`},
		{"坐标超出范围", `{"title":"a","priority":"low","position":{"x":1e400,"y":2,"z":3}}`},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			_, err := DecodeCreateRequest([]byte(tc.body))
			assertValidationError(t, err)
		})
	}
}

// TestDecodeStatusUpdateRequest_Valid 覆盖三种合法状态。
func TestDecodeStatusUpdateRequest_Valid(t *testing.T) {
	for _, s := range []Status{StatusOpen, StatusInProgress, StatusResolved} {
		body := `{"status":"` + string(s) + `"}`
		got, err := DecodeStatusUpdateRequest([]byte(body))
		if err != nil {
			t.Fatalf("合法状态 %s 不应报错: %v", s, err)
		}
		if got != s {
			t.Fatalf("期望 %s，实际 %s", s, got)
		}
	}
}

// TestDecodeStatusUpdateRequest_Invalid 覆盖状态接口负例：结构错误、未知字段、
// 缺失、类型错误与非法枚举（含大写和别名，不做大小写转换或别名兼容）。
func TestDecodeStatusUpdateRequest_Invalid(t *testing.T) {
	cases := []struct {
		name string
		body string
	}{
		{"语法错误", `{"status":`},
		{"数组代替对象", `["open"]`},
		{"未知字段", `{"status":"open","title":"a"}`},
		{"缺失状态", `{}`},
		{"null 状态", `{"status":null}`},
		{"状态类型错误", `{"status":1}`},
		{"非法枚举", `{"status":"done"}`},
		{"大写枚举", `{"status":"OPEN"}`},
		{"空字符串", `{"status":""}`},
		{"对象后额外内容", `{"status":"open"}{}`},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			_, err := DecodeStatusUpdateRequest([]byte(tc.body))
			assertValidationError(t, err)
		})
	}
}

// decodeValid 构造基准合法请求。
func decodeValid(t *testing.T) CreateRequest {
	t.Helper()
	req, err := DecodeCreateRequest([]byte(validCreateJSON))
	if err != nil {
		t.Fatalf("解析基准请求失败: %v", err)
	}
	return req
}

// TestBuildNewIssue_Valid 覆盖领域校验与规范化：标题 trim、描述原样保留（不 trim）、
// 优先级精确匹配、零/负数坐标接受、服务端字段统一生成。
func TestBuildNewIssue_Valid(t *testing.T) {
	req := decodeValid(t)
	req.Title = ptr("  入口墙面破损  ")
	req.Description = ptr("  描述保留首尾空白\n第二行  ")

	it, err := req.BuildNewIssue(fixedGen())
	if err != nil {
		t.Fatalf("合法请求不应报错: %v", err)
	}
	if it.Title != "入口墙面破损" {
		t.Fatalf("标题应保存 trim 后的值，实际 %q", it.Title)
	}
	if it.Description != "  描述保留首尾空白\n第二行  " {
		t.Fatalf("描述应原样保存，实际 %q", it.Description)
	}
	if it.Priority != PriorityHigh {
		t.Fatalf("优先级不一致: %s", it.Priority)
	}
	if it.Status != StatusOpen {
		t.Fatalf("新问题状态必须为 open，实际 %s", it.Status)
	}
	if it.ID != "fixed-id" || it.CreatedAt != "2026-10-27T12:00:00+08:00" || it.UpdatedAt != it.CreatedAt {
		t.Fatalf("服务端字段应来自生成器: %+v", it)
	}
	if it.Position != (Position{X: 0.42, Y: 0.03, Z: 1.26}) {
		t.Fatalf("坐标不一致: %+v", it.Position)
	}

	// 零与负数坐标合法。
	req = decodeValid(t)
	req.Position = &PositionRequest{X: ptr(0.0), Y: ptr(-1.5), Z: ptr(-99.0)}
	if _, err := req.BuildNewIssue(fixedGen()); err != nil {
		t.Fatalf("零与负数坐标应被接受: %v", err)
	}
}

// TestBuildNewIssue_Invalid 覆盖领域负例：空标题（含全空格）、缺失/非法优先级、
// 缺失位置、缺失坐标轴、NaN 与正负无穷（直接构造，标准 JSON 无法表达）。
func TestBuildNewIssue_Invalid(t *testing.T) {
	newReq := func(mutate func(*CreateRequest)) CreateRequest {
		req := decodeValid(t)
		mutate(&req)
		return req
	}
	cases := []struct {
		name   string
		mutate func(*CreateRequest)
	}{
		{"缺失标题", func(r *CreateRequest) { r.Title = nil }},
		{"空字符串标题", func(r *CreateRequest) { r.Title = ptr("") }},
		{"全空格标题", func(r *CreateRequest) { r.Title = ptr("   \t ") }},
		{"缺失优先级", func(r *CreateRequest) { r.Priority = nil }},
		{"非法优先级", func(r *CreateRequest) { r.Priority = ptr("urgent") }},
		{"大写优先级", func(r *CreateRequest) { r.Priority = ptr("HIGH") }},
		{"别名优先级", func(r *CreateRequest) { r.Priority = ptr("高") }},
		{"缺失位置", func(r *CreateRequest) { r.Position = nil }},
		{"缺失 x 轴", func(r *CreateRequest) { r.Position.X = nil }},
		{"缺失 z 轴", func(r *CreateRequest) { r.Position.Z = nil }},
		{"NaN 坐标", func(r *CreateRequest) { r.Position.Y = ptr(math.NaN()) }},
		{"正无穷坐标", func(r *CreateRequest) { r.Position.X = ptr(math.Inf(1)) }},
		{"负无穷坐标", func(r *CreateRequest) { r.Position.Z = ptr(math.Inf(-1)) }},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			_, err := newReq(tc.mutate).BuildNewIssue(fixedGen())
			assertValidationError(t, err)
		})
	}
}

// TestBuildNewIssue_CannotOverrideServerFields 验证创建请求无法覆盖
// ID、默认状态和服务端时间（阶段 1 门禁）。
func TestBuildNewIssue_CannotOverrideServerFields(t *testing.T) {
	req := decodeValid(t)
	it, err := req.BuildNewIssue(fixedGen())
	if err != nil {
		t.Fatalf("合法请求不应报错: %v", err)
	}
	if it.ID != "fixed-id" || it.Status != StatusOpen || it.CreatedAt != it.UpdatedAt {
		t.Fatalf("服务端字段必须由领域逻辑生成: %+v", it)
	}
}

// TestBuildNewIssue_DefaultGeneratorFields 验证默认生成器产出的
// ID 为 UUID v4、时间可解析为 RFC 3339 且偏移为 +08:00（阶段 1 门禁）。
func TestBuildNewIssue_DefaultGeneratorFields(t *testing.T) {
	req := decodeValid(t)
	it, err := req.BuildNewIssue(DefaultFieldGenerator())
	if err != nil {
		t.Fatalf("默认生成器不应报错: %v", err)
	}
	const uuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"
	if !regexp.MustCompile(uuidPattern).MatchString(it.ID) {
		t.Fatalf("ID 应为 UUID v4 格式，实际 %q", it.ID)
	}
	ts, err := time.Parse(time.RFC3339, it.CreatedAt)
	if err != nil {
		t.Fatalf("createdAt 应可解析为 RFC 3339: %v", err)
	}
	if _, offset := ts.Zone(); offset != 8*60*60 {
		t.Fatalf("时间偏移应为 +08:00，实际 %d 秒", offset)
	}
	if it.UpdatedAt != it.CreatedAt {
		t.Fatalf("初始 updatedAt 应等于 createdAt: %s != %s", it.UpdatedAt, it.CreatedAt)
	}
}

// TestBuildNewIssue_GeneratorFailures 验证 ID 或时间生成失败返回
// 非 ValidationError 的错误（HTTP 层映射为 500），不降级、不出具虚假成功。
func TestBuildNewIssue_GeneratorFailures(t *testing.T) {
	req := decodeValid(t)

	failID := FieldGenerator{
		NewID:  func() (string, error) { return "", errors.New("随机源不可用") },
		NewNow: func() (string, error) { return "2026-10-27T12:00:00+08:00", nil },
	}
	if _, err := req.BuildNewIssue(failID); err == nil {
		t.Fatal("ID 生成失败必须返回错误")
	} else {
		var vErr *ValidationError
		if errors.As(err, &vErr) {
			t.Fatalf("ID 生成失败不是参数错误: %v", err)
		}
	}

	failNow := FieldGenerator{
		NewID:  func() (string, error) { return "id", nil },
		NewNow: func() (string, error) { return "", errors.New("时钟不可用") },
	}
	if _, err := req.BuildNewIssue(failNow); err == nil {
		t.Fatal("时间生成失败必须返回错误")
	} else {
		var vErr *ValidationError
		if errors.As(err, &vErr) {
			t.Fatalf("时间生成失败不是参数错误: %v", err)
		}
	}
}

// TestNewUUID_UniqueAndFormatted 验证默认 UUID 生成的格式稳定性与唯一性。
func TestNewUUID_UniqueAndFormatted(t *testing.T) {
	const uuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"
	seen := make(map[string]struct{}, 1000)
	for i := 0; i < 1000; i++ {
		id, err := newUUIDv4()
		if err != nil {
			t.Fatalf("生成 UUID 失败: %v", err)
		}
		if !regexp.MustCompile(uuidPattern).MatchString(id) {
			t.Fatalf("UUID 格式不正确: %q", id)
		}
		if _, dup := seen[id]; dup {
			t.Fatalf("UUID 重复: %q", id)
		}
		seen[id] = struct{}{}
	}
}

// ptr 返回入参指针，简化测试构造。
func ptr[T any](v T) *T { return &v }
