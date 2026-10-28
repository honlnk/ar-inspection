// 现场巡检问题标注后端入口：读取配置、打开存储、注册路由并优雅关闭。
package main

import (
	"context"
	"errors"
	"log"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/gin-gonic/gin"

	"ar-inspection/backend/internal/auth"
	"ar-inspection/backend/internal/httpapi"
	"ar-inspection/backend/internal/issue"
)

func main() {
	if err := run(); err != nil {
		log.Fatalf("服务启动失败: %v", err)
	}
}

func run() error {
	addr := envOr("ADDR", ":8080")
	dbPath := envOr("ISSUE_DB_PATH", "./data/inspection.db")
	corsOrigins := envOr("CORS_ORIGINS", "http://localhost:5173,http://127.0.0.1:5173")

	// 计划 006：演示账号密码与会话密钥只从环境注入；缺失或过短直接失败，
	// 不为开发环境设置可猜测的默认值，也不为本地演示关闭鉴权。
	inspectorPassword := os.Getenv("INSPECTOR_PASSWORD")
	adminPassword := os.Getenv("ADMIN_PASSWORD")
	sessionSecret := os.Getenv("SESSION_SECRET")
	if strings.TrimSpace(inspectorPassword) == "" || strings.TrimSpace(adminPassword) == "" {
		return errors.New("缺少演示账号密码环境变量：请设置 INSPECTOR_PASSWORD 与 ADMIN_PASSWORD（参见 backend/.env.example）")
	}
	if len(strings.TrimSpace(sessionSecret)) < 32 {
		return errors.New("缺少或过短的会话密钥：请设置 SESSION_SECRET（至少 32 个字符的随机值，参见 backend/.env.example）")
	}

	store, err := issue.OpenSQLite(dbPath)
	if err != nil {
		return err
	}
	defer store.Close()

	// 启动时完成建表和轻量检查；失败则明确报错退出，不降级为内存存储。
	initCtx, cancelInit := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancelInit()
	if err := store.Init(initCtx); err != nil {
		return err
	}

	// 账号/会话存储与问题库同文件；建表依赖 issues 表，须在问题库初始化之后。
	authStore, err := auth.OpenSQLite(dbPath)
	if err != nil {
		return err
	}
	defer authStore.Close()
	if err := authStore.Init(initCtx); err != nil {
		return err
	}
	authService := auth.NewService(authStore, []byte(sessionSecret))
	if err := authService.EnsureAccounts(initCtx, inspectorPassword, adminPassword); err != nil {
		return err
	}
	authService.CleanupExpiredSessions(context.Background())

	gin.SetMode(gin.ReleaseMode)
	// 开发快捷登录默认关闭：只有显式设置 QUICK_LOGIN_ENABLED=1 才注册路由，
	// 避免任何未配置的部署（含 Docker 演示）意外获得免密登录入口。
	quickLoginEnabled := envBool("QUICK_LOGIN_ENABLED")
	routerOpts := make([]httpapi.Option, 0, 1)
	if quickLoginEnabled {
		routerOpts = append(routerOpts, httpapi.WithQuickLogin(true))
		log.Printf("开发快捷登录已启用（POST /api/auth/quick-login）：仅限本地开发，请勿在生产环境开启")
	}
	router := httpapi.NewRouter(store, authService, splitOrigins(corsOrigins), issue.DefaultFieldGenerator(), routerOpts...)
	server := &http.Server{
		Addr:              addr,
		Handler:           router,
		ReadHeaderTimeout: 5 * time.Second,
	}

	serverErr := make(chan error, 1)
	go func() {
		log.Printf("巡检后端已启动：监听 %s，数据库 %s", addr, dbPath)
		if err := server.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			serverErr <- err
		}
	}()

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt, syscall.SIGTERM)

	select {
	case err := <-serverErr:
		return err
	case <-stop:
		log.Println("收到退出信号，正在优雅关闭……")
		shutdownCtx, cancelShutdown := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancelShutdown()
		if err := server.Shutdown(shutdownCtx); err != nil {
			return err
		}
		log.Println("服务已停止")
		return nil
	}
}

func envOr(key, fallback string) string {
	if v := strings.TrimSpace(os.Getenv(key)); v != "" {
		return v
	}
	return fallback
}

// envBool 解析开发开关：仅明确的真值字符串视为开启，其余（含未设置）一律关闭。
func envBool(key string) bool {
	switch strings.ToLower(strings.TrimSpace(os.Getenv(key))) {
	case "1", "true", "yes", "on":
		return true
	default:
		return false
	}
}

func splitOrigins(v string) []string {
	parts := strings.Split(v, ",")
	origins := make([]string, 0, len(parts))
	for _, p := range parts {
		if p = strings.TrimSpace(p); p != "" {
			origins = append(origins, p)
		}
	}
	return origins
}
