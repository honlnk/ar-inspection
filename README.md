# 现场巡检问题标注（Unity AR + Go + React）

园区巡检场景的小型原型：巡检员用手机在真实环境中放置 AR 标记并上报问题，管理人员在网页端查看并更新处理状态。任务书全文见 [docs/Unity-AR研发实操题-候选人任务书-简略版.md](docs/Unity-AR研发实操题-候选人任务书-简略版.md)。

## 使用的版本

| 部分 | 技术 | 版本 | 状态 |
| --- | --- | --- | --- |
| 移动端 | Unity 6000.3.27f1 LTS（修订 672d562a42a7）+ AR Foundation 6.6.2 + ARCore XR Plug-in 6.6.2 + XR Plug-in Management 4.4.0 + XR Core Utils 2.6.0 | 见 `unity/Packages/manifest.json` 与 `unity/Packages/packages-lock.json` | 编辑器与 Android 全套组件已安装；工程已初始化并实际打开，编译及自检通过；review 发现的 ARCore Loader 重复已修复（初始化幂等），Android 构建待验证 |
| 管理端 | React 19.3.1 + TypeScript 7.0.2 + Vite 8.3.5 + Tailwind CSS 4.3.3 + oxlint 1.89.0（Node.js v24.11.0、pnpm 10.21.0） | 见 `web/package.json` 与 `web/pnpm-lock.yaml` | 骨架完成，typecheck/lint/build 与浏览器双视口验收通过；未接后端数据。注：工具链由计划锁定的 Vite 7 / TS 5 经用户拍板升级至最新稳定版（2026-10-27，计划文档施工日志留档） |
| 后端 | Go 1.27.3 + Gin 1.12.0 + modernc.org/sqlite 1.61.0（纯 Go 驱动，SQLite 本地文件） | 见 `backend/go.mod` 与 `backend/go.sum` | 骨架已建立，常规测试、构建、SQLite 初始化及同库重启验证通过；健康检查为真实读库检查，文件损坏场景已有测试覆盖 |
| 真机 | iQOO Z9（V2361A，Android 16） | ARCore 运行时 1.57.0，实测 SUPPORTED_INSTALLED | 已验证 |

## 如何启动

- **Go 后端**：`cd backend && go run ./cmd/server`。默认监听 `:8080`（`ADDR` 可改），数据库默认 `./data/inspection.db`（`ISSUE_DB_PATH` 可改），环境变量示例见 `backend/.env.example`。健康检查：`curl http://localhost:8080/api/health` → `{"data":{"status":"ok"}}`。真机联调时需监听 `0.0.0.0`（默认 `:8080` 即是）。
- **React 管理端**：`cd web && pnpm install && pnpm dev`，默认 `http://localhost:5173`（`VITE_API_BASE_URL` 见 `web/.env.example`，骨架阶段尚未请求后端）。类型检查/构建/静态检查：`pnpm typecheck`、`pnpm build`、`pnpm lint`。
- **Unity 移动端**：编辑器 6000.3.27f1 与 Android 全套组件已安装（Hub 路径 `/Applications/Unity/Hub/Editor/6000.3.27f1/`）。工程初始化已执行（场景 `Assets/Scenes/Inspection.unity`、Android/ARCore 配置已落地）；日常用 Unity Hub 打开 `unity/` 工程即可。初始化菜单「现场巡检 / 初始化工程（场景 + Android + ARCore 配置）」可重复执行（幂等）：已存在的配置不重复创建，重复的同类型 XR Loader 会被自动清理。
- **Unity 阶段门禁自检（SK-01/SK-02 自动验证）**：
  `/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.GateVerify.Run -logFile unity/Logs/gate-verify.log`
  （或在编辑器菜单「现场巡检 / 阶段门禁自检」查看），逐项输出 PASS/FAIL，任一项失败进程退出码非 0。最近一次结果：25/25 全部通过（2026-10-27 修复批后于工程副本批处理复验，含新增的 ARCore Loader 同类型重复检查）。自检不验证 Android 构建后的最终合并 Manifest，该结果不含此项。

## Unity 真机如何配置 Go 后端地址

（待实现。骨架阶段仅定义了三端统一的 DTO：`unity/Assets/Scripts/API/IssueDtos.cs`。计划：提供运行时可配置的 BaseUrl，默认 `http://<电脑局域网IP>:8080`，构建时允许明文 HTTP 仅限开发构建；手机与电脑需同一 Wi-Fi。）

## 已完成 / 未完成 / 已知问题

**已完成**

- 真机 ARCore 可行性验证：iQOO Z9 实测 AR 会话创建、相机启动、运动跟踪运行全部成功，ARCore 官方判定 SUPPORTED_INSTALLED（证据：`tools/ar-support-check/results/`、`docs/evidence/`）
- 仓库结构搭建、任务书与验证材料归档
- Go 后端骨架：Gin 分层结构（cmd/internal：issue 存储 + httpapi）、SQLite WAL 自动建表、`GET /api/health`、统一响应结构与 CORS 基础、优雅关闭；常规测试与构建门禁通过。健康检查为真实读库检查（现有连接 + 全新连接各读一次 SQLite 系统表），连接关闭、运行中文件损坏、重启后文件损坏三类故障均返回 503，有测试覆盖
- React 管理端骨架：静态管理台外壳（侧栏/顶栏/卡片/表格）、三端一致类型与 API 解析基础、Tailwind CSS v4 工具类实现；typecheck/lint/build 与桌面+窄屏浏览器验收通过
- Unity 工程骨架：编辑器 6000.3.27f1 + Android 全套组件已安装；工程初始化完成（`Inspection.unity` 主场景含 AR 会话/AR 相机/仅水平平面检测/射线/锚点管理器/Canvas/EventSystem，Android IL2CPP+ARM64+API25+Both 输入，ARCore Loader 已启用，自定义 Main Manifest 文件含 INTERNET 权限与 ARCore 包可见性声明）；编辑器实际打开无编译错误，门禁自检 25/25 项通过，初始化幂等已复验（重复 Loader 自动清理），Android 构建待验证。Custom Main Manifest 由标准路径下的 Manifest 文件启用，无需手动勾选

**未完成**

- Unity AR 移动端业务（平面检测、点击放置标记、表单上报、UI 防误触）
- Go 后端业务接口（创建、列表、状态修改、参数校验）
- React 管理端业务（真实数据加载、刷新、状态修改、加载/错误反馈）
- Android 开发构建、最终合并 Manifest 与真机部署
- 三端联调与演示视频

**已知问题**

- **Unity 下载的地域限制（已解决，留档）**：Unity CDN（Akamai）按出口 IP 判定大中华区并重定向到 download.unitychina.cn（对 Unity 6 macOS 包一律 404）；本机机场的香港/日本节点 IP 段（ASN 134972）同样命中该判定，切换到「美国A01」节点后正常。后续下载 Unity 相关资源需保持美国/非大中华区出口节点。
- **最低 API 25（计划偏差）**：计划要求 API 24，Unity 6000.3 引擎最低仅支持 API 25（Android 7.1），已在计划文档施工日志记录；对目标真机（Android 16）无影响。
- **SQLite 健康检查覆盖不足（已修复，留档）**：原实现仅探测数据库连接，文件内容损坏时仍可能误报正常。2026-10-27 修复为真实读库检查（现有连接 + 全新连接各读一次 SQLite 系统表），并补齐运行中损坏与重启后损坏的回归测试；WAL 完好时的主文件覆盖经查证不属可检出故障（数据仍由 WAL 提供），详见计划 001 第 7.3 节。
- **ARCore Loader 重复（已修复，留档）**：Android XR 配置曾包含两个独立的 ARCore Loader，初始化脚本重复执行会继续累积，自检当时无法发现。2026-10-27 修复：初始化按类型复用并自动清理重复项（幂等，副本批处理连跑两次复验），自检新增同类型重复检查项，现有配置已清理；重复配置对 Android 构建和运行的实际影响未实测（Android 构建待验证）。

## 佐证材料

- [docs/真机ARCore可行性验证报告.md](docs/真机ARCore可行性验证报告.md) — 本轮设备验证的完整记录（结论、排查过程、UNKNOWN_ERROR 根因、复现方式）
- `docs/evidence/` — Play 商店"不兼容"提示与 vivo 商店页面对比截图（背景说明：Play 分发目录不含国行型号，与运行时支持无关，详见 AI_LOG 第 7 条）
- `tools/ar-support-check/` — ARCore 支持检测工具源码与三张真机输出截图

## 复盘记录

- [docs/lessons/001-CustomMainManifest门禁误报.md](docs/lessons/001-CustomMainManifest门禁误报.md) — Custom Main Manifest 开关的真实机制、门禁误报根因与防再犯规则
