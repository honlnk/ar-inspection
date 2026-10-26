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

	gin.SetMode(gin.ReleaseMode)
	router := httpapi.NewRouter(store, splitOrigins(corsOrigins))
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
