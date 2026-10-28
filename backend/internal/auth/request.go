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
