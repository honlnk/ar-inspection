# 本地测试账号

本仓库随计划 006 起需要登录使用。以下为本机当前 `backend/.env` 与 `compose.local.env` 中配置的真实测试账号（两文件已核对一致），供后续联调与验收直接取用。

| 账号 | 用户名 | 密码 | 用途 |
| --- | --- | --- | --- |
| 巡检员 | `inspector` | `VFCUX38hJSaV` | Unity 巡检端登录、上报问题 |
| 管理员 | `admin` | `5mgTCm6UkC5p` | 网页管理端登录、处理问题、WebSocket 实时看板 |

说明：

- 会话有效期 12 小时；Unity/命令行工具登录后令牌自动保持，无需反复输入。
- 密码只存在于上述两个本地环境文件中（均被 `.gitignore` 忽略，不入库）；后端启动时从环境变量读取。
- 若密码失效（如环境文件被改动），以 `backend/.env` / `compose.local.env` 中的当前值为准，或参照 `backend/.env.example` / `compose.local.env.example` 重新生成。
- 本文件属本地测试凭证，密码为随机生成的演示值，不用于任何真实环境。
