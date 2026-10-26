# 现场巡检问题标注（Unity AR + Go + React）

园区巡检场景的小型原型：巡检员用手机在真实环境中放置 AR 标记并上报问题，管理人员在网页端查看并更新处理状态。任务书全文见 [docs/Unity-AR研发实操题-候选人任务书-简略版.md](docs/Unity-AR研发实操题-候选人任务书-简略版.md)。

## 使用的版本

| 部分 | 技术 | 版本 | 状态 |
| --- | --- | --- | --- |
| 移动端 | Unity LTS + AR Foundation + ARCore | 待安装后补充 | 未开始 |
| 管理端 | React + TypeScript | 待补充 | 未开始 |
| 后端 | Go | 待补充（`go version` 确认后填写） | 未开始 |
| 真机 | iQOO Z9（V2361A，Android 16） | ARCore 运行时 1.57.0，实测 SUPPORTED_INSTALLED | 已验证 |

## 如何启动

- **Go 后端**：（待补充，计划 `cd backend && go run ./...`，默认监听 `:8080`）
- **React 管理端**：（待补充，计划 `cd web && npm install && npm run dev`）
- **Unity 移动端**：（待补充：Unity 版本、打包方式 `Build And Run` 到真机）

## Unity 真机如何配置 Go 后端地址

（待实现。计划：在 Unity 工程中提供常量/运行时可配置的 BaseUrl，默认 `http://<电脑局域网IP>:8080`，并在构建说明中注明手机与电脑需同一 Wi-Fi、后端监听 0.0.0.0。）

## 已完成 / 未完成 / 已知问题

**已完成**

- 真机 ARCore 可行性验证：iQOO Z9 实测 AR 会话创建、相机启动、运动跟踪运行全部成功，ARCore 官方判定 SUPPORTED_INSTALLED（证据：`tools/ar-support-check/results/`、`docs/evidence/`）
- 仓库结构搭建、任务书与验证材料归档

**未完成**

- Unity AR 移动端（平面检测、点击放置标记、表单上报）
- Go 后端（健康检查、接收上报、列表、状态修改、持久化）
- React 管理端（列表展示、状态修改、错误提示）
- 演示视频（手机端 + 电脑端各一段）

**已知问题**

- 无（开发中如有会如实更新至此）

## 佐证材料

- [docs/真机ARCore可行性验证报告.md](docs/真机ARCore可行性验证报告.md) — 本轮设备验证的完整记录（结论、排查过程、UNKNOWN_ERROR 根因、复现方式）
- `docs/evidence/` — Play 商店"不兼容"提示与 vivo 商店页面对比截图（背景说明：Play 分发目录不含国行型号，与运行时支持无关，详见 AI_LOG 第 7 条）
- `tools/ar-support-check/` — ARCore 支持检测工具源码与三张真机输出截图
