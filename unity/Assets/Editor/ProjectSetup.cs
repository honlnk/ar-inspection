using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using Unity.XR.CoreUtils;

namespace Inspection.EditorTools
{
    /// <summary>
    /// 一次性工程初始化：创建巡检主场景、目录骨架，并落地 Android / ARCore 构建设置。
    /// 首次用 Unity 打开 unity/ 工程后，从菜单「现场巡检 / 初始化工程」执行。
    /// 脚本可重复执行（幂等）：已存在的配置不重复创建，重复的同类型 XR Loader 会被清理。
    /// 本脚本只搭骨架，不实现平面检测、标记放置或表单业务。
    /// </summary>
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/Inspection.unity";
        private const string AppIdentifier = "com.honlnk.arinspection";

        [MenuItem("现场巡检/初始化工程（场景 + Android + ARCore 配置）")]
        public static void RunAll()
        {
            EnsureFolders();
            ConfigurePlayerSettings();
            SwitchToAndroid();
            EnableARCoreLoader();
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 初始化完成：主场景已创建并加入 Build Settings，Android/ARCore 配置已落地。");
        }

        private static void EnsureFolders()
        {
            foreach (var folder in new[]
            {
                "Assets/Scenes",
                "Assets/Prefabs",
                "Assets/Scripts/AR",
                "Assets/Scripts/UI",
                "Assets/XR/Settings",
            })
            {
                Directory.CreateDirectory(folder);
            }
            AssetDatabase.Refresh();
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "honlnk";
            PlayerSettings.productName = "现场巡检问题标注";
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, AppIdentifier);

            // Android：IL2CPP + ARM64，目标 SDK 跟随编辑器默认。
            // 计划要求最低 API 24：Unity 6000.3 引擎下限 25（已记录偏差）；
            // 首次构建实测 ARCore 6.6 + Vulkan 要求 minSdk ≥ 29，随之上调（真机 Android 16 不受影响）。
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            // 入口必须是 Activity（UnityPlayerActivity）：自定义 Main Manifest 声明的就是它；
            // GameActivity 入口会把该 Activity 合并成 enabled=false 且不注入 GameActivity，App 无法启动
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

            // 局域网 HTTP 联调：仅开发构建允许明文流量（正式构建不允许）。
            // activeInputHandler 2=Both；insecureHttpOption 1=仅开发
            SetPlayerSetting("insecureHttpOption", 1);
            SetPlayerSetting("activeInputHandler", 2);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        }

        private static void SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }
        }

        private static void EnableARCoreLoader()
        {
            // 复刻 XRGeneralSettingsPerBuildTarget.GetOrCreate()（internal）的公开等价逻辑：
            // 先查 EditorBuildSettings 配置对象，再全工程找资产，都没有则创建。
            XRGeneralSettingsPerBuildTarget container;
            if (!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out container) || container == null)
            {
                var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (guids.Length > 0)
                {
                    container = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (container == null)
            {
                container = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(container, "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, container, true);
            }

            if (!container.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                container.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            }
            if (!container.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                container.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }
            var manager = container.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (manager == null)
            {
                Debug.LogWarning("[ProjectSetup] 无法创建 Android XRManagerSettings，请手动在 XR Plug-in Management 勾选 ARCore。");
                return;
            }

            // 通过反射定位 ARCoreLoader 类型，避免对 ARCore 插件类型的编译期硬依赖
            // （真实命名空间为 UnityEngine.XR.ARCore，Unity.XR.ARCore 为备选）
            var loaderType = FindType("UnityEngine.XR.ARCore.ARCoreLoader")
                ?? FindType("Unity.XR.ARCore.ARCoreLoader");
            if (loaderType == null)
            {
                Debug.LogWarning("[ProjectSetup] 未找到 ARCoreLoader 类型，请安装 com.unity.xr.arcore 后在 XR Plug-in Management 中手动勾选 ARCore。");
                return;
            }

            // TryAddLoader 按对象引用去重而不按类型去重，重复执行会累积同类型 Loader。
            // 这里按类型复用第一个已有 Loader，并移除多余项及其子资产，保证初始化幂等。
            XRLoader existing = null;
            var duplicates = new List<XRLoader>();
            foreach (var l in manager.activeLoaders)
            {
                if (l == null || l.GetType() != loaderType)
                {
                    continue;
                }
                if (existing == null)
                {
                    existing = l;
                }
                else
                {
                    duplicates.Add(l);
                }
            }
            foreach (var dup in duplicates)
            {
                if (!manager.TryRemoveLoader(dup))
                {
                    Debug.LogWarning($"[ProjectSetup] 移除重复 ARCore Loader 失败：{dup.name}");
                    continue;
                }
                AssetDatabase.RemoveObjectFromAsset(dup);
                UnityEngine.Object.DestroyImmediate(dup);
                EditorUtility.SetDirty(container);
            }
            if (duplicates.Count > 0)
            {
                Debug.Log($"[ProjectSetup] 已清理 {duplicates.Count} 个重复 ARCore Loader。");
            }
            if (existing != null)
            {
                return;
            }

            var loader = ScriptableObject.CreateInstance(loaderType) as XRLoader;
            if (loader == null)
            {
                Debug.LogWarning("[ProjectSetup] 创建 ARCore Loader 实例失败。");
                return;
            }
            loader.name = "ARCore Loader";
            AssetDatabase.AddObjectToAsset(loader, container.SettingsForBuildTarget(BuildTargetGroup.Android));
            if (!manager.TryAddLoader(loader))
            {
                Debug.LogWarning("[ProjectSetup] ARCore Loader 注册失败，请在 XR Plug-in Management 中手动勾选 ARCore。");
                return;
            }
            EditorUtility.SetDirty(container);
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 官方创建 AR Session 时同时挂会话与输入管理器（SceneUtils.CreateARSessionWithParent）；
            // 输入管理器是手持 AR 设备姿态进入 Input System 的前提（计划 005）
            var sessionGo = new GameObject("AR Session");
            sessionGo.AddComponent<ARSession>();
            sessionGo.AddComponent<ARInputManager>();

            var xrOriginGo = new GameObject("XR Origin");
            var xrOrigin = xrOriginGo.AddComponent<XROrigin>();
            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(xrOriginGo.transform, false);
            var cameraGo = new GameObject("Main Camera");
            var camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<ARCameraManager>();
            cameraGo.AddComponent<ARCameraBackground>();
            ConfigurePoseDriver(cameraGo.AddComponent<TrackedPoseDriver>());
            cameraGo.transform.SetParent(offsetGo.transform, false);
            cameraGo.tag = "MainCamera";
            xrOrigin.Camera = camera;
            xrOrigin.CameraFloorOffsetObject = offsetGo;

            var planeManager = xrOriginGo.AddComponent<ARPlaneManager>();
            // 骨架阶段只请求水平平面，且不指定平面可视化 Prefab（不伪造检测效果）
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            xrOriginGo.AddComponent<ARRaycastManager>();
            xrOriginGo.AddComponent<ARAnchorManager>();

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"保存场景失败：{ScenePath}");
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        /// <summary>
        /// 统一 AR 相机输入配置（计划 005），工程初始化与运行时搭建（SceneWiring）共用：
        /// AR Session 同对象有且仅有一个 ARInputManager；XR Origin 主相机只保留一个
        /// Input System 版 TrackedPoseDriver，删除旧版空间跟踪驱动与已弃用的 ARPoseDriver，
        /// 并按官方移动 AR 创建逻辑绑定手持 AR 设备位置/旋转。任一前置对象缺失直接抛错，
        /// 不再通过字符串查找旧组件并静默回退。
        /// </summary>
        public static void EnsureArcameraInput()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (session == null)
            {
                throw new InvalidOperationException("场景缺少 ARSession，无法配置 AR 输入管理器。");
            }
            var inputManagers = session.GetComponents<ARInputManager>();
            for (var i = 1; i < inputManagers.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(inputManagers[i]);
            }
            if (inputManagers.Length == 0)
            {
                session.gameObject.AddComponent<ARInputManager>();
            }

            var xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                throw new InvalidOperationException("场景缺少 XR Origin 或主相机，无法配置相机姿态驱动。");
            }
            var cameraGo = xrOrigin.Camera.gameObject;
            var driver = cameraGo.GetComponent<TrackedPoseDriver>();
            var staleComponents = new List<Component>();
            foreach (var component in cameraGo.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }
                var type = component.GetType();
                if (type == typeof(TrackedPoseDriver))
                {
                    if (driver == null)
                    {
                        driver = (TrackedPoseDriver)component;
                    }
                    else
                    {
                        staleComponents.Add(component); // 重复姿态驱动：只保留第一个
                    }
                    continue;
                }
                // 旧版空间跟踪驱动与已弃用 ARPoseDriver 按类型全名识别并删除，不与新驱动并存
                var fullName = type.FullName;
                if (fullName == "UnityEngine.SpatialTracking.TrackedPoseDriver"
                    || fullName == "UnityEngine.XR.ARFoundation.ARPoseDriver")
                {
                    staleComponents.Add(component);
                }
            }
            foreach (var stale in staleComponents)
            {
                UnityEngine.Object.DestroyImmediate(stale);
            }
            if (driver == null)
            {
                driver = cameraGo.AddComponent<TrackedPoseDriver>();
            }
            ConfigurePoseDriver(driver);
        }

        /// <summary>
        /// 与 AR Foundation 6.6.2 官方 XROriginCreateUtil.CreateARMainCamera 完全一致的驱动配置：
        /// 内嵌位置/旋转动作（随组件序列化，不建全局输入资产），每路各含 XRHMD 兼容绑定与
        /// 手持 AR 设备绑定；trackingType/updateType 保持组件默认（RotationAndPosition /
        /// UpdateAndBeforeRender），不自行发明分帧补偿或双驱动。
        /// </summary>
        private static void ConfigurePoseDriver(TrackedPoseDriver driver)
        {
            var positionAction = new InputAction(
                "Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotationAction = new InputAction(
                "Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
            driver.positionInput = new InputActionProperty(positionAction);
            driver.rotationInput = new InputActionProperty(rotationAction);
        }

        private static SerializedObject GetPlayerSettingsObject()
        {
            // PlayerSettings 无公开实例入口，加载其底层资产对象（编辑器工具的标准做法）
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            return (assets != null && assets.Length > 0) ? new SerializedObject(assets[0]) : null;
        }

        private static void SetPlayerSetting(string propertyPath, int value)
        {
            var so = GetPlayerSettingsObject();
            if (so == null)
            {
                Debug.LogWarning("[ProjectSetup] 无法加载 ProjectSettings.asset，跳过设置：" + propertyPath);
                return;
            }
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
            {
                Debug.LogWarning($"[ProjectSetup] 未找到 PlayerSettings 字段 {propertyPath}，跳过该项设置");
                return;
            }
            prop.intValue = value;
            so.ApplyModifiedProperties();
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }
    }
}
