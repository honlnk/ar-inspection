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
                    var hasPoseDriver = cameraGo.GetComponents<Component>()
                        .Any(c => c != null && (c.GetType().Name == "ARPoseDriver" || c.GetType().Name == "TrackedPoseDriver"));
                    Check(hasPoseDriver, "AR 相机含姿态驱动组件（ARPoseDriver 或 TrackedPoseDriver）");
                }
            }

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
            // 计划要求 API 24；Unity 6000.3 引擎最低仅支持 API 25（已在计划文档记录偏差）
            Check(PlayerSettings.Android.minSdkVersion == AndroidSdkVersions.AndroidApiLevel25,
                "Android 最低 API 为 25（引擎下限，计划 24 的已记录偏差）");
            // 以下两项在 6000.3 无公开读取 API，用 SerializedObject 按序列化字段名读取
            Check(GetPlayerSetting("activeInputHandler") == 2, "输入处理为 Both");
            Check(GetPlayerSetting("insecureHttpOption") == 1, "明文 HTTP 仅限开发构建");
            Check(PlayerSettings.GetApplicationIdentifier(android) == "com.honlnk.arinspection",
                "Android 包名正确");
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
