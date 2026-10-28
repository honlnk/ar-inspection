# 计划 006 · Docker 双容器验收记录（2026-10-28）

> 环境：Docker Desktop，服务端 `linux/arm64 29.1.3`，Compose `v5.0.0-desktop.1`（Apple Silicon 原生，无 x86 模拟）。
> 密码与会话密钥经未跟踪的 `compose.local.env` 注入，本记录中全部脱敏。

## 交付物

- `backend/Dockerfile`：golang:1.27-alpine 多阶段构建 → alpine:3.22 非 root（uid=10001 app）运行，CGO 关闭静态编译（modernc.org/sqlite 纯 Go）
- `web/Dockerfile`：node:24-alpine + corepack pnpm@10.21.0（与本机开发一致，锁文件冻结安装）构建，构建时 `VITE_API_BASE_URL=""` 走同源；nginx:1.29-alpine 提供静态页
- `web/docker/nginx.conf`：`/api` 同源反向代理（含 `/api/ws` WebSocket 升级、3600s 读写超时），SPA 回退 `index.html`
- `compose.yaml`：backend（8080 直连给真机 Unity）+ web（5173→80）+ 命名卷 `inspection-data`，健康检查真实调用 `GET /api/health`
- `backend/.dockerignore`、`web/.dockerignore`：排除本机依赖、数据库、密钥
- `compose.local.env.example`：占位符示例（真实文件 `compose.local.env` 不入库，`.gitignore` 已覆盖 `*.local.env`）

## 验收过程与结果

### 1. 构建

```
docker compose build
→ Image ar-inspection-web:006 Built
→ Image ar-inspection-backend:006 Built        （均为 arm64 原生）
```

### 2. 全新卷首启（验收前确认 `ar-inspection_inspection-data` 卷不存在）

```
docker compose up -d --wait
→ ar-inspection-backend-1 Healthy
→ ar-inspection-web-1      Healthy
```

### 3. 健康与业务链路（全部经 5173 同源代理）

```
curl http://localhost:8080/api/health   → {"data":{"status":"ok"}}
curl http://localhost:5173/api/health   → {"data":{"status":"ok"}}          # nginx 代理生效

POST /api/auth/login（inspector，密码取自 compose.local.env）
→ Set-Cookie: inspection_session=[REDACTED: 64 位会话令牌]；HttpOnly

POST /api/issues（Authorization: Bearer [REDACTED]）
→ 201 {"data":{"id":"9ef9e4b4-…","title":"Docker 卷持久化验证","priority":"high","status":"open",
       "createdAt":"2026-10-28T07:43:55+08:00", …}}                          # 后端生成 id / 默认 open / 北京时间

curl http://localhost:5173/              → SPA index.html 正常返回
```

### 4. 停止 → 重启持久化复核

```
docker compose stop && docker compose up -d --wait   （两容器再次 Healthy）
GET /api/issues（inspector Bearer）→ 1 条，即首启创建的「Docker 卷持久化验证」，时间戳一致
```

### 5. 容器内落盘与权限

```
docker compose exec backend sh -c 'id && ls -l /data'
→ uid=10001(app) gid=10001(app)                                            # 非 root 运行
→ /data/inspection.db(-shm/-wal) 属主 app:app                              # SQLite 写入命名卷
```

### 6. 局域网访问（真机 Unity 直连口径）

```
电脑局域网 IP：192.168.31.17
curl http://192.168.31.17:8080/api/health → {"data":{"status":"ok"}}
curl http://192.168.31.17:5173/           → 200
```

### 7. WebSocket 经代理升级（admin 会话）

```
curl -i --http1.1 http://localhost:5173/api/ws  （携带 admin Cookie 与 Upgrade 头）
→ HTTP/1.1 101 Switching Protocols / Server: nginx/1.29.8 / Connection: upgrade
```

## 结论

阶段 4 验收标准全部满足：arm64 双镜像一键构建启动、健康检查真实调用既有接口、同源代理覆盖 REST 与 WS、全新卷初始化与重启持久化、非 root 运行、局域网直连口径与本机跑 Go 一致。启动命令不会自动删卷；清理需显式 `docker compose down -v`。
