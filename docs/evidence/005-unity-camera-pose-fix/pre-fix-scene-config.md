# 修复前主场景 AR 输入链路配置证据（计划 005 阶段 0）

- 采集时间：2026-10-28（施工前基线）
- 基线提交：文档 `c72b60d`，Unity 功能 `66dc6f5`（工作树无未提交的 `unity/` 改动）
- 采集方式：直接读取 `unity/Assets/Scenes/Inspection.unity` 序列化内容与本机包缓存源码核对，未做任何修改

## 1. 主相机挂的是旧版空间跟踪姿态驱动

主场景中主相机（GameObject fileID `753962736`）上的姿态驱动组件序列化标识为：

```yaml
m_EditorClassIdentifier: UnityEngine.SpatialTracking::UnityEngine.SpatialTracking.TrackedPoseDriver
m_Device: 0
m_PoseSource: 2
m_TrackingType: 0
m_UpdateType: 0
```

即旧版 `UnityEngine.SpatialTracking.TrackedPoseDriver`，沿旧式设备姿态通道取数。组件上没有任何 Input System 位置/旋转动作绑定（该组件没有此字段）。

## 2. AR Session 对象缺少 AR 输入管理器

`AR Session`（GameObject fileID `949439176`）的全部组件仅有两个：

```yaml
- component: {fileID: 949439178}   # Transform
- component: {fileID: 949439177}   # ARSession（m_EditorClassIdentifier: Unity.XR.ARFoundation::UnityEngine.XR.ARFoundation.ARSession）
```

没有 `ARInputManager`，手持 AR 输入链路不完整。

## 3. 与官方包源码的对照（本机缓存版本）

| 对照项 | 官方来源与内容 | 当前工程 | 结论 |
| --- | --- | --- | --- |
| AR Session 创建 | `com.unity.xr.arfoundation@6.6.2` `Editor/ARFoundation/SceneUtils.cs` `CreateARSessionWithParent`：`CreateGameObject("AR Session", typeof(ARSession), typeof(ARInputManager))` | 仅 `ARSession` | 缺输入管理器 |
| 相机姿态驱动 | `Editor/ARFoundation/XROriginCreateUtil.cs` `CreateARMainCamera`：`UnityEngine.InputSystem.XR.TrackedPoseDriver` | 旧版 SpatialTracking 驱动 | 驱动类型错误 |
| 位置输入 | 官方内嵌 `InputAction("Position", binding: "<XRHMD>/centerEyePosition")` + `AddBinding("<HandheldARInputDevice>/devicePosition")` | 无 | 缺绑定 |
| 旋转输入 | 官方内嵌 `InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation")` + `AddBinding("<HandheldARInputDevice>/deviceRotation")` | 无 | 缺绑定 |
| 驱动更新方式 | 官方未改默认值（`m_UpdateType = UpdateAndBeforeRender`，源码 `TrackedPoseDriver.cs` 字段默认） | 旧组件无对应概念 | — |
| 手持 AR 设备来源 | `com.unity.xr.arcore@6.6.2` `Runtime/Input/InputLayoutLoader.cs`：`RegisterLayout<HandheldARInputDevice>(interface XR* + product "(ARCore)")` | 依赖已在工程（包版本一致） | 运行时设备可注册 |
| 已弃用组件 | `Runtime/ARFoundation/Deprecated/ARPoseDriver.cs` 标注 `[Obsolete("ARPoseDriver is now deprecated, please use the InputSystem TrackedPoseDriver component instead.")]` | 初始化工具仍在优先查找它 | 回退来源需删除 |

## 4. 缺陷进入场景的来源

`unity/Assets/Editor/ProjectSetup.cs` 的 `AddPoseDriver`：优先反射查找 `UnityEngine.XR.ARPoseDriver`（当前包已弃用，查找失败），随后回退添加旧版 `UnityEngine.SpatialTracking.TrackedPoseDriver`；`BuildScene` 创建 AR Session 时也只加了 `ARSession`。复盘 002 第 3 节的根因与场景实际内容一致。

## 5. 基线自动化状态（施工前）

- EditMode 测试：26/26 通过（`unity/Logs/tests-baseline-005.xml`，2026-10-28）
- 阶段门禁（旧口径）：35/35 通过（`unity/Logs/gate-baseline-005.log`）——旧门禁只按组件名检查"有姿态驱动"，未识别上述缺陷，这正是计划 005 要升级的检查
- 真机：iQOO Z9 未连接（`adb devices` 为空），真机相关项列为待办
