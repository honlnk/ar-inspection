using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// 阶段 3 门禁自检（计划 001 的 SK-01/SK-02 可自动化部分）。
    /// 编辑器菜单运行只输出报告；批处理模式任一项失败以退出码 1 结束：
    /// Unity -batchmode -quit -projectPath &lt;unity 工程路径&gt;
    ///   -executeMethod Inspection.EditorTools.GateVerify.Run
    /// </summary>
    public static class GateVerify
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int _failures;

        [MenuItem("现场巡检/阶段门禁自检（SK-01/SK-02）")]
        public static void Run()
        {
            _failures = 0;
            Report.Clear();
            Report.AppendLine("[GateVerify] 阶段 3 门禁自检开始");

            VerifyScene();
            VerifyBuildSettings();
            VerifyPlayerSettings();
            VerifyARCoreLoader();
            VerifyManifest();
            VerifyRuntimeWiring();

            Report.AppendLine($"[GateVerify] 结果：{(_failures == 0 ? "全部通过" : $"{_failures} 项失败")}");
            Debug.Log(Report.ToString());

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(_failures == 0 ? 0 : 1);
            }
        }

        private static void Check(bool pass, string description)
        {
            Report.AppendLine($"[GateVerify] {(pass ? "PASS" : "FAIL")} - {description}");
            if (!pass)
            {
                _failures++;
            }
        }

        private static void VerifyScene()
        {
            const string scenePath = "Assets/Scenes/Inspection.unity";
            Check(File.Exists(scenePath), $"主场景文件存在：{scenePath}");
            if (!File.Exists(scenePath))
            {
                return;
            }
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            Check(session != null, "场景含 ARSession");

            var xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            Check(xrOrigin != null, "场景含 XROrigin");
            if (xrOrigin != null)
            {
                Check(xrOrigin.Camera != null, "XROrigin 已绑定 AR 相机");
                if (xrOrigin.Camera != null)
                {
                    var cameraGo = xrOrigin.Camera.gameObject;
                    Check(cameraGo.GetComponent<ARCameraManager>() != null, "AR 相机含 ARCameraManager");
                    Check(cameraGo.GetComponent<ARCameraBackground>() != null, "AR 相机含 ARCameraBackground");
                }
            }

            // 计划 005：旧「有名字相近的姿态组件」检查升级为输入链路精确检查
            var inputFailures = GetArcameraInputFailures();
            foreach (var failure in inputFailures)
            {
                Check(false, failure);
            }
            Check(inputFailures.Count == 0,
                "AR 输入链路精确检查：输入管理器唯一、Input System 姿态驱动唯一、无旧驱动、手持 AR 位置/旋转绑定齐全");

            var planeManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>();
            Check(planeManager != null && planeManager.requestedDetectionMode == PlaneDetectionMode.Horizontal,
                "ARPlaneManager 存在且仅请求水平平面");
            Check(UnityEngine.Object.FindFirstObjectByType<ARRaycastManager>() != null, "场景含 ARRaycastManager");
            Check(UnityEngine.Object.FindFirstObjectByType<ARAnchorManager>() != null, "场景含 ARAnchorManager");

            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);            var overlay = canvases.FirstOrDefault(c => c.renderMode == RenderMode.ScreenSpaceOverlay);
            Check(overlay != null && overlay.GetComponent<GraphicRaycaster>() != null,
                "场景含 ScreenSpaceOverlay Canvas 与 GraphicRaycaster");
            var eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            Check(eventSystem != null && eventSystem.GetComponent<StandaloneInputModule>() != null,
                "场景含 EventSystem 与 StandaloneInputModule");
        }

        private static void VerifyBuildSettings()
        {
            Check(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android, "当前构建平台为 Android");
            var sceneEnabled = EditorBuildSettings.scenes.Any(s => s.enabled && s.path == "Assets/Scenes/Inspection.unity");
            Check(sceneEnabled, "Build Settings 含已启用的 Inspection 场景");
        }

        private static void VerifyPlayerSettings()
        {
            var android = NamedBuildTarget.Android;
            Check(PlayerSettings.GetScriptingBackend(android) == ScriptingImplementation.IL2CPP,
                "Android 脚本后端为 IL2CPP");
            Check(PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64,
                "Android 目标架构仅 ARM64");
            // 计划要求 API 24 → 引擎下限 25；首次构建实测 ARCore 6.6 + Vulkan 要求 ≥ 29（计划文档记录偏差）
            Check(PlayerSettings.Android.minSdkVersion == AndroidSdkVersions.AndroidApiLevel29,
                "Android 最低 API 为 29（ARCore 6.6 + Vulkan 要求，计划 24/25 的已记录偏差）");
            // 以下两项在 6000.3 无公开读取 API，用 SerializedObject 按序列化字段名读取
            Check(GetPlayerSetting("activeInputHandler") == 2, "输入处理为 Both");
            Check(GetPlayerSetting("insecureHttpOption") == 1, "明文 HTTP 仅限开发构建");
            Check(PlayerSettings.GetApplicationIdentifier(android) == "com.honlnk.arinspection",
                "Android 包名正确");
            Check(PlayerSettings.Android.applicationEntry == AndroidApplicationEntry.Activity,
                "Android 入口为 Activity（UnityPlayerActivity，与自定义 Main Manifest 一致；GameActivity 入口会使启动器被禁用）");
            Check(PlayerSettings.defaultInterfaceOrientation == UIOrientation.Portrait,
                "屏幕方向锁定竖屏 Portrait（计划 004 4.1）");
        }

        /// <summary>计划 004 新增：运行时脚本挂载、Prefab 引用与 UI 骨架（打开场景后检查）。</summary>
        private static void VerifyRuntimeWiring()
        {
            // 依赖 VerifyScene 已打开主场景
            var placement = UnityEngine.Object.FindFirstObjectByType<Inspection.AR.PlacementController>(FindObjectsInactive.Include);
            Check(placement != null, "场景含 PlacementController（挂载在 XR Origin）");
            if (placement != null)
            {
                var so = new SerializedObject(placement);
                Check(so.FindProperty("markerPrefab").objectReferenceValue != null,
                    "PlacementController 已引用 IssueMarker.prefab");
                Check(so.FindProperty("formController").objectReferenceValue != null
                    && so.FindProperty("reportButton").objectReferenceValue != null
                    && so.FindProperty("settingsPanel").objectReferenceValue != null,
                    "PlacementController 已接线表单/跟随按钮/设置面板");
                Check(so.FindProperty("diagnostics").objectReferenceValue != null,
                    "PlacementController 已接线开发构建放置诊断记录器");
            }

            var planeManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>();
            Check(planeManager != null && planeManager.planePrefab != null,
                "ARPlaneManager 已引用 PlaneVisualizer.prefab");

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                var appUi = canvas.transform.Find("AppUI");
                Check(appUi != null
                    && appUi.Find("FormPanelRoot") != null
                    && appUi.Find("SettingsPanelRoot") != null
                    && appUi.Find("ReportButtonRoot") != null
                    && appUi.Find("ToastRoot") != null,
                    "AppUI 骨架齐全（表单/设置/跟随按钮/Toast）");

                if (appUi != null)
                {
                    var formRoot = appUi.Find("FormPanelRoot");
                    Check(formRoot != null && formRoot.Find("Mask") != null && formRoot.Find("FormCard") != null,
                        "表单面板含全屏蒙层与底部卡片");
                    var settingsRoot = appUi.Find("SettingsPanelRoot");
                    Check(settingsRoot != null && settingsRoot.Find("Mask") != null && settingsRoot.Find("SettingsCard") != null,
                        "设置面板含全屏蒙层与居中弹窗");
                }
            }
        }

        /// <summary>
        /// 计划 005：AR 输入链路精确检查（作用于当前已打开场景），返回失败描述；空列表 = 通过。
        /// 阶段门禁与 SceneWiring 保存后复检共用本方法，避免两套口径漂移。覆盖：
        /// AR Session 同对象恰一个 ARInputManager；主相机恰一个 Input System 版姿态驱动；
        /// 不存在旧版空间跟踪驱动、已弃用 ARPoseDriver 或重复驱动；位置/旋转输入动作
        /// 随场景反序列化，且 XRHMD 兼容绑定与手持 AR 设备（HandheldARInputDevice）绑定齐全。
        /// </summary>
        public static List<string> GetArcameraInputFailures()
        {
            var failures = new List<string>();
            var session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (session == null)
            {
                failures.Add("场景缺少 ARSession");
                return failures;
            }
            var inputManagerCount = session.GetComponents<ARInputManager>().Length;
            if (inputManagerCount == 0)
            {
                failures.Add("AR Session 同对象缺少 AR 输入管理器（ARInputManager）");
            }
            else if (inputManagerCount > 1)
            {
                failures.Add($"AR Session 同对象有 {inputManagerCount} 个 ARInputManager（应恰 1 个）");
            }

            var xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                failures.Add("场景缺少 XR Origin 或其主相机");
                return failures;
            }
            var cameraGo = xrOrigin.Camera.gameObject;

            var drivers = cameraGo.GetComponents<TrackedPoseDriver>();
            if (drivers.Length == 0)
            {
                failures.Add("主相机缺少 Input System 版姿态驱动（UnityEngine.InputSystem.XR.TrackedPoseDriver）");
            }
            else if (drivers.Length > 1)
            {
                failures.Add($"主相机有 {drivers.Length} 个 Input System 姿态驱动（应恰 1 个）");
            }
            foreach (var component in cameraGo.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }
                var fullName = component.GetType().FullName;
                if (fullName == "UnityEngine.SpatialTracking.TrackedPoseDriver")
                {
                    failures.Add("主相机仍存在旧版空间跟踪姿态驱动（SpatialTracking.TrackedPoseDriver）");
                }
                else if (fullName == "UnityEngine.XR.ARFoundation.ARPoseDriver")
                {
                    failures.Add("主相机仍存在已弃用的 ARPoseDriver（AR Foundation 6 已弃用）");
                }
            }

            if (drivers.Length > 0)
            {
                failures.AddRange(GetPoseBindingFailures(
                    "位置", drivers[0].positionInput, "<XRHMD>/centerEyePosition", "<HandheldARInputDevice>/devicePosition"));
                failures.AddRange(GetPoseBindingFailures(
                    "旋转", drivers[0].rotationInput, "<XRHMD>/centerEyeRotation", "<HandheldARInputDevice>/deviceRotation"));
            }
            return failures;
        }

        /// <summary>
        /// 单路姿态输入的绑定检查。Input System 1.20 序列化层没有按绑定禁用的标志位，
        /// 「绑定存在且启用」落实为：动作实例随场景反序列化可读（非空），且两路绑定路径
        /// 都出现在动作的 bindings 列表中。
        /// </summary>
        private static List<string> GetPoseBindingFailures(
            string label, InputActionProperty input, string hmdPath, string handheldPath)
        {
            var failures = new List<string>();
            var action = input.action;
            if (action == null)
            {
                failures.Add($"姿态驱动{label}输入动作未随场景序列化（读取为空）");
                return failures;
            }
            foreach (var path in new[] { hmdPath, handheldPath })
            {
                var found = false;
                foreach (var binding in action.bindings)
                {
                    if (binding.path == path)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var owner = path == handheldPath ? "手持 AR 设备" : "头显兼容";
                    failures.Add($"姿态驱动{label}输入缺少{owner}绑定 {path}");
                }
            }
            return failures;
        }

        private static int GetPlayerSetting(string propertyPath)
        {
            // PlayerSettings 无公开实例入口，加载其底层资产对象（编辑器工具的标准做法）
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                return -1;
            }
            var prop = new SerializedObject(assets[0]).FindProperty(propertyPath);
            return prop != null ? prop.intValue : -1;
        }

        private static void VerifyARCoreLoader()
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            var arcoreLoaders = general != null && general.Manager != null
                ? general.Manager.activeLoaders
                    .Where(l => l != null && l.GetType().Name.Contains("ARCoreLoader")).ToList()
                : new List<XRLoader>();
            Check(arcoreLoaders.Count >= 1, "Android 构建组已启用 ARCore Loader");
            Check(arcoreLoaders.Count == 1,
                $"ARCore Loader 无同类型重复注册（实际 {arcoreLoaders.Count} 个；重复会随构建序列化进 XR 配置）");
        }

        private static void VerifyManifest()
        {
            const string manifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
            Check(File.Exists(manifestPath),
                $"已启用自定义 Main Manifest：{manifestPath}（由标准路径下的文件启用；勿用 useCustomMainManifest 序列化字段判断）");
            if (File.Exists(manifestPath))
            {
                var content = File.ReadAllText(manifestPath);
                Check(content.Contains("com.google.ar.core"), "Manifest 含 com.google.ar.core 包可见性声明");
                Check(content.Contains("android.permission.INTERNET"), "Manifest 含 INTERNET 权限");
            }
        }
    }
}
