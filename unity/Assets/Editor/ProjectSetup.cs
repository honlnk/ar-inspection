using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// 首次用 Unity 打开 unity/ 工程后，从菜单「现场巡检 / 初始化工程」执行一次即可。
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
            // useCustomMainManifest 属于按平台分块的设置，必须在切到 Android 之后写入，
            // 否则平台切换会以默认值重建 Android 块，覆盖先前设置
            SetPlayerSetting("useCustomMainManifest", 1);
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
            // 计划要求最低 API 24，但 Unity 6000.3 引擎最低仅支持 API 25（Android 7.1），
            // 已在计划文档施工日志记录该偏差；真机 Android 16 不受影响。
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // 局域网 HTTP 联调：仅开发构建允许明文流量（正式构建不允许）。
            // 以下两项为全局设置，按 ProjectSettings.asset 序列化字段名直写：
            // activeInputHandler 2=Both；insecureHttpOption 1=仅开发
            // useCustomMainManifest 按平台分块存储，须在切换到 Android 之后设置（见 RunAll）
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

            // 通过反射创建 ARCoreLoader，避免对 ARCore 插件类型的编译期硬依赖
            // （真实命名空间为 UnityEngine.XR.ARCore，Unity.XR.ARCore 为备选）
            var loaderType = FindType("UnityEngine.XR.ARCore.ARCoreLoader")
                ?? FindType("Unity.XR.ARCore.ARCoreLoader");
            if (loaderType == null)
            {
                Debug.LogWarning("[ProjectSetup] 未找到 ARCoreLoader 类型，请安装 com.unity.xr.arcore 后在 XR Plug-in Management 中手动勾选 ARCore。");
                return;
            }
            var loader = ScriptableObject.CreateInstance(loaderType) as XRLoader;
            if (loader != null)
            {
                loader.name = "ARCore Loader";
                AssetDatabase.AddObjectToAsset(loader, container.SettingsForBuildTarget(BuildTargetGroup.Android));
                manager.TryAddLoader(loader);
                EditorUtility.SetDirty(container);
            }
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var sessionGo = new GameObject("AR Session");
            sessionGo.AddComponent<ARSession>();

            var xrOriginGo = new GameObject("XR Origin");
            var xrOrigin = xrOriginGo.AddComponent<XROrigin>();
            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(xrOriginGo.transform, false);
            var cameraGo = new GameObject("Main Camera");
            var camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<ARCameraManager>();
            cameraGo.AddComponent<ARCameraBackground>();
            AddPoseDriver(cameraGo);
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

        private static void AddPoseDriver(GameObject cameraGo)
        {
            // 优先 AR Foundation 自带的 AR Pose Driver；找不到时退回 SpatialTracking 的 TrackedPoseDriver
            var poseDriverType = FindType("UnityEngine.XR.ARPoseDriver")
                ?? FindType("UnityEngine.SpatialTracking.TrackedPoseDriver");
            if (poseDriverType == null)
            {
                Debug.LogWarning("[ProjectSetup] 未找到相机姿态驱动组件，请手动为 Main Camera 添加 AR Pose Driver。");
                return;
            }
            cameraGo.AddComponent(poseDriverType);
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
