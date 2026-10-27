// HTTP 客户端与响应契约检查：统一信封解析、超时/网络错误分类与可读 JSON 输出。
package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"net"
	"net/http"
	"strings"
	"time"
)

// exitCodeError 携带进程退出码的错误。
type exitCodeError struct {
	code    int
	message string
}

func (e *exitCodeError) Error() string { return e.message }

func requestError(format string, args ...any) *exitCodeError {
	return &exitCodeError{code: exitRequestFailed, message: fmt.Sprintf(format, args...)}
}

// position 保持三端统一的嵌套坐标结构；x/y/z 由人工输入。
type position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// createRequest 只携带四类客户端字段；id、status、时间一律由服务端生成。
type createRequest struct {
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Position    position `json:"position"`
}

// issueFields 与三端统一的问题字段一致，用于成功响应契约检查。
type issueFields struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Status      string   `json:"status"`
	Position    position `json:"position"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`
}

// envelope 后端统一响应结构；成功与错误互斥。
type envelope struct {
	Data  json.RawMessage `json:"data"`
	Error *struct {
		Code    string `json:"code"`
		Message string `json:"message"`
	} `json:"error"`
}

var validPriorities = map[string]bool{"low": true, "medium": true, "high": true}
var validStatuses = map[string]bool{"open": true, "in_progress": true, "resolved": true}

// createIssue 向后端创建问题并返回完整响应体；HTTP 错误、网络错误、超时与契约错误
// 均以 exitCodeError 返回（HTTP 错误时响应体一并返回用于完整展示）。
func createIssue(baseURL string, timeout time.Duration, req createRequest) ([]byte, *exitCodeError) {
	payload, err := json.Marshal(req)
	if err != nil {
		return nil, requestError("构造请求体失败：%v", err)
	}
	body, status, cerr := doRequest(http.MethodPost, apiURL(baseURL, "/api/issues"), timeout, payload)
	if cerr != nil {
		return nil, cerr
	}
	if !isSuccessStatus(status) {
		return body, httpStatusError(body, status)
	}
	if verr := validateCreatedIssue(body); verr != nil {
		return body, verr
	}
	return body, nil
}

// listIssues 查询问题列表并返回完整响应体；空数组合法，失败不得伪装为空列表。
func listIssues(baseURL string, timeout time.Duration) ([]byte, *exitCodeError) {
	body, status, cerr := doRequest(http.MethodGet, apiURL(baseURL, "/api/issues"), timeout, nil)
	if cerr != nil {
		return nil, cerr
	}
	if !isSuccessStatus(status) {
		return body, httpStatusError(body, status)
	}
	if verr := validateIssueList(body); verr != nil {
		return body, verr
	}
	return body, nil
}

func apiURL(baseURL, path string) string {
	return strings.TrimRight(baseURL, "/") + path
}

func isSuccessStatus(status int) bool {
	return status >= 200 && status < 300
}

// doRequest 发送请求并读取完整响应体；网络错误与超时转换为中文诊断。
func doRequest(method, url string, timeout time.Duration, payload []byte) ([]byte, int, *exitCodeError) {
	var reader io.Reader
	if payload != nil {
		reader = bytes.NewReader(payload)
	}
	httpReq, err := http.NewRequest(method, url, reader)
	if err != nil {
		return nil, 0, requestError("构造请求失败：%v", err)
	}
	if payload != nil {
		httpReq.Header.Set("Content-Type", "application/json")
	}
	httpReq.Header.Set("Accept", "application/json")

	client := &http.Client{Timeout: timeout}
	resp, err := client.Do(httpReq)
	if err != nil {
		if isTimeoutError(err) {
			return nil, 0, requestError("请求超时（%s），后端未在时限内响应；工具不自动重试，请确认后端状态后手动重试", timeout)
		}
		return nil, 0, requestError("无法连接后端：%v", err)
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, resp.StatusCode, requestError("读取响应失败：%v", err)
	}
	return body, resp.StatusCode, nil
}

func isTimeoutError(err error) bool {
	var netErr net.Error
	return errors.As(err, &netErr) && netErr.Timeout()
}

// httpStatusError 从错误响应中提取错误码与中文信息；无法解析时仅报告状态码。
func httpStatusError(body []byte, status int) *exitCodeError {
	var env envelope
	if err := json.Unmarshal(body, &env); err == nil && env.Error != nil {
		return requestError("接口返回错误（HTTP %d，%s）：%s", status, env.Error.Code, env.Error.Message)
	}
	return requestError("接口返回错误（HTTP %d）", status)
}

// parseEnvelope 解析统一信封，要求成功响应为带 data 字段的 JSON 对象。
func parseEnvelope(body []byte) (*envelope, *exitCodeError) {
	var env envelope
	if err := json.Unmarshal(body, &env); err != nil {
		return nil, requestError("后端响应不是合法 JSON")
	}
	if env.Data == nil {
		return nil, requestError("后端成功响应缺少 data 字段")
	}
	return &env, nil
}

// validateCreatedIssue 检查创建成功结果：完整问题结构、非空 ID、默认 open 与服务端时间。
func validateCreatedIssue(body []byte) *exitCodeError {
	env, cerr := parseEnvelope(body)
	if cerr != nil {
		return cerr
	}
	var created issueFields
	if err := json.Unmarshal(env.Data, &created); err != nil {
		return requestError("创建结果结构不正确：%v", err)
	}
	if cerr := checkIssueFields(created); cerr != nil {
		return cerr
	}
	if created.Status != "open" {
		return requestError("创建结果默认状态应为 open，实际为 %q", created.Status)
	}
	return nil
}

// validateIssueList 检查列表结果：data 必须是数组（空数组合法），逐项做基本结构检查。
func validateIssueList(body []byte) *exitCodeError {
	env, cerr := parseEnvelope(body)
	if cerr != nil {
		return cerr
	}
	var items []issueFields
	if err := json.Unmarshal(env.Data, &items); err != nil {
		return requestError("列表 data 必须是问题数组：%v", err)
	}
	for index, item := range items {
		if cerr := checkIssueFields(item); cerr != nil {
			return requestError("第 %d 条问题结构不正确：%s", index+1, cerr.message)
		}
	}
	return nil
}

// checkIssueFields 校验单条问题的基本契约：非空 ID 与标题、合法枚举、有限坐标与服务端时间。
func checkIssueFields(item issueFields) *exitCodeError {
	if item.ID == "" {
		return requestError("问题缺少非空 id")
	}
	if item.Title == "" {
		return requestError("问题缺少非空 title")
	}
	if !validPriorities[item.Priority] {
		return requestError("问题优先级 %q 不在 low/medium/high 之内", item.Priority)
	}
	if !validStatuses[item.Status] {
		return requestError("问题状态 %q 不在 open/in_progress/resolved 之内", item.Status)
	}
	for name, value := range map[string]float64{"x": item.Position.X, "y": item.Position.Y, "z": item.Position.Z} {
		if err := checkFinite(name, value); err != "" {
			return requestError("问题坐标 %s 非法：%s", name, err)
		}
	}
	if !validServerTime(item.CreatedAt) {
		return requestError("问题 createdAt 不是合法的 RFC 3339 服务端时间：%q", item.CreatedAt)
	}
	if !validServerTime(item.UpdatedAt) {
		return requestError("问题 updatedAt 不是合法的 RFC 3339 服务端时间：%q", item.UpdatedAt)
	}
	return nil
}

func checkFinite(name string, value float64) string {
	if math.IsNaN(value) || math.IsInf(value, 0) {
		return fmt.Sprintf("%s = %v 不是有限数值", name, value)
	}
	return ""
}

func validServerTime(value string) bool {
	_, err := time.Parse(time.RFC3339, value)
	return err == nil
}

// writeJSON 以缩进格式完整输出响应体；非 JSON 内容原样输出。
func writeJSON(w io.Writer, body []byte) {
	var buf bytes.Buffer
	if err := json.Indent(&buf, body, "", "  "); err != nil {
		_, _ = w.Write(append(bytes.TrimRight(body, "\n"), '\n'))
		return
	}
	_, _ = buf.WriteString("\n")
	_, _ = w.Write(buf.Bytes())
}
