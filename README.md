# 现场巡检问题标注（Unity AR + Go + React）

园区巡检场景的小型原型：巡检员用手机在真实环境中放置 AR 标记并上报问题，管理人员在网页端查看并更新处理状态。任务书全文见 [docs/Unity-AR研发实操题-候选人任务书-简略版.md](docs/Unity-AR研发实操题-候选人任务书-简略版.md)。

## 使用的版本

| 部分 | 技术 | 版本 | 状态 |
| --- | --- | --- | --- |
| 移动端 | Unity 6000.3.27f1 LTS（修订 672d562a42a7）+ AR Foundation 6.6.2 + ARCore XR Plug-in 6.6.2 + XR Plug-in Management 4.4.0 + XR Core Utils 2.6.0 + Test Framework 1.5.1 | 见 `unity/Packages/manifest.json` 与 `unity/Packages/packages-lock.json` | 真机业务链路已通（2026-10-28 计划 004）：Android 开发构建、真机平面检测、点击放置标记、跟随按钮、表单上报与后端地址运行时配置真机验收，HTTP 上报已入库复核；存在一项已知问题「放置位置相对点击位置整体偏移」（见已知问题）。注：minSdk 由 25 提至 29（ARCore 硬性要求），图形 API 强制 OpenGL ES3 |
| 管理端 | React 19.3.1 + TypeScript 7.0.2 + Vite 8.3.5 + Tailwind CSS 4.3.3 + oxlint 1.89.0；测试：Vitest 5.0.5 + React Testing Library 16.3.3 + user-event 14.6.8 + jest-dom 7.0.1 + jsdom 30.1.3（Node.js v24.11.0、pnpm 10.21.0） | 见 `web/package.json` 与 `web/pnpm-lock.yaml` | 业务闭环完成：真实列表加载、手动刷新、状态修改保存与全部异常反馈（失败/超时/异常响应）已实现并与真实后端联调验收通过（详见下方管理端说明）。注：工具链由计划锁定的 Vite 7 / TS 5 经用户拍板升级至最新稳定版（2026-10-27，计划文档施工日志留档） |
| 后端 | Go 1.27.3 + Gin 1.12.0 + modernc.org/sqlite 1.61.0（纯 Go 驱动，SQLite 本地文件） | 见 `backend/go.mod` 与 `backend/go.sum` | 业务闭环完成：健康检查、创建、列表、状态修改四接口与参数校验、事务持久化、重启数据保留均已实现并通过真实进程验收（详见下方接口说明） |
| 真机 | iQOO Z9（V2361A，Android 16） | ARCore 运行时 1.57.0，实测 SUPPORTED_INSTALLED | 已验证 |

## 如何启动

- **Go 后端**：`cd backend && go run ./cmd/server`。默认监听 `:8080`（`ADDR` 可改），数据库默认 `./data/inspection.db`（`ISSUE_DB_PATH` 可改），跨域白名单默认 `http://localhost:5173,http://127.0.0.1:5173`（`CORS_ORIGINS` 可改），环境变量示例见 `backend/.env.example`。健康检查：`curl http://localhost:8080/api/health` → `{"data":{"status":"ok"}}`。真机联调时需监听 `0.0.0.0`（默认 `:8080` 即是）。测试与静态检查：`go test ./...`、`go test -race ./...`、`go vet ./...`。
- **React 管理端**：`cd web && pnpm install && pnpm dev`，默认 `http://localhost:5173`（`VITE_API_BASE_URL` 见 `web/.env.example`，默认请求 `http://localhost:8080`；启动后即从后端加载问题列表）。类型检查/静态检查/测试/构建：`pnpm typecheck`、`pnpm lint`、`pnpm test`、`pnpm build`。
- **Unity 上报模拟工具（开发辅助）**：独立 Go 命令行工具 `tools/issue-reporter/`，代替 Unity 客户端以真实 HTTP 上报/查询问题，用于接口联调与回归（坐标为人工输入的模拟坐标，不代表真实 AR 位置）：

  ```bash
  cd tools/issue-reporter
  go run . create -base-url http://localhost:8080 -title "模拟上报：入口墙面破损" -description "非 AR 工具创建" -priority high -x 0.42 -y 0.03 -z 1.26
  go run . list -base-url http://localhost:8080
  ```

  详见 [tools/issue-reporter/README.md](tools/issue-reporter/README.md)（参数、输出与退出码）。测试：`cd tools/issue-reporter && go test ./...`。
- **Unity 移动端**：编辑器 6000.3.27f1 与 Android 全套组件已安装（Hub 路径 `/Applications/Unity/Hub/Editor/6000.3.27f1/`）。工程初始化已执行（场景 `Assets/Scenes/Inspection.unity`、Android/ARCore 配置已落地）；日常用 Unity Hub 打开 `unity/` 工程即可。初始化菜单「现场巡检 / 初始化工程（场景 + Android + ARCore 配置）」可重复执行（幂等）：已存在的配置不重复创建，重复的同类型 XR Loader 会被自动清理。
- **Unity 构建与真机安装**（开发构建，IL2CPP + ARM64 + OpenGL ES3，产物不入 Git）：

  ```bash
  # 场景内容搭建（Prefab/材质/UI/脚本接线，幂等；改动 SceneWiring.cs 后重跑）
  /Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.SceneWiring.Run -logFile unity/Logs/scene-wiring.log
  # 构建 APK（编辑器菜单「现场巡检 / 构建 Android 开发包」等价）
  /Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.BuildAndroid.Run -logFile unity/Logs/build-android.log
  adb install -r unity/Builds/inspection-dev.apk
  ```

  EditMode 测试：`Unity -batchmode -projectPath unity -runTests -testPlatform EditMode -testResults <路径>/results.xml`（当前 26/26）。真机要求 ARCore 设备与相机授权（可 `adb shell pm grant com.honlnk.arinspection android.permission.CAMERA` 预授权）；开发构建左上角有黄色调试信息条（实时显示会话/平面/放置诊断，仅开发构建存在）。
- **Unity 放置诊断记录（仅开发构建）**：每次成功放置后，等待该帧渲染结束，保存同名的 PNG 截图与 JSON 数据。数据包含屏幕尺寸与点击位置、射线命中位置、全部已检平面的边界世界/屏幕坐标、黄色方块与点击位置的屏幕偏差、锚点、相机姿态/投影/内参与会话状态。屏幕坐标以左下角为原点，单位为像素；偏移的 Y 为负表示方块位于触点下方。记录位于应用私有目录，不自动上传、不自动删除，会占用手机存储；截图包含现场实景，导出和分享前需确认环境可披露。连接手机后可导出到电脑的临时目录：

  ```bash
  mkdir -p /tmp/ar-placement-diagnostics
  adb pull /sdcard/Android/data/com.honlnk.arinspection/files/PlacementDiagnostics /tmp/ar-placement-diagnostics/
  ```

  诊断记录位于 Android 应用外部文件目录，可直接导出，不使用跨应用私有目录的复制命令。原始截图和 AR 数据仍保留在手机中，上述命令不会清理设备文件。
- **Unity 阶段门禁自检（SK-01/SK-02 自动验证）**：
  `/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.GateVerify.Run -logFile unity/Logs/gate-verify.log`
  （或在编辑器菜单「现场巡检 / 阶段门禁自检」查看），逐项输出 PASS/FAIL，任一项失败进程退出码非 0。最近一次结果：35/35 全部通过（2026-10-28 放置诊断增量复验，含诊断记录器接线、运行时接线、minSdk 29、Activity 入口与竖屏锁定检查）。自检不验证 Android 构建后的最终合并 Manifest，该结果不含此项。

## Unity 真机如何配置 Go 后端地址

App 右上角「设置」打开配置面板，填入电脑上运行后端服务的局域网地址（如 `http://192.168.31.17:8080`），点「保存」立即生效并持久保存（PlayerPrefs，重启 App 保留）；顶部栏左侧实时显示当前地址。首次启动会自动弹出一次设置面板引导配置。要求手机与电脑同一 Wi-Fi，后端监听 `0.0.0.0`（默认 `:8080` 即是）。

- 开发构建通过 AAR 插件注入 `networkSecurityConfig` 放行明文 HTTP（仅限局域网联调，不含任何敏感信息）；顶部栏「手电」可在暗光环境补光辅助平面检测，「重置」可在跟踪异常（如点击落点明显异常）时一键重建 AR 地图。
- 真机联调可用独立实例避免污染日常数据：`ADDR=0.0.0.0:18080 ISSUE_DB_PATH=~/.ar004-tmp/inspection.db go run ./cmd/server`（2026-10-28 真机上报验收即用此实例）。
- 上报超时 10 秒；后端不可达/超时/异常响应均有中文提示，表单与标记保留可原样重试。

## 后端接口说明

基础路径 `/api`，请求与响应均为 UTF-8 JSON。成功响应为 `{ "data": … }`（列表的 `data` 恒为数组，空列表返回 `[]`）；错误响应为 `{ "error": { "code": …, "message": … } }`，`message` 为中文提示。创建与状态修改请求拒绝未知字段和对象后的额外 JSON 内容，请求体上限 1 MiB。

| 方法与路径 | 用途 | 成功响应 |
| --- | --- | --- |
| `GET /api/health` | 服务与存储可用性检查 | `200`，`data` 为 `{ "status": "ok" }`；存储不可用返回 `503 service_unavailable` |
| `GET /api/issues` | 获取问题列表（按创建时间从新到旧，同时间按 ID 稳定排序，不分页） | `200`，`data` 为问题数组 |
| `POST /api/issues` | 创建问题（Unity 上报） | `201`，`data` 为完整新问题 |
| `PATCH /api/issues/{id}/status` | 按问题 ID 修改处理状态 | `200`，`data` 为更新后的完整问题 |

创建请求体示例（`description` 可省略，省略时保存为空字符串；`title` 必填且去除首尾空白后不能为空；`priority` 精确匹配 `low/medium/high`；坐标必须为有限数值，零和负数合法；ID、状态与时间由后端生成，客户端传入会被当作未知字段拒绝）：

```json
{
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 }
}
```

状态修改请求体仅为 `{ "status": "open" }`（三种合法状态可任意转换，重复设置同一状态也算一次合法更新并刷新 `updatedAt`）。问题字段三端一致：`id / title / description / priority / status / position{x,y,z} / createdAt / updatedAt`，时间为带 `+08:00` 偏移的 RFC 3339 字符串。

| HTTP 状态 | 错误码 | 典型情况 |
| --- | --- | --- |
| `400` | `invalid_argument` | JSON 无法解析、额外 JSON 内容、请求体超过 1 MiB、字段缺失或类型错误、未知字段、空标题、非法枚举、无效坐标 |
| `404` | `issue_not_found` | 状态修改对应的问题 ID 不存在 |
| `404` | `not_found` | API 路径不存在 |
| `405` | `method_not_allowed` | 已知路径使用未开放的方法 |
| `500` | `internal_error` | ID/时间生成失败、存储读写失败等内部问题（详细信息仅写服务端日志） |
| `503` | `service_unavailable` | 健康检查发现存储不可用 |

快速验证（后端启动后）：

```bash
curl http://localhost:8080/api/health
curl -X POST http://localhost:8080/api/issues -H 'Content-Type: application/json' \
  -d '{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}'
curl http://localhost:8080/api/issues
curl -X PATCH http://localhost:8080/api/issues/<创建返回的id>/status -H 'Content-Type: application/json' -d '{"status":"in_progress"}'
```

重启 `go run ./cmd/server` 后再次 `GET /api/issues`，问题与修改后的状态仍然存在。

## 已完成 / 未完成 / 已知问题

**已完成**

- 真机 ARCore 可行性验证：iQOO Z9 实测 AR 会话创建、相机启动、运动跟踪运行全部成功，ARCore 官方判定 SUPPORTED_INSTALLED（证据：`tools/ar-support-check/results/`、`docs/evidence/`）
- 仓库结构搭建、任务书与验证材料归档
- Go 后端骨架：Gin 分层结构（cmd/internal：issue 存储 + httpapi）、SQLite WAL 自动建表、`GET /api/health`、统一响应结构与 CORS 基础、优雅关闭；常规测试与构建门禁通过。健康检查为真实读库检查（现有连接 + 全新连接各读一次 SQLite 系统表），连接关闭、运行中文件损坏、重启后文件损坏三类故障均返回 503，有测试覆盖
- Go 后端业务闭环（2026-10-27 计划 002）：`POST /api/issues` 创建、`GET /api/issues` 列表、`PATCH /api/issues/{id}/status` 状态修改三接口上线——严格 JSON 校验（未知字段/额外内容/1 MiB 上限/中文错误）、后端生成 UUID v4 与北京时间、事务化 SQLite 读写、`created_at DESC, id ASC` 稳定排序、空列表 `[]`；模型/存储/HTTP 三层测试与 `-race` 全过，真实进程「创建→列表→改状态→停止→重启→再查询」验收通过（详见 `docs/plan/002` 施工日志）
- React 管理端业务闭环（2026-10-27 计划 003）：真实问题列表首次加载与手动刷新（刷新保留未保存草稿、失败保留旧数据）、行内状态修改与保存（只发送 `{status}`、以服务端完整结果替换、失败保留原状态与草稿可原行重试）、单笔保存与列表请求互斥、10 秒超时、统一信封与运行时结构检查、北京时间展示、全部失败场景可见提示与重试入口（不白屏不伪装）；42 个组件与请求层测试全过，并与真实 Go 后端 + 模拟工具完成「工具创建 → React 刷新 → React 改状态 → 工具复核 → 后端重启 → React 复核」联合验收（桌面与 375px 窄屏，证据 `docs/evidence/003-react-bug-management/`）
- Unity HTTP 上报模拟工具 `tools/issue-reporter/`（2026-10-27 计划 003）：独立 Go Module（仅标准库）create/list 子命令、完整 JSON 输出、10 秒默认超时、0/1/2 退出码、创建与列表契约检查、失败不自动重试；22 个测试（含 `-race`）全过。定位为开发辅助与回归工具，不是正式产品端、不验证 AR 能力，后续 Unity 真机完成前用于隔离接口问题
- Unity 工程骨架：编辑器 6000.3.27f1 + Android 全套组件已安装；工程初始化完成（`Inspection.unity` 主场景含 AR 会话/AR 相机/仅水平平面检测/射线/锚点管理器/Canvas/EventSystem，Android IL2CPP+ARM64+Both 输入，ARCore Loader 已启用，自定义 Main Manifest 文件含 INTERNET 权限与 ARCore 包可见性声明）；编辑器实际打开无编译错误，初始化幂等已复验（重复 Loader 自动清理）。Custom Main Manifest 由标准路径下的 Manifest 文件启用，无需手动勾选
- Unity 真机 AR 放置与上报业务（2026-10-28 计划 004）：Android 开发构建链路（命令行/菜单一键构建，合并 Manifest 含 INTERNET/CAMERA/ARCore queries/networkSecurityConfig）；真机平面检测与可视化（半透明色块 + 边界描线，手电补光按钮）；点击平面放置黄色标记（平面附锚，单草稿语义，距相机 >2m 或相机后方的伪命中自动拒绝，顶部「重置」一键重建地图）；「上报此标记」跟随按钮（投影跟随、出屏隐藏）；移动表单（标题必填/描述/优先级下拉/只读坐标）与全屏蒙层防误触；HTTP 上报（10 秒超时、六类响应分支中文提示）；后端地址运行时配置（首启引导一次、PlayerPrefs 持久）。EditMode 测试 26/26、门禁自检 35/35；真机完整链路验收通过——后端实测收到真机上报 `{"title":"111","position":{"x":-0.039,"y":0.303,"z":1.52}}` 全字段一致（AC-03 ✅，证据 `docs/evidence/004-unity-ar-place-report/`）
- Unity 成功放置诊断记录（开发构建）：同名截图与结构化数据已接入，具体字段和导出方式见上文；构建和编辑器测试通过，五组真机记录已读取并完成双会话交叉验证，结论见复盘 002。

**未完成**

- 完整三端联调（Unity 真机上报 → React 刷新可见 → 改状态 → 复核 → 重启数据保留）与演示视频（下一计划 005）
- 已知问题「放置位置偏移」的修复（见下）

**已知问题**

- **Unity 放置位置偏移（根因已定位，待修复复验，2026-10-28 更新）**：真机上 AR 虚拟层相对相机实景错位、标记落在触点下方或屏幕外。五组诊断证实 ARCore 会话与平面检测持续工作，但 Unity 渲染相机位置与旋转始终不变；场景使用旧版空间跟踪驱动且缺少 AR 输入管理器，手机姿态未进入渲染相机。已排除射线映射、投影矩阵、锚点额外偏移及 Vulkan 单一根因。修复方向是补齐 AR 输入管理器、改用 Input System 姿态驱动并同步初始化工具，不做坐标补偿；尚待实施和真机复验。完整证据与验收标准见 [复盘 002](docs/lessons/002-AR虚拟内容偏移与相机位姿冻结.md)，早期现象截图见 `docs/evidence/004-unity-ar-place-report/`。
- **真机长时间运行发热降频（已缓解）**：持续扫描 + 手电常亮会使机身升温触发 Thermal Status Severe（实测电池 40.3°C），ARCore 识别随之变慢；已将 App 全局限帧 30fps（`AppFrameRate`）并建议短测多轮。iQOO Z9（骁龙 7 Gen 3）算力本身满足 ARCore 要求（SUPPORTED_INSTALLED），非算力瓶颈。
- **真机切后台/息屏后相机失效（已定位，规避法有效）**：App 切后台再恢复后相机帧持续报 "The hardware buffer handle is null"、触摸无响应；`force-stop` 后重启即恢复。未做深度修复（涉及 ARCore 会话恢复生命周期），演示时注意保持亮屏。
- **Unity 下载的地域限制（已解决，留档）**：Unity CDN（Akamai）按出口 IP 判定大中华区并重定向到 download.unitychina.cn（对 Unity 6 macOS 包一律 404）；本机机场的香港/日本节点 IP 段（ASN 134972）同样命中该判定，切换到「美国A01」节点后正常。后续下载 Unity 相关资源需保持美国/非大中华区出口节点。
- **最低 SDK 版本（计划偏差，累计两层）**：计划要求 API 24，Unity 6000.3 引擎最低仅支持 API 25；ARCore + Vulkan 组合硬性要求 API ≥ 29，故 minSdk 落在 29（ProjectSettings/ProjectSetup/GateVerify 三处同步）。对目标真机（Android 16）无影响。
- **SQLite 健康检查覆盖不足（已修复，留档）**：原实现仅探测数据库连接，文件内容损坏时仍可能误报正常。2026-10-27 修复为真实读库检查（现有连接 + 全新连接各读一次 SQLite 系统表），并补齐运行中损坏与重启后损坏的回归测试；WAL 完好时的主文件覆盖经查证不属可检出故障（数据仍由 WAL 提供），详见计划 001 第 7.3 节。
- **ARCore Loader 重复（已修复，留档）**：Android XR 配置曾包含两个独立的 ARCore Loader，初始化脚本重复执行会继续累积，自检当时无法发现。2026-10-27 修复：初始化按类型复用并自动清理重复项（幂等），自检新增同类型重复检查项，现有配置已清理。

## 佐证材料

- [docs/真机ARCore可行性验证报告.md](docs/真机ARCore可行性验证报告.md) — 本轮设备验证的完整记录（结论、排查过程、UNKNOWN_ERROR 根因、复现方式）
- `docs/evidence/` — Play 商店"不兼容"提示与 vivo 商店页面对比截图（背景说明：Play 分发目录不含国行型号，与运行时支持无关，详见 AI_LOG 第 7 条）；`docs/evidence/003-react-bug-management/` — 计划 003 浏览器联合验收截图（桌面与 375px 窄屏正常列表、窄屏刷新失败提示）；`docs/evidence/004-unity-ar-place-report/` — 计划 004 真机验收截图（平面检测、放置标记与跟随按钮、点击位置偏移问题留证）
- `tools/ar-support-check/` — ARCore 支持检测工具源码与三张真机输出截图

## 复盘记录

- [docs/lessons/001-CustomMainManifest门禁误报.md](docs/lessons/001-CustomMainManifest门禁误报.md) — Custom Main Manifest 开关的真实机制、门禁误报根因与防再犯规则
- [docs/lessons/002-AR虚拟内容偏移与相机位姿冻结.md](docs/lessons/002-AR虚拟内容偏移与相机位姿冻结.md) — AR 平面与标记偏移的真机证据、相机输入断链根因、已排除误判与修复验收方向
