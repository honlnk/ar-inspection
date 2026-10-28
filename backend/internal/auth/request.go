package auth

import (
	"bytes"
	"encoding/json"
	"fmt"
	"sort"
)

// LoginRequest 登录请求字段；仅接受 username/password，未知字段拒绝。
type LoginRequest struct {
	Username string `json:"username"`
	Password string `json:"password"`
}

var loginRequestFields = map[string]struct{}{"username": {}, "password": {}}

// DecodeLoginRequest 严格解析登录请求：单个 JSON 对象、仅含两个字段、均为字符串。
// 与问题接口的严格解析保持同一风格，尽早发现字段拼写偏差。
func DecodeLoginRequest(data []byte) (LoginRequest, error) {
	dec := json.NewDecoder(bytes.NewReader(data))
	var raw map[string]json.RawMessage
	if err := dec.Decode(&raw); err != nil {
		return LoginRequest{}, &ValidationError{Message: "请求体必须是单个合法的 JSON 对象"}
	}
	if dec.More() {
		return LoginRequest{}, &ValidationError{Message: "请求体只能包含一个 JSON 对象"}
	}
	unknown := make([]string, 0, len(raw))
	for key := range raw {
		if _, ok := loginRequestFields[key]; !ok {
			unknown = append(unknown, key)
		}
	}
	if len(unknown) > 0 {
		sort.Strings(unknown)
		return LoginRequest{}, &ValidationError{Message: fmt.Sprintf("包含未知字段 %q", unknown[0])}
	}
	var req LoginRequest
	if err := json.Unmarshal(data, &req); err != nil {
		return LoginRequest{}, &ValidationError{Message: "用户名与密码必须是字符串"}
	}
	return req, nil
}

// ---- 开发期快捷登录请求解码 ----

// QuickLoginRequest 开发快捷登录请求：仅接受 role（inspector/admin），
// 不接受密码字段——客户端与仓库都不保存任何明文口令。
type QuickLoginRequest struct {
	Role Role `json:"role"`
}

var quickLoginRequestFields = map[string]struct{}{"role": {}}

// DecodeQuickLoginRequest 严格解析：单个 JSON 对象、仅 role 字段、角色枚举合法。
func DecodeQuickLoginRequest(data []byte) (QuickLoginRequest, error) {
	dec := json.NewDecoder(bytes.NewReader(data))
	var raw map[string]json.RawMessage
	if err := dec.Decode(&raw); err != nil {
		return QuickLoginRequest{}, &ValidationError{Message: "请求体必须是单个合法的 JSON 对象"}
	}
	if dec.More() {
		return QuickLoginRequest{}, &ValidationError{Message: "请求体只能包含一个 JSON 对象"}
	}
	unknown := make([]string, 0, len(raw))
	for key := range raw {
		if _, ok := quickLoginRequestFields[key]; !ok {
			unknown = append(unknown, key)
		}
	}
	if len(unknown) > 0 {
		sort.Strings(unknown)
		return QuickLoginRequest{}, &ValidationError{Message: fmt.Sprintf("包含未知字段 %q", unknown[0])}
	}
	var req QuickLoginRequest
	if err := json.Unmarshal(data, &req); err != nil {
		return QuickLoginRequest{}, &ValidationError{Message: "role 必须是字符串"}
	}
	if !req.Role.Valid() {
		return QuickLoginRequest{}, &ValidationError{Message: "role 仅支持 inspector 或 admin"}
	}
	return req, nil
}

// ---- 计划 008：人员管理请求解码（与登录同一严格解析风格）----

// CreateUserRequest 新增巡检员请求；仅接受三个字段，未知字段拒绝。
type CreateUserRequest struct {
	Username    string  `json:"username"`
	DisplayName string  `json:"displayName"`
	Password    *string `json:"password"`
}

var createUserRequestFields = map[string]struct{}{"username": {}, "displayName": {}, "password": {}}

// DecodeCreateUserRequest 严格解析新增请求：单个 JSON 对象、仅含三个字段、均为字符串。
func DecodeCreateUserRequest(data []byte) (CreateUserRequest, error) {
	dec := json.NewDecoder(bytes.NewReader(data))
	var raw map[string]json.RawMessage
	if err := dec.Decode(&raw); err != nil {
		return CreateUserRequest{}, &ValidationError{Message: "请求体必须是单个合法的 JSON 对象"}
	}
	if dec.More() {
		return CreateUserRequest{}, &ValidationError{Message: "请求体只能包含一个 JSON 对象"}
	}
	unknown := make([]string, 0, len(raw))
	for key := range raw {
		if _, ok := createUserRequestFields[key]; !ok {
			unknown = append(unknown, key)
		}
	}
	if len(unknown) > 0 {
		sort.Strings(unknown)
		return CreateUserRequest{}, &ValidationError{Message: fmt.Sprintf("包含未知字段 %q", unknown[0])}
	}
	var req CreateUserRequest
	if err := json.Unmarshal(data, &req); err != nil {
		return CreateUserRequest{}, &ValidationError{Message: "字段值必须是字符串"}
	}
	return req, nil
}

// UpdateUserRequest 修改巡检员请求：显示名与密码均可选，至少一项。
type UpdateUserRequest struct {
	DisplayName *string `json:"displayName"`
	Password    *string `json:"password"`
}

var updateUserRequestFields = map[string]struct{}{"displayName": {}, "password": {}}

// DecodeUpdateUserRequest 严格解析修改请求：单个 JSON 对象、仅含两个字段、均为字符串。
func DecodeUpdateUserRequest(data []byte) (UpdateUserRequest, error) {
	dec := json.NewDecoder(bytes.NewReader(data))
	var raw map[string]json.RawMessage
	if err := dec.Decode(&raw); err != nil {
		return UpdateUserRequest{}, &ValidationError{Message: "请求体必须是单个合法的 JSON 对象"}
	}
	if dec.More() {
		return UpdateUserRequest{}, &ValidationError{Message: "请求体只能包含一个 JSON 对象"}
	}
	unknown := make([]string, 0, len(raw))
	for key := range raw {
		if _, ok := updateUserRequestFields[key]; !ok {
			unknown = append(unknown, key)
		}
	}
	if len(unknown) > 0 {
		sort.Strings(unknown)
		return UpdateUserRequest{}, &ValidationError{Message: fmt.Sprintf("包含未知字段 %q", unknown[0])}
	}
	var req UpdateUserRequest
	if err := json.Unmarshal(data, &req); err != nil {
		return UpdateUserRequest{}, &ValidationError{Message: "字段值必须是字符串"}
	}
	if req.DisplayName == nil && req.Password == nil {
		return UpdateUserRequest{}, &ValidationError{Message: "至少提供显示名或密码一项修改"}
	}
	return req, nil
}
