# 现场巡检问题标注 · React 管理端

园区巡检问题的网页管理端（React 19 + TypeScript 5 + Vite 7 + Tailwind CSS 4），使用 pnpm 管理依赖。

当前处于骨架阶段：仅包含静态管理台外壳、统一类型与 API 解析基础，尚不请求后端数据。

## 常用命令

```bash
pnpm install --frozen-lockfile   # 按锁文件安装依赖
pnpm dev                         # 启动开发服务器（默认 http://localhost:5173）
pnpm typecheck                   # TypeScript 类型检查
pnpm lint                        # oxlint 检查（Vite 模板默认配置）
pnpm build                       # 生产构建
```

## 环境变量

参考 `.env.example`，复制为 `.env.local` 配置后端地址：

- `VITE_API_BASE_URL`：后端 API 基础地址，默认 `http://localhost:8080`。
