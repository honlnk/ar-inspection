package issue

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"sort"
	"strings"
)

// ValidationError 表示请求参数不合法；HTTP 层据此映射为 400 invalid_argument。
// Message 为明确中文，不包含 SQL、堆栈或本机路径。
type ValidationError struct {
	Message string
}

func (e *ValidationError) Error() string { return e.Message }

// CreateRequest 创建问题的客户端请求字段；指针用于区分“字段缺失”与“零值”。
// 仅接受 title/description/priority/position；客户端传入 id、status 或服务端时间
// 会作为未知字段在解码阶段被拒绝。
type CreateRequest struct {
	Title       *string          `json:"title"`
	Description *string          `json:"description"`
	Priority    *string          `json:"priority"`
	Position    *PositionRequest `json:"position"`
}

// PositionRequest 位置请求字段；x/y/z 均为指针以区分“缺失”与“0 坐标”。
type PositionRequest struct {
	X *float64 `json:"x"`
	Y *float64 `json:"y"`
	Z *float64 `json:"z"`
}

// createRequestFields 与 statusRequestFields 是两种请求允许的字段白名单，
// 用于在类型解码前先拒绝未知字段，尽早发现字段拼写偏差。
var (
	createRequestFields = map[string]struct{}{
		"title": {}, "description": {}, "priority": {}, "position": {},
	}
	statusRequestFields = map[string]struct{}{"status": {}}
)

// DecodeCreateRequest 严格解析创建请求：必须是单个完整 JSON 对象，
// 拒绝语法错误、字段类型错误、未知字段与对象后的额外内容。
func DecodeCreateRequest(data []byte) (CreateRequest, error) {
	if _, err := decodeStrictObject(data, createRequestFields); err != nil {
		return CreateRequest{}, err
	}
	var req CreateRequest
	if err := unmarshalStrict(data, &req); err != nil {
		return CreateRequest{}, err
	}
	return req, nil
}

// DecodeStatusUpdateRequest 严格解析状态修改请求并返回合法状态：
// 必须是单个 JSON 对象且仅含 status 字段，状态值必须为合法枚举。
// 按“先校验 JSON 和状态值，再查询 ID”的顺序约束，由调用方拿合法状态去查库。
func DecodeStatusUpdateRequest(data []byte) (Status, error) {
	if _, err := decodeStrictObject(data, statusRequestFields); err != nil {
		return "", err
	}
	var req struct {
		Status *string `json:"status"`
	}
	if err := unmarshalStrict(data, &req); err != nil {
		return "", err
	}
	if req.Status == nil {
		return "", &ValidationError{Message: "必须提供状态 status"}
	}
	status := Status(*req.Status)
	if !status.Valid() {
		return "", &ValidationError{Message: "状态必须是 open、in_progress、resolved 之一"}
	}
	return status, nil
}

// BuildNewIssue 校验创建请求并由领域逻辑生成完整问题实体：
// 统一设置 ID、默认状态 open 与北京时间，初始 createdAt == updatedAt，
// HTTP 层不自行拼装服务端字段。参数不合法返回 *ValidationError；
// ID 或时间生成失败返回其他错误，由 HTTP 层映射为服务器错误。
func (r CreateRequest) BuildNewIssue(gen FieldGenerator) (Issue, error) {
	if r.Title == nil || strings.TrimSpace(*r.Title) == "" {
		return Issue{}, &ValidationError{Message: "标题不能为空"}
	}
	title := strings.TrimSpace(*r.Title)

	description := ""
	if r.Description != nil {
		description = *r.Description
	}

	if r.Priority == nil {
		return Issue{}, &ValidationError{Message: "必须提供优先级 priority"}
	}
	priority := Priority(*r.Priority)
	if !priority.Valid() {
		return Issue{}, &ValidationError{Message: "优先级必须是 low、medium、high 之一"}
	}

	if r.Position == nil {
		return Issue{}, &ValidationError{Message: "必须提供位置 position"}
	}
	position, err := validPosition(*r.Position)
	if err != nil {
		return Issue{}, err
	}

	id, err := gen.NewID()
	if err != nil {
		return Issue{}, fmt.Errorf("生成问题 ID 失败: %w", err)
	}
	now, err := gen.NewNow()
	if err != nil {
		return Issue{}, fmt.Errorf("生成时间失败: %w", err)
	}
	return Issue{
		ID:          id,
		Title:       title,
		Description: description,
		Priority:    priority,
		Status:      StatusOpen,
		Position:    position,
		CreatedAt:   now,
		UpdatedAt:   now,
	}, nil
}

// validPosition 校验位置坐标：x/y/z 必须全部存在且为有限数值；
// 零和负数合法，NaN 与正负无穷非法。
func validPosition(p PositionRequest) (Position, error) {
	if p.X == nil || p.Y == nil || p.Z == nil {
		return Position{}, &ValidationError{Message: "位置必须包含 x、y、z 三个数值坐标"}
	}
	axes := []struct {
		name string
		v    float64
	}{{"x", *p.X}, {"y", *p.Y}, {"z", *p.Z}}
	for _, a := range axes {
		if math.IsNaN(a.v) || math.IsInf(a.v, 0) {
			return Position{}, &ValidationError{
				Message: fmt.Sprintf("位置坐标 %s 必须是有限数值，不接受 NaN 或无穷", a.name),
			}
		}
	}
	return Position{X: *p.X, Y: *p.Y, Z: *p.Z}, nil
}

// decodeStrictObject 把请求体解码为单个 JSON 对象并执行白名单外的未知字段拒绝：
// 拒绝非对象（数组、字符串、数字）、语法错误与对象后的额外非空白内容。
func decodeStrictObject(data []byte, allowed map[string]struct{}) (map[string]json.RawMessage, error) {
	dec := json.NewDecoder(bytes.NewReader(data))
	var raw map[string]json.RawMessage
	if err := dec.Decode(&raw); err != nil {
		return nil, &ValidationError{Message: "请求体必须是单个合法的 JSON 对象"}
	}
	if dec.More() {
		return nil, &ValidationError{Message: "请求体只能包含一个 JSON 对象"}
	}
	unknown := make([]string, 0, len(raw))
	for key := range raw {
		if _, ok := allowed[key]; !ok {
			unknown = append(unknown, key)
		}
	}
	if len(unknown) > 0 {
		sort.Strings(unknown) // 保证多字段时错误信息稳定
		return nil, &ValidationError{Message: fmt.Sprintf("包含未知字段 %q", unknown[0])}
	}
	return raw, nil
}

// unmarshalStrict 把已通过白名单检查的请求体解码到具体结构，
// 字段类型错误（含超出 float64 范围的数字）翻译为中文校验错误。
func unmarshalStrict(data []byte, v any) error {
	if err := json.Unmarshal(data, v); err != nil {
		var typeErr *json.UnmarshalTypeError
		if errors.As(err, &typeErr) && typeErr.Field != "" {
			return &ValidationError{Message: fmt.Sprintf("字段 %s 的类型不正确", typeErr.Field)}
		}
		return &ValidationError{Message: "请求体必须是单个合法的 JSON 对象"}
	}
	return nil
}
