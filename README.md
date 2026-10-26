# 现场巡检问题标注（Unity AR + Go + React）

园区巡检场景的小型原型：巡检员用手机在真实环境中放置 AR 标记并上报问题，管理人员在网页端查看并更新处理状态。任务书全文见 [docs/Unity-AR研发实操题-候选人任务书-简略版.md](docs/Unity-AR研发实操题-候选人任务书-简略版.md)。

## 使用的版本

| 部分 | 技术 | 版本 | 状态 |
| --- | --- | --- | --- |
| 移动端 | Unity 6000.3.27f1 LTS（修订 672d562a42a7）+ AR Foundation 6.6.2 + ARCore XR Plug-in 6.6.2 + XR Plug-in Management 4.4.0 + XR Core Utils 2.6.0 | 见 `unity/Packages/manifest.json` 与 `unity/Packages/packages-lock.json` | 编辑器与 Android 全套组件已安装；工程已初始化并通过门禁自检（24/25 项，编译零错误零警告；1 项 GUI 开关待手动勾选，见已知问题） |
| 管理端 | React 19.3.1 + TypeScript 5.9.3 + Vite 7.3.7 + Tailwind CSS 4.3.3 + oxlint 1.89.0（Node.js v24.11.0、pnpm 10.21.0） | 见 `web/package.json` 与 `web/pnpm-lock.yaml` | 骨架完成，typecheck/lint/build 与浏览器双视口验收通过；未接后端数据 |
| 后端 | Go 1.27.3 + Gin 1.12.0 + modernc.org/sqlite 1.61.0（纯 Go 驱动，SQLite 本地文件） | 见 `backend/go.mod` 与 `backend/go.sum` | 骨架完成，`GET /api/health` + SQLite 自动初始化 + 重启持久化验证通过 |
| 真机 | iQOO Z9（V2361A，Android 16） | ARCore 运行时 1.57.0，实测 SUPPORTED_INSTALLED | 已验证 |

## 如何启动

- **Go 后端**：`cd backend && go run ./cmd/server`。默认监听 `:8080`（`ADDR` 可改），数据库默认 `./data/inspection.db`（`ISSUE_DB_PATH` 可改），环境变量示例见 `backend/.env.example`。健康检查：`curl http://localhost:8080/api/health` → `{"data":{"status":"ok"}}`。真机联调时需监听 `0.0.0.0`（默认 `:8080` 即是）。
- **React 管理端**：`cd web && pnpm install && pnpm dev`，默认 `http://localhost:5173`（`VITE_API_BASE_URL` 见 `web/.env.example`，骨架阶段尚未请求后端）。类型检查/构建/静态检查：`pnpm typecheck`、`pnpm build`、`pnpm lint`。
- **Unity 移动端**：编辑器 6000.3.27f1 与 Android 全套组件已安装（Hub 路径 `/Applications/Unity/Hub/Editor/6000.3.27f1/`）。工程初始化已执行过一次（场景 `Assets/Scenes/Inspection.unity`、Android/ARCore 配置均已落地）；日常用 Unity Hub 打开 `unity/` 工程即可。如需重建配置，菜单「现场巡检 / 初始化工程（场景 + Android + ARCore 配置）」可重复执行（幂等）。
- **Unity 阶段门禁自检（SK-01/SK-02 自动验证）**：
  `/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.GateVerify.Run -logFile unity/Logs/gate-verify.log`
  （或在编辑器菜单「现场巡检 / 阶段门禁自检」查看），逐项输出 PASS/FAIL，任一项失败进程退出码非 0。最近一次结果：24/25 通过。

## Unity 真机如何配置 Go 后端地址

（待实现。骨架阶段仅定义了三端统一的 DTO：`unity/Assets/Scripts/API/IssueDtos.cs`。计划：提供运行时可配置的 BaseUrl，默认 `http://<电脑局域网IP>:8080`，构建时允许明文 HTTP 仅限开发构建；手机与电脑需同一 Wi-Fi。）

## 已完成 / 未完成 / 已知问题

**已完成**

- 真机 ARCore 可行性验证：iQOO Z9 实测 AR 会话创建、相机启动、运动跟踪运行全部成功，ARCore 官方判定 SUPPORTED_INSTALLED（证据：`tools/ar-support-check/results/`、`docs/evidence/`）
- 仓库结构搭建、任务书与验证材料归档
- Go 后端骨架：Gin 分层结构（cmd/internal：issue 存储 + httpapi）、SQLite WAL 自动建表、`GET /api/health`（存储不可用返回 503）、统一响应结构与 CORS 基础、优雅关闭；测试与构建门禁通过
- React 管理端骨架：静态管理台外壳（侧栏/顶栏/卡片/表格）、三端一致类型与 API 解析基础、Tailwind CSS v4 工具类实现；typecheck/lint/build 与桌面+窄屏浏览器验收通过
- Unity 工程骨架：编辑器 6000.3.27f1 + Android 全套组件已安装；工程初始化完成（`Inspection.unity` 主场景含 AR 会话/AR 相机/仅水平平面检测/射线/锚点管理器/Canvas/EventSystem，Android IL2CPP+ARM64+API25+Both 输入，ARCore Loader 已启用，自定义 Main Manifest 文件含 INTERNET 权限与 ARCore 包可见性声明）；批处理门禁自检 24/25 项通过、编译零错误零警告

**未完成**

- 「Custom Main Manifest」开关的手动勾选（Unity 6000.3 无程序化 API，见已知问题）
- Unity AR 移动端业务（平面检测、点击放置标记、表单上报、UI 防误触）
- Go 后端业务接口（创建、列表、状态修改、参数校验）
- React 管理端业务（真实数据加载、刷新、状态修改、加载/错误反馈）
- Android 开发构建与真机部署（属后续计划）
- 三端联调与演示视频

**已知问题**

- **「Custom Main Manifest」开关需手动勾选一次**：Unity 6000.3 对该开关无任何程序化 API（含 internal），脚本直写会被编辑器内部状态还原。请打开工程后到 Player Settings → Android → Publishing Settings 勾选「Custom Main Manifest」；`Assets/Plugins/Android/AndroidManifest.xml` 文件已就位（含 INTERNET 权限与 `com.google.ar.core` 查询声明），若勾选时 Unity 以模板覆盖该文件，重新执行初始化菜单或告知助手恢复即可。
- **Unity 下载的地域限制（已解决，留档）**：Unity CDN（Akamai）按出口 IP 判定大中华区并重定向到 download.unitychina.cn（对 Unity 6 macOS 包一律 404）；本机机场的香港/日本节点 IP 段（ASN 134972）同样命中该判定，切换到「美国A01」节点后正常。后续下载 Unity 相关资源需保持美国/非大中华区出口节点。
- **最低 API 25（计划偏差）**：计划要求 API 24，Unity 6000.3 引擎最低仅支持 API 25（Android 7.1），已在计划文档施工日志记录；对目标真机（Android 16）无影响。

## 佐证材料

- [docs/真机ARCore可行性验证报告.md](docs/真机ARCore可行性验证报告.md) — 本轮设备验证的完整记录（结论、排查过程、UNKNOWN_ERROR 根因、复现方式）
- `docs/evidence/` — Play 商店"不兼容"提示与 vivo 商店页面对比截图（背景说明：Play 分发目录不含国行型号，与运行时支持无关，详见 AI_LOG 第 7 条）
- `tools/ar-support-check/` — ARCore 支持检测工具源码与三张真机输出截图
