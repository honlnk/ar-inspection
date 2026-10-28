# 现场巡检问题标注（Unity AR + Go + React）

园区巡检场景的小型原型：巡检员用手机在真实环境中放置 AR 标记并上报问题，管理人员在网页端查看并更新处理状态。任务书全文见 [docs/Unity-AR研发实操题-候选人任务书-简略版.md](docs/Unity-AR研发实操题-候选人任务书-简略版.md)。

## 使用的版本

| 部分 | 技术 | 版本 | 状态 |
| --- | --- | --- | --- |
| 移动端 | Unity 6000.3.27f1 LTS（修订 672d562a42a7）+ AR Foundation 6.6.2 + ARCore XR Plug-in 6.6.2 + XR Plug-in Management 4.4.0 + XR Core Utils 2.6.0 + Test Framework 1.5.1 | 见 `unity/Packages/manifest.json` 与 `unity/Packages/packages-lock.json` | 真机业务链路已通（2026-10-28 计划 004）：Android 开发构建、真机平面检测、点击放置标记、跟随按钮、表单上报与后端地址运行时配置真机验收，HTTP 上报已入库复核；计划 005 修复相机姿态后放置偏移已真机复验消除；计划 006 新增登录会话、优先级三色标记、只读历史列表与完整诊断开关；计划 007 界面美化与系统返回已完成：Unity 界面排版、Android 系统返回、上报表单白屏、卡片/控件圆角、诊断开关关闭态与 2 米超距提示分级；真机补测三色标记、上报入库、历史刷新与诊断开关即时生效，并修复调试条恢复与返回键丢输入两个缺陷；历史改为按登录巡检员隔离（服务端过滤 + 换账号清空列表），两端登录页新增仅开发态可见的一键登录；EditMode 89/89、门禁 66 项全过、APK 已更新。React 管理端同步完成登录、工作台与窄屏改造，并已在真实后端浏览器验收（1440×900、375×812）。注：minSdk 由 25 提至 29（ARCore 硬性要求），图形 API 强制 OpenGL ES3 |
| 管理端 | React 19.3.1 + TypeScript 7.0.2 + Vite 8.3.5 + Tailwind CSS 4.3.3 + oxlint 1.89.0；测试：Vitest 5.0.5 + React Testing Library 16.3.3 + user-event 14.6.8 + jest-dom 7.0.1 + jsdom 30.1.3（Node.js v24.11.0、pnpm 10.21.0） | 见 `web/package.json` 与 `web/pnpm-lock.yaml` | 业务闭环完成：真实列表加载、手动刷新、状态修改保存与全部异常反馈已实现；计划 006 新增管理员登录/会话恢复、WebSocket 实时同步（自动重连+序号去重+静默校准）、标题/优先级/状态三条件筛选，浏览器实测通过；计划 008 新增「人员管理」页（管理员对巡检员账号增删改查：新增表单、行内编辑、两段式删除确认、管理员账号只读展示）与双入口导航（问题列表/人员管理切换保持工作台挂载，实时连接与草稿不丢）；计划 007 完成浅色现场工作台视觉体系：设计令牌与优先级/状态语义色、居中登录卡片、三项摘要、独立筛选工具栏、响应式卡片表格与行内展开、草稿/远端更新提示、静态骨架与空态动作，99 测试全绿；真实后端浏览器验收 1440×900 与 375×812 均无横向溢出，状态保存、实时同步、筛选空态与登录失败提示实测通过。注：工具链由计划锁定的 Vite 7 / TS 5 经用户拍板升级至最新稳定版（2026-10-27，计划文档施工日志留档） |
| 后端 | Go 1.27.3 + Gin 1.12.0 + modernc.org/sqlite 1.61.0（纯 Go 驱动，SQLite 本地文件）+ coder/websocket 1.8.x（实时事件） | 见 `backend/go.mod` 与 `backend/go.sum` | 业务闭环完成：健康检查、创建、列表、状态修改四接口与参数校验、事务持久化、重启数据保留均已验收；计划 006 新增本地演示账号（inspector/admin，bcrypt+会话 Cookie）、按角色收口的授权（匿名 401/越权 403/巡检员仅见自己上报）、`/api/ws` 管理员实时事件流（单调 seq，提交后发布）；计划 008 新增 `/api/users` 四个管理接口（列表/新增/修改/删除，仅管理员），初始化语义改造为「admin 每次启动按环境变量同步，inspector 仅首启创建此后由管理页维护」，删除巡检员保留其问题（归属转未知仅管理员可见）并同步清会话，`-race` 测试全绿 |
| 容器 | Docker（linux/arm64 29.1.3）+ Compose v5.0.0；镜像 golang:1.27-alpine / alpine:3.22 / node:24-alpine / nginx:1.29-alpine | 见 `backend/Dockerfile`、`web/Dockerfile`、`compose.yaml` | 双容器一键启动验收通过（2026-10-28）：arm64 原生构建、健康检查、同源 `/api` 与 WS 代理、全新命名卷初始化、停止/重启数据保留、非 root 运行、局域网直连 |
| 真机 | iQOO Z9（V2361A，Android 16） | ARCore 运行时 1.57.0，实测 SUPPORTED_INSTALLED | 已验证 |

## 如何启动

系统自计划 006 起需要登录（两个本地演示账号：巡检员 `inspector` 用 Unity 端上报，管理员 `admin` 用网页管理端处理）。自计划 008 起巡检员账号由管理员在网页「人员管理」页增删改查，`INSPECTOR_PASSWORD` 仅作为**首次部署创建 inspector 时的初始密码**，此后页面改密即生效、环境变量不再覆盖。两套启动方式二选一：

### 方式一：Docker Compose 一键启动（推荐演示用）

前置：Docker Desktop（本机为 Apple Silicon，linux/arm64；首次构建需下载基础镜像，耗时视网络）。

```bash
cp compose.local.env.example compose.local.env   # 填入自拟的 INSPECTOR_PASSWORD/ADMIN_PASSWORD 与 64 位 hex SESSION_SECRET
docker compose up -d --build                     # 首次构建两镜像并启动
docker compose ps                                # 两容器 Healthy 即就绪
```

- 管理端：`http://localhost:5173`（admin 登录；页面与 `/api`、`/api/ws` 同源，由 Nginx 代理到后端容器，无跨域依赖）
- 真机 Unity：后端地址填 `http://<电脑局域网IP>:8080`（容器已映射 8080，与本机跑 Go 口径一致；手机与电脑同一 Wi-Fi）
- 数据持久化：SQLite 写入命名卷 `ar-inspection_inspection-data`，`docker compose stop`/`restart` 数据保留；显式清理需 `docker compose down -v`（启动命令不会自动删卷）
- 常见问题：局域网访问不通先查电脑防火墙；健康检查失败看 `docker compose logs backend`；后端容器以非 root（uid=10001）运行

### 方式二：本机直接启动（开发方式）

- **Go 后端**：需要三个环境变量（缺失或会话密钥过短会拒绝启动）：`INSPECTOR_PASSWORD`（仅首次部署创建 inspector 的初始密码，见上文）、`ADMIN_PASSWORD`（每次启动同步到 admin 账号）、`SESSION_SECRET`（≥32 字符，随机 64 位 hex 推荐）。本机启动：`cd backend && set -a && source .env && set +a && go run ./cmd/server`（`.env` 参考 `backend/.env.example`，不入库）。默认监听 `:8080`（`ADDR` 可改），数据库默认 `./data/inspection.db`（`ISSUE_DB_PATH` 可改），跨域白名单默认 `http://localhost:5173,http://127.0.0.1:5173`（`CORS_ORIGINS` 可改）。健康检查：`curl http://localhost:8080/api/health` → `{"data":{"status":"ok"}}`。真机联调时需监听 `0.0.0.0`（默认 `:8080` 即是）。测试与静态检查：`go test ./...`、`go test -race ./...`、`go vet ./...`。本地开发可额外设置 `QUICK_LOGIN_ENABLED=1` 启用免密快捷登录（只对内置 inspector/admin 生效，默认关闭，详见下文「开发快捷登录」）。
- **React 管理端**：`cd web && pnpm install && pnpm dev`，默认 `http://localhost:5173`（`VITE_API_BASE_URL` 见 `web/.env.example`，默认请求 `http://localhost:8080`；启动后即从后端加载问题列表）。类型检查/静态检查/测试/构建：`pnpm typecheck`、`pnpm lint`、`pnpm test`、`pnpm build`。
- **Unity 上报模拟工具（开发辅助）**：独立 Go 命令行工具 `tools/issue-reporter/`，代替 Unity 客户端以真实 HTTP 上报/查询问题，用于接口联调与回归（坐标为人工输入的模拟坐标，不代表真实 AR 位置）：

  ```bash
  cd tools/issue-reporter
  go run . create -base-url http://localhost:8080 -username inspector -password <密码> \
    -title "模拟上报：入口墙面破损" -description "非 AR 工具创建" -priority high -x 0.42 -y 0.03 -z 1.26
  go run . list -base-url http://localhost:8080 -username inspector -password <密码>
  ```

  计划 006 起业务接口需要会话：提供 `-username/-password` 先登录（工具从 `Set-Cookie` 头解析会话令牌，与 Unity 端同一方案）再以 Bearer 调用，登录失败直接失败、不回退匿名；已有令牌可用 `-token` 代替登录（与 `-username` 互斥）。详见 [tools/issue-reporter/README.md](tools/issue-reporter/README.md)（参数、输出与退出码）。测试：`cd tools/issue-reporter && go test ./...`。
- **Unity 移动端**：编辑器 6000.3.27f1 与 Android 全套组件已安装（Hub 路径 `/Applications/Unity/Hub/Editor/6000.3.27f1/`）。工程初始化已执行（场景 `Assets/Scenes/Inspection.unity`、Android/ARCore 配置已落地）；日常用 Unity Hub 打开 `unity/` 工程即可。初始化菜单「现场巡检 / 初始化工程（场景 + Android + ARCore 配置）」可重复执行（幂等）：已存在的配置不重复创建，重复的同类型 XR Loader 会被自动清理。
- **Unity 构建与真机安装**（开发构建，IL2CPP + ARM64 + OpenGL ES3，产物不入 Git）：

  ```bash
  # 场景内容搭建（Prefab/材质/UI/脚本接线，幂等；改动 SceneWiring.cs 后重跑）
  /Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.SceneWiring.Run -logFile unity/Logs/scene-wiring.log
  # 构建 APK（编辑器菜单「现场巡检 / 构建 Android 开发包」等价）
  /Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.BuildAndroid.Run -logFile unity/Logs/build-android.log
  adb install -r unity/Builds/inspection-dev.apk
  ```

  EditMode 测试：`Unity -batchmode -projectPath unity -runTests -testPlatform EditMode -testResults <路径>/results.xml`（当前 89/89：计划 004/005 的契约与 AR 输入链路 + 计划 006 鉴权/历史列表/诊断开关/优先级映射 + 计划 007 返回路由、历史标签排版、表单收起与 2 米超距提示分级 + 历史按账号隔离与开发快捷登录用例；注意 `-runTests` 模式不能带 `-quit`）。真机要求 ARCore 设备与相机授权（可 `adb shell pm grant com.honlnk.arinspection android.permission.CAMERA` 预授权）；开发构建顶部栏下方有黄色调试信息条与 `[ARCameraPose]` logcat 输出（仅开发构建存在，且自计划 006 起受「完整诊断」开关控制，默认关闭、设置面板可开；开关即时生效，无需重启）。
- **Unity 放置诊断记录（仅开发构建，默认关闭）**：设置面板打开「完整诊断」后，每次成功放置会等待该帧渲染结束，保存同名的 PNG 截图与 JSON 数据。数据包含屏幕尺寸与点击位置、射线命中位置、全部已检平面的边界世界/屏幕坐标、黄色方块与点击位置的屏幕偏差、锚点、相机姿态/投影/内参与会话状态。屏幕坐标以左下角为原点，单位为像素；偏移的 Y 为负表示方块位于触点下方。记录位于应用私有目录，不自动上传、不自动删除，会占用手机存储；截图包含现场实景，导出和分享前需确认环境可披露。开关状态持久化（PlayerPrefs），业务结果提示（成功/失败/超时）不受开关影响。连接手机后可导出到电脑的临时目录：

  ```bash
  mkdir -p /tmp/ar-placement-diagnostics
  adb pull /sdcard/Android/data/com.honlnk.arinspection/files/PlacementDiagnostics /tmp/ar-placement-diagnostics/
  ```

  诊断记录位于 Android 应用外部文件目录，可直接导出，不使用跨应用私有目录的复制命令。原始截图和 AR 数据仍保留在手机中，上述命令不会清理设备文件。
- **Unity 阶段门禁自检（SK-01/SK-02 自动验证）**：
  `/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -executeMethod Inspection.EditorTools.GateVerify.Run -logFile unity/Logs/gate-verify.log`
  （或在编辑器菜单「现场巡检 / 阶段门禁自检」查看），逐项输出 PASS/FAIL，任一项失败进程退出码非 0。最近一次结果：66 项全部通过（2026-10-28 合并后：在计划 007 的历史标签排版、表单卡片贴底、卡片圆角、诊断开关关闭态方框、返回处理器接线、预测性返回关闭与表单/顶部栏层级等检查之外，新增登录页开发快捷登录入口与面板接线两项检查）。自检不验证 Android 构建后的最终合并 Manifest，该结果不含此项。

### 开发快捷登录（仅本地开发）

两个登录页在开发态各有一个一键登录入口，方便本地联调，不涉及任何密码：

| 客户端 | 入口显示条件 | 一键登录账号 |
| --- | --- | --- |
| Unity 巡检端 | 编辑器或开发构建（`Debug.isDebugBuild`）；正式包隐藏 | 内置巡检员 `inspector` |
| React 管理台 | Vite 开发服务器（`pnpm dev`）；`pnpm build` 生产包不渲染 | 内置管理员 `admin` |

后端默认**不注册**快捷登录路由；本地需要时设置 `QUICK_LOGIN_ENABLED=1` 并重启（`backend/.env` 与 `compose.local.env` 的示例已包含该项）。接口 `POST /api/auth/quick-login` 只接受 `{"role":"inspector"}` 或 `{"role":"admin"}`，只对内置账号签发会话，不接收密码字段；目标账号被删除时返回 401 并提示改用常规登录。后端未启用时按钮会提示「请在后端设置 QUICK_LOGIN_ENABLED=1」。**生产部署请保持关闭或不设置该变量。**

## Unity 真机如何登录与配置 Go 后端地址

自计划 006 起，Unity 端启动后先显示登录面板：每个巡检员用自己的账号登录（计划 008 起由管理员在网页「人员管理」页创建；管理员账号会被拒绝并提示改用网页管理端；首启内置巡检员 `inspector` 的初始密码来自 `INSPECTOR_PASSWORD`）。登录会话令牌仅存于本机 PlayerPrefs（不存密码），重启 App 自动恢复会话；过期或退出登录后回到登录面板，草稿与表单内容保留。「历史」按登录账号隔离，只返回当前巡检员本人的上报，不会看到其他巡检员的数据。

后端地址配置：登录面板右上角「设置」或主界面顶部栏「设置」均可打开配置面板，填入电脑上运行后端服务的局域网地址（如 `http://192.168.31.17:8080`），点「保存」立即生效并持久保存（PlayerPrefs，重启 App 保留）；顶部栏左侧实时显示当前地址。首次启动会自动弹出一次设置面板引导配置。要求手机与电脑同一 Wi-Fi，后端监听 `0.0.0.0`（默认 `:8080` 即是；Docker 启动时容器已映射 8080，填法相同）。

- 开发构建通过 AAR 插件注入 `networkSecurityConfig` 放行明文 HTTP（仅限局域网联调，不含任何敏感信息）；顶部栏「手电」可在暗光环境补光辅助平面检测，「重置」可在跟踪异常（如点击落点明显异常）时一键重建 AR 地图，「历史」只展示当前登录巡检员本人的上报（服务端按账号过滤，面板显示「当前账号：…（仅显示本人上报）」；只读列表，含刷新与退出登录；共享设备切换账号或退出登录会清空上一账号的列表）。
- 真机联调可用独立实例避免污染日常数据：`ADDR=0.0.0.0:18080 ISSUE_DB_PATH=~/.ar004-tmp/inspection.db go run ./cmd/server`（需同时提供三个账号/会话环境变量，2026-10-28 真机上报验收即用此实例）。
- 上报超时 10 秒；后端不可达/超时/异常响应均有中文提示，表单与标记保留可原样重试；会话过期时提示重新登录且草稿不丢。

## 后端接口说明

基础路径 `/api`，请求与响应均为 UTF-8 JSON。成功响应为 `{ "data": … }`（列表的 `data` 恒为数组，空列表返回 `[]`）；错误响应为 `{ "error": { "code": …, "message": … } }`，`message` 为中文提示。创建与状态修改请求拒绝未知字段和对象后的额外 JSON 内容，请求体上限 1 MiB。

### 账号与会话（计划 006 起）

两个本地演示账号：`inspector`（巡检员，Unity 端上报）与 `admin`（管理员，网页管理端处理）。密码 bcrypt 存储；会话令牌 12 小时有效，仅以 HMAC-SHA256 摘要存库。浏览器走 HttpOnly Cookie `inspection_session`；非浏览器客户端（Unity/工具）从登录响应的 `Set-Cookie` 头解析令牌后以 `Authorization: Bearer <令牌>` 携带，任何端都不存密码。

自计划 008 起，管理员可在网页「人员管理」页增删改查巡检员账号：新增即建即登录（新账号无需重启即可用 Unity 端登录上报）；改显示名不影响会话；重置密码使该账号全部会话立即失效；删除账号保留其历史问题（归属转为未知，仅管理员可见）。`admin` 为系统账号由环境变量维护，页面只读；`inspector` 为首启自动创建的普通巡检员，此后与其他巡检员一样由页面管理（首启初始密码来自 `INSPECTOR_PASSWORD`，此后以页面设置为准）。

| 方法与路径 | 用途 | 权限 |
| --- | --- | --- |
| `POST /api/auth/login` | 登录（`{username, password}`），成功 Set-Cookie 下发会话 | 匿名 |
| `POST /api/auth/logout` | 登出，会话立即失效 | 已登录 |
| `POST /api/auth/quick-login` | 开发期快捷登录（`{role:"inspector"\|"admin"}`，只对内置演示账号签发会话、不校验密码）；默认不注册，仅 `QUICK_LOGIN_ENABLED=1` 时存在 | 匿名（仅开发后端） |
| `GET /api/auth/me` | 当前会话用户信息（会话恢复用） | 已登录 |
| `GET /api/health` | 服务与存储可用性检查 | 匿名 |
| `GET /api/issues` | 问题列表 | inspector（仅自己上报）/ admin（全量，含历史无主数据） |
| `POST /api/issues` | 创建问题（Unity 上报） | inspector / admin |
| `PATCH /api/issues/{id}/status` | 修改处理状态 | 仅 admin |
| `GET /api/users` | 巡检员与管理员账号列表（人员管理页） | 仅 admin |
| `POST /api/users` | 新增巡检员（`{username, displayName, password}`；用户名 2～32 位限字母数字下划线连字符且不可改，显示名 1～32 字符，密码 8～72 位） | 仅 admin |
| `PATCH /api/users/{username}` | 修改显示名与/或重置密码（重置密码清空该账号全部会话） | 仅 admin |
| `DELETE /api/users/{username}` | 删除巡检员（保留其历史问题，归属转未知；admin 账号不可改删） | 仅 admin |
| `GET /api/ws` | 实时事件流（WebSocket；创建/更新事件，事件携带单调递增 `seq`，客户端据此去重与重连校准） | 仅 admin |

未认证统一 `401 authentication_required`；已认证但角色不允许统一 `403 permission_denied`。人员管理另有细分：目标账号不存在 `404 user_not_found`，对管理员账号改删 `403 admin_account_protected`，用户名重复或校验非法 `400 invalid_argument`（中文提示区分「用户名已存在」等场景）。

### 问题字段与校验

成功状态码：健康检查 `200`（存储不可用 `503 service_unavailable`）、列表 `200`（按创建时间从新到旧，同时间按 ID 稳定排序，不分页）、创建 `201`、状态修改 `200`（`data` 均为完整问题或问题数组）。

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
| `400` | `invalid_argument` | JSON 无法解析、额外 JSON 内容、请求体超过 1 MiB、字段缺失或类型错误、未知字段、空标题、非法枚举、无效坐标、登录凭据缺失 |
| `401` | `authentication_required` | 未登录或会话过期/已登出（业务接口与 `/api/ws`） |
| `403` | `permission_denied` | 已登录但角色不允许（如巡检员改状态、连 `/api/ws`） |
| `404` | `issue_not_found` | 状态修改对应的问题 ID 不存在 |
| `404` | `not_found` | API 路径不存在 |
| `405` | `method_not_allowed` | 已知路径使用未开放的方法 |
| `500` | `internal_error` | ID/时间生成失败、存储读写失败等内部问题（详细信息仅写服务端日志） |
| `503` | `service_unavailable` | 健康检查发现存储不可用 |

快速验证（后端启动后；`<密码>` 即启动时配置的对应账号密码）：

```bash
curl http://localhost:8080/api/health
# 登录换会话 Cookie（浏览器方式；非浏览器客户端从 Set-Cookie 头取令牌走 Bearer）
curl -i -X POST http://localhost:8080/api/auth/login -H 'Content-Type: application/json' \
  -d '{"username":"inspector","password":"<密码>"}'
# 业务调用（用上一步 Set-Cookie 中的 inspection_session 值）
curl http://localhost:8080/api/issues -H 'Authorization: Bearer <令牌>'
curl -X POST http://localhost:8080/api/issues -H 'Authorization: Bearer <令牌>' -H 'Content-Type: application/json' \
  -d '{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}'
```

重启后端（或 `docker compose restart backend`）后再次登录查询，问题与修改后的状态仍然存在。

## 已完成 / 未完成 / 已知问题

**已完成**

- 真机 ARCore 可行性验证：iQOO Z9 实测 AR 会话创建、相机启动、运动跟踪运行全部成功，ARCore 官方判定 SUPPORTED_INSTALLED（证据：`tools/ar-support-check/results/`、`docs/evidence/`）
- 仓库结构搭建、任务书与验证材料归档
- Go 后端骨架：Gin 分层结构（cmd/internal：issue 存储 + httpapi）、SQLite WAL 自动建表、`GET /api/health`、统一响应结构与 CORS 基础、优雅关闭；常规测试与构建门禁通过。健康检查为真实读库检查（现有连接 + 全新连接各读一次 SQLite 系统表），连接关闭、运行中文件损坏、重启后文件损坏三类故障均返回 503，有测试覆盖
- Go 后端业务闭环（2026-10-27 计划 002）：`POST /api/issues` 创建、`GET /api/issues` 列表、`PATCH /api/issues/{id}/status` 状态修改三接口上线——严格 JSON 校验（未知字段/额外内容/1 MiB 上限/中文错误）、后端生成 UUID v4 与北京时间、事务化 SQLite 读写、`created_at DESC, id ASC` 稳定排序、空列表 `[]`；模型/存储/HTTP 三层测试与 `-race` 全过，真实进程「创建→列表→改状态→停止→重启→再查询」验收通过（详见 `docs/plan/002` 施工日志）
- React 管理端业务闭环（2026-10-27 计划 003）：真实问题列表首次加载与手动刷新（刷新保留未保存草稿、失败保留旧数据）、行内状态修改与保存（只发送 `{status}`、以服务端完整结果替换、失败保留原状态与草稿可原行重试）、单笔保存与列表请求互斥、10 秒超时、统一信封与运行时结构检查、北京时间展示、全部失败场景可见提示与重试入口（不白屏不伪装）；42 个组件与请求层测试全过，并与真实 Go 后端 + 模拟工具完成「工具创建 → React 刷新 → React 改状态 → 工具复核 → 后端重启 → React 复核」联合验收（桌面与 375px 窄屏，证据 `docs/evidence/003-react-bug-management/`）
- Unity HTTP 上报模拟工具 `tools/issue-reporter/`（2026-10-27 计划 003）：独立 Go Module（仅标准库）create/list 子命令、完整 JSON 输出、10 秒默认超时、0/1/2 退出码、创建与列表契约检查、失败不自动重试；22 个测试（含 `-race`）全过。定位为开发辅助与回归工具，不是正式产品端、不验证 AR 能力，后续 Unity 真机完成前用于隔离接口问题
- Unity 工程骨架：编辑器 6000.3.27f1 + Android 全套组件已安装；工程初始化完成（`Inspection.unity` 主场景含 AR 会话/AR 相机/仅水平平面检测/射线/锚点管理器/Canvas/EventSystem，Android IL2CPP+ARM64+Both 输入，ARCore Loader 已启用，自定义 Main Manifest 文件含 INTERNET 权限与 ARCore 包可见性声明）；编辑器实际打开无编译错误，初始化幂等已复验（重复 Loader 自动清理）。Custom Main Manifest 由标准路径下的 Manifest 文件启用，无需手动勾选
- Unity 真机 AR 放置与上报业务（2026-10-28 计划 004）：Android 开发构建链路（命令行/菜单一键构建，合并 Manifest 含 INTERNET/CAMERA/ARCore queries/networkSecurityConfig）；真机平面检测与可视化（半透明色块 + 边界描线，手电补光按钮）；点击平面放置黄色标记（平面附锚，单草稿语义，距相机 >2m 的命中拒绝放置并提示靠近，6m 及以上或相机后方提示重置，顶部「重置」一键重建地图）；「上报此标记」跟随按钮（投影跟随、出屏隐藏）；移动表单（标题必填/描述/优先级下拉/只读坐标）与全屏蒙层防误触；HTTP 上报（10 秒超时、六类响应分支中文提示）；后端地址运行时配置（首启引导一次、PlayerPrefs 持久）。EditMode 测试 26/26、门禁自检 35/35；真机完整链路验收通过——后端实测收到真机上报 `{"title":"111","position":{"x":-0.039,"y":0.303,"z":1.52}}` 全字段一致（AC-03 ✅，证据 `docs/evidence/004-unity-ar-place-report/`）
- Unity 成功放置诊断记录（开发构建）：同名截图与结构化数据已接入，具体字段和导出方式见上文；构建和编辑器测试通过，五组真机记录已读取并完成双会话交叉验证，结论见复盘 002。
- Unity AR 相机姿态与放置偏移修复（2026-10-28 计划 005）：AR Session 同对象补齐唯一 AR 输入管理器、主相机改用 Input System 姿态驱动并按官方绑定手持 AR 设备位置/旋转、初始化与搭建共用同一配置（幂等，副本两轮全量循环验证）；门禁升级为输入链路精确检查，EditMode 31/31（含负例）、门禁 35/35。**真机复验通过**：139 条相机姿态采样无冻结（位置三轴变化 0.46/0.27/0.60 m）、24 组放置标记世界坐标=命中点且屏幕投影与触点偏移 0px、世界锁定与平面对齐正常、UI 防误触/跟随按钮/上报成功与失败回归/地址重启保留全部通过（证据 `docs/evidence/005-unity-camera-pose-fix/verification-report.md`）。
- 功能完善与三端交付闭环（2026-10-28 计划 006）：① 本地演示账号与真实后端权限（bcrypt 口令、HttpOnly Cookie 会话 + Bearer 双通道、12 小时 TTL、巡检员仅见自己上报/管理员全量/匿名 401/越权 403）；② React WebSocket 实时同步（管理员事件流、单调 seq 去重、断线指数退避重连+静默全量校准，浏览器实测「另一端创建→看板不刷新自动出现」）；③ 管理端三条件筛选（标题关键词+优先级+状态，纯前端过滤，计数与草稿语义保留）；④ Docker Compose 一键启动（arm64 双镜像、同源 `/api`+WS 代理、命名卷持久化、非 root、健康检查，全新卷验收含停止/重启数据保留）；⑤ Unity 登录会话（会话恢复、admin 拒入、过期保草稿跳登录）+ 只读历史列表（按创建时间倒序、北京时间、刷新/退出登录）；⑥ AR 草稿三色优先级（低绿/中橙/高红，共享材质、表单内切换即时重着色且几何/锚点/坐标不变）；⑦ 完整诊断开关（默认关、持久化，门控调试条/网络详细日志/放置截图，业务提示不受影响）。三端闭环（工具代真机上报→React 实时出现→管理员改状态→API 复核→容器重启→再查询）与必做回归（空标题/非法枚举 400、匿名 401、巡检员改状态 403、后端不可用提示不白屏、恢复自动重连）全部通过（证据 `docs/evidence/006-feature-completion/`）。
- Unity 界面美化与系统返回（2026-10-28 计划 007，Unity 部分）：历史卡片标签/时间排版、设置页账号说明遮挡、Android 系统返回统一分发（键盘→设置→登录→历史→表单→根页面）、上报表单白屏、卡片/输入/按钮/标签圆角、诊断开关关闭态方框与 2 米超距提示分级已修复；放置输入增加 Ended 帧 UI 复核与执行顺序约束，点上报按钮不再偶尔误放置。EditMode 85/85、门禁 64 项全过。真机已验证表单完整显示与圆角、返回收起保留草稿与输入、键盘优先收起、模态蒙层阻断顶部栏点击、设置/历史关闭、左右边缘滑动返回、根页面二次返回退出、登录页首次返回不隐藏且二次返回退出、2.2/2.7 米超距点击提示靠近而非重置。真机阶段 3 补测完成：三色标记（中橙/低绿/高红）在实景切换、标题上报成功并入库、历史列表出现新记录且刷新可用、诊断开关开启与关闭均即时生效；同时修复调试条开启后需重启才能恢复（`Text` 与组件同物体，改为只切 `Text.enabled`）和系统返回收起软键盘会清空已输入标题（uGUI `wasCanceled` 回滚，表单增加输入缓存并在取消后恢复）两个缺陷，调试条下移不再压住顶部栏。证据 `docs/evidence/007-ui-polish/device/`（13 张）。
- React 管理端界面美化（2026-10-28 计划 007，React 部分）：统一浅色中性底、白色内容面与深靛蓝主操作的令牌体系，优先级固定低绿/中橙/高红、状态固定待处理橙/处理中蓝/已解决绿并始终带中文文字；登录页改为居中卡片并保留输入与错误；工作台新增「巡检问题」标题、三项摘要（全部/待处理/处理进度）、独立筛选工具栏与靠近列表的计数；问题列表在桌面为卡片表格、窄屏同一份 DOM 切换为卡片，不靠 `overflow-x` 兜底；描述两行截断并可行内展开查看完整描述、坐标和创建时间；草稿行浅色底并标注「未保存 → 目标状态」，远端更新与草稿分开展示；加载使用静态骨架，空态提供重新加载或清空筛选动作；窄屏顶部改为两行（产品名 + 账户菜单 / 双入口导航），账户菜单可键盘展开。`pnpm typecheck`、`pnpm lint`、`pnpm test`（102/102）、`pnpm build` 全绿；真实后端浏览器验收 1440×900 与 375×812 无横向溢出，管理员登录、失败提示、筛选空态、行展开、状态保存、实时事件自动更新与人员管理页均实测通过（证据 `docs/evidence/007-ui-polish/react/`）。
- 演示数据与交付检查（2026-10-28 计划 007 阶段 4）：清空历史测试记录后重建干净数据库，账号仅保留 `admin` / `inspector`；以巡检员身份预置 2 条演示问题——「南门岗消防通道占位」（中 / 待处理）与「3号楼东侧照明故障」（高 / 待处理），保留现场创建第 3 条的空间。真机历史列表、管理员接口视图均与预置数据一致，刷新正常；后端地址 `192.168.31.17:8080` 与手机同网段可达（ping 0% 丢包），手机电量 100%、温度 37.2°C、充电中。仓库检查确认无构建产物入库（`unity/Builds/`、`web/node_modules`、`web/dist`、`backend/data/` 与本地 env 均被忽略），跟踪文件无会话令牌或密钥字符串，本轮变更集中在计划 007 的 Unity/React 界面与文档；`TEST-ACCOUNTS.md` 的演示密码按用户确认保留为本地案例，不做脱敏。
- 人员管理功能（2026-10-28 计划 008）：管理员在网页管理台对巡检员账号增删改查。后端 `/api/users` 四接口（仅管理员，错误码细分 user_not_found/admin_account_protected）；初始化语义改造（admin 每次启动按环境变量同步，inspector 仅首启创建，删除账号重启不复活）；重置密码/删除账号同事务清会话（旧令牌立即 401）；删除保留历史问题（归属转未知，仅管理员可见，与既有遗留数据语义一致）。React 新增「人员管理」页（新增表单/行内编辑/两段式删除确认/管理员只读分区）与双入口导航（问题列表、人员管理两面板保持挂载切换，实时连接、草稿与筛选不丢）。六步闭环验收（页面创建→新账号即建即登录即上报→实时可见→三视角归属可见性→改显示名不踢会话→重置密码踢会话→删除含数据库三表复核→重启不复活）全部通过；验收中发现的 CORS 预检缺 DELETE 已修复（证据 `docs/evidence/008-personnel-management/`）。
- 巡检员历史按账号隔离（2026-10-28）：Unity「历史」只展示当前登录巡检员本人的上报。服务端 `GET /api/issues` 对 inspector 按账号过滤、对未知角色失败关闭（403），管理员仍看全量；Unity 登录会话新增用户名持久化，共享设备切换账号或退出登录时清空上一账号的历史列表并作废在途请求，面板显示「当前账号：…（仅显示本人上报）」。新增 Go 双巡检员隔离测试与 3 条 Unity EditMode 用例；实测张三/李四各自只见本人上报、管理员见全部，`go test -race ./...` 全绿、合并到主分支后 EditMode 89/89。
- 开发快捷登录（2026-10-28）：Unity 开发构建/编辑器与管理台 Vite 开发态登录页各新增「开发快捷登录」按钮，一键以内置 inspector/admin 登录，不输入也不保存密码。后端新增 `POST /api/auth/quick-login`，默认不注册（`QUICK_LOGIN_ENABLED=1` 才启用），只接受 role 且只对内置账号签发会话，未知角色或携带密码字段 400、目标账号被删除 401；启动时输出启用告警。Unity 按 `Debug.isDebugBuild || Application.isEditor` 显示、管理台按 `import.meta.env.DEV` 显示，正式构建均不渲染；后端未启用时提示「请在后端设置 QUICK_LOGIN_ENABLED=1」。验证：Go 新增 4 条接口测试（默认 404、双角色签发、严格校验、账号删除）且 `-race` 全绿；React 新增 3 条登录页测试、合并后共 102/102，typecheck/lint 通过；Unity 合并后 EditMode 89/89、门禁 66/66；临时后端实测启用 200、禁用 404、快捷令牌可用于 `/api/auth/me`。

**未完成**

- 计划 007 剩余用户侧工作与录屏：开发、自动化、浏览器与真机验收均已完成（React 102/102、Unity EditMode 89/89 + 门禁 66 项、APK 已更新，真机三色标记/上报/历史刷新/诊断开关均已验证），演示数据已整理为 2 条预置问题；仍待用户按计划 006 脚本完成室内彩排与正式录屏
- 演示视频录制与最终提交（提交/推送等待用户授权）

**已知问题**

- **Unity 放置位置偏移（已修复并真机复验通过，2026-10-28 计划 005，留档）**：真机上 AR 虚拟层曾相对相机实景错位、标记落在触点下方或屏幕外。根因：AR Session 缺 AR 输入管理器、主相机误用旧版空间跟踪驱动，手机姿态未进入渲染相机。已按计划 005 修复并真机验证（详见「已完成」与 [复盘 002](docs/lessons/002-AR虚拟内容偏移与相机位姿冻结.md) 第 10 节）；证据 `docs/evidence/005-unity-camera-pose-fix/`。- **Docker 基础镜像未锁 patch 级 digest（可重复性取舍）**：`golang:1.27-alpine`、`node:24-alpine`、`nginx:1.29-alpine`、`alpine:3.22` 为浮动小版本 tag，跨机器重建理论上存在小版本漂移；pnpm 已锁 `10.21.0`、Go 依赖与前端依赖均按锁文件冻结。如需严格可重复构建，可在部署时改为 digest 固定。
- **计划 007 真机验收已覆盖界面与返回链路**：Unity 登录、设置页、历史列表、上报表单、三色标记、上报提交、历史刷新、诊断开关与全部系统返回场景均已在真机验证；正式录屏与演示数据整理仍待用户完成。
- **人员管理未经真机实测（计划 008）**：新账号从 Unity 真机端登录上报的链路由 `tools/issue-reporter`（同一 HTTP 契约）代验；人员管理页的桌面闭环已全部通过，但真机上用新建巡检员账号走一遍登录+上报尚未实测（Unity 端登录界面无改动，预期不受影响）。
- **真机长时间运行发热降频（已缓解）**：持续扫描 + 手电常亮会使机身升温触发 Thermal Status Severe（实测电池 40.3°C），ARCore 识别随之变慢；已将 App 全局限帧 30fps（`AppFrameRate`）并建议短测多轮。iQOO Z9（骁龙 7 Gen 3）算力本身满足 ARCore 要求（SUPPORTED_INSTALLED），非算力瓶颈。
- **真机切后台/息屏后相机失效（已定位，规避法有效）**：App 切后台再恢复后相机帧持续报 "The hardware buffer handle is null"、触摸无响应；`force-stop` 后重启即恢复。未做深度修复（涉及 ARCore 会话恢复生命周期），演示时注意保持亮屏。
- **Unity 下载的地域限制（已解决，留档）**：Unity CDN（Akamai）按出口 IP 判定大中华区并重定向到 download.unitychina.cn（对 Unity 6 macOS 包一律 404）；本机机场的香港/日本节点 IP 段（ASN 134972）同样命中该判定，切换到「美国A01」节点后正常。后续下载 Unity 相关资源需保持美国/非大中华区出口节点。
- **最低 SDK 版本（计划偏差，累计两层）**：计划要求 API 24，Unity 6000.3 引擎最低仅支持 API 25；ARCore + Vulkan 组合硬性要求 API ≥ 29，故 minSdk 落在 29（ProjectSettings/ProjectSetup/GateVerify 三处同步）。对目标真机（Android 16）无影响。
- **SQLite 健康检查覆盖不足（已修复，留档）**：原实现仅探测数据库连接，文件内容损坏时仍可能误报正常。2026-10-27 修复为真实读库检查（现有连接 + 全新连接各读一次 SQLite 系统表），并补齐运行中损坏与重启后损坏的回归测试；WAL 完好时的主文件覆盖经查证不属可检出故障（数据仍由 WAL 提供），详见计划 001 第 7.3 节。
- **ARCore Loader 重复（已修复，留档）**：Android XR 配置曾包含两个独立的 ARCore Loader，初始化脚本重复执行会继续累积，自检当时无法发现。2026-10-27 修复：初始化按类型复用并自动清理重复项（幂等），自检新增同类型重复检查项，现有配置已清理。

## 佐证材料

- [docs/真机ARCore可行性验证报告.md](docs/真机ARCore可行性验证报告.md) — 本轮设备验证的完整记录（结论、排查过程、UNKNOWN_ERROR 根因、复现方式）
- `docs/evidence/` — Play 商店"不兼容"提示与 vivo 商店页面对比截图（背景说明：Play 分发目录不含国行型号，与运行时支持无关，详见 AI_LOG 第 7 条）；`docs/evidence/003-react-bug-management/` — 计划 003 浏览器联合验收截图（桌面与 375px 窄屏正常列表、窄屏刷新失败提示）；`docs/evidence/004-unity-ar-place-report/` — 计划 004 真机验收截图（平面检测、放置标记与跟随按钮、点击位置偏移问题留证）；`docs/evidence/005-unity-camera-pose-fix/` — 计划 005 相机姿态修复真机复验报告；`docs/evidence/006-feature-completion/` — 计划 006 证据（登录后实时看板与筛选截图、三端闭环截图、Docker 验收记录、闭环与回归验收记录）；`docs/evidence/007-ui-polish/react/` — 计划 007 React 浏览器验收截图（1440×900 登录/工作台/行展开/未保存草稿/保存成功/筛选空态/实时更新，375×812 登录/工作台/卡片操作/账户菜单，登录失败提示）；`docs/evidence/007-ui-polish/device/` — 计划 007 真机验收截图（三色标记、优先级切换、上报成功、历史新记录与刷新、诊断开关开启/关闭、返回键保留输入、2 条预置演示数据）；`docs/evidence/008-personnel-management/` — 计划 008 人员管理六步闭环验收记录与七张截图（页面操作全程 + 数据库复核）
- `tools/ar-support-check/` — ARCore 支持检测工具源码与三张真机输出截图

## 复盘记录

- [docs/lessons/001-CustomMainManifest门禁误报.md](docs/lessons/001-CustomMainManifest门禁误报.md) — Custom Main Manifest 开关的真实机制、门禁误报根因与防再犯规则
- [docs/lessons/002-AR虚拟内容偏移与相机位姿冻结.md](docs/lessons/002-AR虚拟内容偏移与相机位姿冻结.md) — AR 平面与标记偏移的真机证据、相机输入断链根因、已排除误判与修复验收方向
