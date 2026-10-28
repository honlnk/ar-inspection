// issue-reporter 是 Unity HTTP 上报的命令行模拟工具（开发辅助程序，不是正式产品端）。
// 以真实 HTTP 请求代替 Unity 客户端向后端创建/查询问题，用于接口联调与回归排障；
// 不验证任何 AR 能力。计划 006 起业务接口需要会话：提供账号凭据时先登录再以
// Bearer 令牌调用，登录失败直接失败、不自动回退匿名请求。
// 退出码：0 成功且契约通过；1 请求或契约失败；2 用法错误。
package main

import (
	"flag"
	"fmt"
	"io"
	"math"
	"os"
	"time"
)

const (
	exitOK            = 0
	exitRequestFailed = 1
	exitUsage         = 2

	defaultBaseURL = "http://localhost:8080"
	defaultTimeout = 10 * time.Second
)

const commandHelp = `issue-reporter —— Unity HTTP 上报模拟工具（开发辅助，不是正式产品端）

代替 Unity 客户端以真实 HTTP 请求向后端上报问题，用于联调与回归。
注意：坐标为人工输入的模拟坐标，不代表真实 AR 位置；本工具不验证 AR 能力。
计划 006 起业务接口需要登录会话（与 Unity 巡检端同一套账号体系）。

用法：
  issue-reporter create [参数]   创建问题（POST /api/issues）
  issue-reporter list   [参数]   查询问题列表（GET /api/issues）

通用参数：
  -base-url string    后端基础地址（默认 "http://localhost:8080"）
  -timeout duration   请求超时（默认 10s）；超时按失败处理，不自动重试
  -username string    登录用户名（如 inspector）；提供后先登录换取会话令牌
  -password string    登录密码；登录失败直接失败，不回退匿名请求
  -token string       已有会话令牌（与 -username/-password 二选一，避免重复登录）

create 额外参数：
  -title string         问题标题，默认空字符串（空标题会被后端拒绝，用于验证错误链路）
  -description string   问题描述，默认空字符串；创建请求始终包含该字段
  -priority string      优先级 low|medium|high，默认 medium；非法值原样发送，由后端校验
  -x/-y/-z float        三轴坐标，默认均为 0；支持零、负数与小数（负数请用 -x=-1.5 写法）

成功与接口错误都会完整输出后端 JSON 响应；本地参数、网络、超时与契约问题输出中文诊断。
退出码：0 成功且响应契约通过；1 HTTP 错误、网络错误、超时或响应契约错误；2 命令用法错误
`

func main() {
	os.Exit(run(os.Args[1:], os.Stdout, os.Stderr))
}

// run 解析子命令并执行；返回进程退出码。输出通过注入便于测试。
func run(args []string, stdout, stderr io.Writer) int {
	if len(args) == 0 {
		fmt.Fprint(stderr, commandHelp)
		return exitUsage
	}
	command, rest := args[0], args[1:]
	switch command {
	case "help", "-h", "--help":
		fmt.Fprint(stdout, commandHelp)
		return exitOK
	case "create":
		return runCreate(rest, stdout, stderr)
	case "list":
		return runList(rest, stdout, stderr)
	default:
		fmt.Fprintf(stderr, "未知命令 %q。\n\n%s", command, commandHelp)
		return exitUsage
	}
}

// newFlagSet 静默 flag 自带输出，由调用方统一给中文诊断。
func newFlagSet(name string, output io.Writer) *flag.FlagSet {
	fs := flag.NewFlagSet(name, flag.ContinueOnError)
	fs.SetOutput(output)
	return fs
}

// authFlags 通用会话参数：-username/-password/-token。
type authFlags struct {
	username *string
	password *string
	token    *string
}

func addAuthFlags(fs *flag.FlagSet) authFlags {
	return authFlags{
		username: fs.String("username", "", "登录用户名"),
		password: fs.String("password", "", "登录密码"),
		token:    fs.String("token", "", "已有会话令牌"),
	}
}

// resolveToken 决定本次请求使用的会话令牌：
// 显式 -token 直接使用；提供账号凭据则先登录换取（并展示登录响应）；
// 两者都缺省返回空串（匿名请求，后端将返回 401，由错误提示引导补参数）。
// -token 与 -username 同时提供视为用法错误，避免歧义。
func resolveToken(auth authFlags, baseURL string, timeout time.Duration, stdout, stderr io.Writer) (string, int) {
	if *auth.token != "" && (*auth.username != "" || *auth.password != "") {
		return "", exitUsage
	}
	if *auth.token != "" {
		return *auth.token, exitOK
	}
	if *auth.username == "" && *auth.password == "" {
		return "", exitOK
	}
	if *auth.username == "" || *auth.password == "" {
		return "", exitUsage
	}
	body, token, cerr := loginForToken(baseURL, timeout, *auth.username, *auth.password)
	if cerr != nil {
		fmt.Fprintf(stderr, "失败：%s\n", cerr.message)
		if len(body) > 0 {
			writeJSON(stdout, body)
		}
		return "", exitRequestFailed
	}
	fmt.Fprintf(stdout, "登录成功（%s）\n", *auth.username)
	writeJSON(stdout, body)
	return token, exitOK
}

func runCreate(args []string, stdout, stderr io.Writer) int {
	fs := newFlagSet("create", stderr)
	baseURL := fs.String("base-url", defaultBaseURL, "后端基础地址")
	timeout := fs.Duration("timeout", defaultTimeout, "请求超时")
	auth := addAuthFlags(fs)
	title := fs.String("title", "", "问题标题")
	description := fs.String("description", "", "问题描述")
	priority := fs.String("priority", "medium", "优先级")
	x := fs.Float64("x", 0, "X 坐标")
	y := fs.Float64("y", 0, "Y 坐标")
	z := fs.Float64("z", 0, "Z 坐标")

	if err := fs.Parse(args); err != nil {
		fmt.Fprintf(stderr, "create 参数解析失败：%v\n", err)
		return exitUsage
	}
	if fs.NArg() > 0 {
		fmt.Fprintf(stderr, "create 不接受多余参数：%v\n", fs.Args())
		return exitUsage
	}
	for _, coord := range []struct {
		name  string
		value float64
	}{{"x", *x}, {"y", *y}, {"z", *z}} {
		if math.IsNaN(coord.value) || math.IsInf(coord.value, 0) {
			fmt.Fprintf(stderr, "坐标 -%s 必须是有限数值（零、负数、小数均可），收到 %v\n", coord.name, coord.value)
			return exitUsage
		}
	}

	token, code := resolveToken(auth, *baseURL, *timeout, stdout, stderr)
	if code != exitOK {
		if code == exitUsage {
			fmt.Fprintln(stderr, "会话参数用法：-token 与 -username/-password 不得同时提供；账号登录需同时给用户名和密码")
		}
		return code
	}

	req := createRequest{
		Title:       *title,
		Description: *description,
		Priority:    *priority,
		Position:    position{X: *x, Y: *y, Z: *z},
	}
	body, cerr := createIssue(*baseURL, *timeout, token, req)
	return reportResult(body, cerr, stdout, stderr)
}

func runList(args []string, stdout, stderr io.Writer) int {
	fs := newFlagSet("list", stderr)
	baseURL := fs.String("base-url", defaultBaseURL, "后端基础地址")
	timeout := fs.Duration("timeout", defaultTimeout, "请求超时")
	auth := addAuthFlags(fs)

	if err := fs.Parse(args); err != nil {
		fmt.Fprintf(stderr, "list 参数解析失败：%v\n", err)
		return exitUsage
	}
	if fs.NArg() > 0 {
		fmt.Fprintf(stderr, "list 不接受多余参数：%v\n", fs.Args())
		return exitUsage
	}

	token, code := resolveToken(auth, *baseURL, *timeout, stdout, stderr)
	if code != exitOK {
		if code == exitUsage {
			fmt.Fprintln(stderr, "会话参数用法：-token 与 -username/-password 不得同时提供；账号登录需同时给用户名和密码")
		}
		return code
	}

	body, cerr := listIssues(*baseURL, *timeout, token)
	return reportResult(body, cerr, stdout, stderr)
}

// reportResult 统一输出：请求有响应体时完整展示（可读缩进），失败另附中文诊断到 stderr。
func reportResult(body []byte, cerr *exitCodeError, stdout, stderr io.Writer) int {
	if cerr != nil {
		fmt.Fprintf(stderr, "失败：%s\n", cerr.message)
		if len(body) > 0 {
			writeJSON(stdout, body)
		}
		return cerr.code
	}
	writeJSON(stdout, body)
	return exitOK
}
