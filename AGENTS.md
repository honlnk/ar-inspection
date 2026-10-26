# AGENTS.md

现场巡检问题标注原型（园区巡检：Unity AR 放标记上报 → Go 后端保存 → React 管理端查看/改状态）。这是一个 5 天内交付的招聘实操作业仓库，当前处于预启动阶段：`unity/`、`backend/`、`web/` 仅有 `.gitkeep`，尚未开始编码。

## 目录

- `unity/` — Unity LTS + AR Foundation + ARCore 移动端（未开始）
- `backend/` — Go 后端，计划监听 `:8080`（未开始）
- `web/` — React + TypeScript 管理端（未开始）
- `tools/ar-support-check/` — 一次性 ARCore 支持检测 Android 工具（已完成，仅作佐证，勿当作正式工程修改）
- `docs/` — 任务书、真机验证报告、证据截图
- `AI_LOG.md` — 作业要求的 AI 使用记录（见下）

## 改代码前必读

- `docs/Unity-AR研发实操题-候选人任务书-简略版.md` — 全部需求的唯一来源，改任何一端前先对照其第 3/4 节
- `docs/PRD-现场巡检问题标注.md`、`docs/ARCHITECTURE-现场巡检问题标注.md` — 需求与架构文档；技术方向已确认，持久化采用 SQLite，不使用 MySQL
- `README.md` — 交付物之一，需随进度同步更新：使用的版本、三端启动方式、Unity 配置后端地址、已完成/未完成/已知问题

## 硬性约束（任务书）

- 技术栈固定：Unity LTS + C# + AR Foundation（Android/ARCore 路线）、React + TypeScript、Go（框架不限）；持久化已确定为 SQLite 本地文件
- 三端字段名必须一致：`id / title / description / priority(low|medium|high) / status(open|in_progress|resolved) / position{x,y,z} / createdAt / updatedAt`
- 后端：生成 id、默认 status=open、标题为空或枚举非法要返回明确参数错误、重启后数据仍在、Unity 和 React 共用同一份数据
- Unity 端点击按钮/输入框时不得误触发放置标记；React 端后端不可用时要显示错误提示，不能白屏
- 不得提交 API Key、Token、账号等敏感信息，脱敏格式 `[REDACTED: …]`
- 交付要求保留完整 Git 历史，不要 squash/rewrite 已有提交

## AI_LOG.md 规则

每次用户主动发指令，按既有格式追加记录：`### Prompt 原文`（引用块保留原文，不能只写总结）+ `### 结果`（采用/修改后采用/未采用 + 如何验证）。被要求更新此文件时严格沿用现有条目格式与按天分节的顺序。

## 真机已知事实与坑（iQOO Z9 / V2361A，Android 16）

- ARCore 运行时 1.57.0，实测 `SUPPORTED_INSTALLED`，会话/相机/跟踪全部可用；Google Play 显示"不兼容"是国行渠道分发问题，与运行时支持无关，不要据此判断设备不支持
- Android 11+ 侧载 App（targetSdk≥30）查询 ARCore 可用性时，manifest 必须含 `<queries><package android:name="com.google.ar.core" /></queries>`，否则报 `UNKNOWN_ERROR`（详见 `docs/真机ARCore可行性验证报告.md`）
- 真机访问电脑后端：手机与电脑同一 Wi-Fi，后端监听 `0.0.0.0`，Unity 内 BaseUrl 用 `http://<电脑局域网IP>:8080`

## 常用命令

- 检测工具（一般无需重建）：`cd tools/ar-support-check && ./gradlew assembleDebug && adb install -r app/build/outputs/apk/debug/app-debug.apk`
- 后端（建立后）：`cd backend && go run ./cmd/server`
- 管理端（建立后）：`cd web && pnpm install && pnpm dev`；项目依赖统一使用 pnpm，不使用 npm/yarn

## 约定

- 文档、README、注释均用中文
- 管理端样式只使用 Tailwind CSS 工具类，禁止普通组件 CSS、CSS Modules 和行内样式；参考 `../linkseek` 管理台布局，以 React 重写，不引入其 Vue/Naive UI 依赖
- 业务时间统一北京时间（UTC+08:00），API 使用带 `+08:00` 偏移的 RFC 3339 字符串
- 先完成必做内容，加分项另行规划，不在首版提前展开
- 版本信息（Unity 版本、Go 版本等）在首次安装/确认后填入 README 的版本表，不要留空也不要瞎填
