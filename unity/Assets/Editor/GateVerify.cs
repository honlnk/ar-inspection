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

        /// <summary>
        /// 全屏面板的 panelRoot 必须指向 Full() 的根节点，而不是 SafeAreaContent：
        /// 面板关闭走 panelRoot.SetActive(false)，指到内容层时外层容器不会关闭，
        /// 仍激活的全屏背景会盖住更下层的面板（表现为登录页只剩背景色、控件全不可见）。
        /// </summary>
        private static void CheckPanelRootsOnBackgroundRoot(Transform appUi)
        {
            var panels = new[]
            {
                (Root: "LoginPanelRoot", Controller: typeof(Inspection.UI.LoginPanelController)),
                (Root: "HistoryPanelRoot", Controller: typeof(Inspection.UI.HistoryPanelController)),
                (Root: "SettingsPanelRoot", Controller: typeof(Inspection.UI.SettingsPanelController)),
                (Root: "FormPanelRoot", Controller: typeof(Inspection.UI.IssueFormController)),
            };

            foreach (var panel in panels)
            {
                var expected = appUi.Find(panel.Root);
                var controller = UnityEngine.Object.FindFirstObjectByType(panel.Controller, FindObjectsInactive.Include)
                    as Component;
                if (expected == null || controller == null)
                {
                    Check(false, $"面板 {panel.Root} 与其控制器存在，可校验显隐目标");
                    continue;
                }

                var so = new SerializedObject(controller);
                var target = so.FindProperty("panelRoot")?.objectReferenceValue as GameObject;
                // 根节点必须直接挂在 AppUI 下（全屏容器），且自身带 SafeAreaFitter 的兄弟内容层
                Check(target == expected.gameObject
                    && expected.parent == appUi
                    && expected.Find("SafeAreaContent") != null,
                    $"面板 {panel.Root} 的显隐目标为全屏根节点（非 SafeAreaContent 内容层）");
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
                Check(so.FindProperty("loginPanel").objectReferenceValue != null
                    && so.FindProperty("historyPanel").objectReferenceValue != null,
                    "PlacementController 已接线登录/历史面板（打开期间冻结放置）");
                var materials = so.FindProperty("priorityMaterials");
                var materialsOk = materials != null && materials.isArray && materials.arraySize == 3;
                if (materialsOk)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        materialsOk &= materials.GetArrayElementAtIndex(i).objectReferenceValue != null;
                    }
                }
                Check(materialsOk, "PlacementController 已引用三份优先级共享材质（中/低/高，计划 006 4.5）");
            }

            var planeManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>();
            Check(planeManager != null && planeManager.planePrefab != null,
                "ARPlaneManager 已引用 PlaneVisualizer.prefab");

            // 计划 006：三份优先级材质资产存在（低=绿 / 中=橙 / 高=红）
            Check(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/IssueMarker-Medium.mat") != null
                && AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/IssueMarker-Low.mat") != null
                && AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/IssueMarker-High.mat") != null,
                "优先级三色材质资产齐全（IssueMarker-Medium/Low/High.mat）");

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
                    Check(formRoot != null && formRoot.Find("SafeAreaContent/Mask") != null
                        && formRoot.Find("SafeAreaContent/FormCard") != null,
                        "表单面板含全屏蒙层与底部卡片");
                    var settingsRoot = appUi.Find("SettingsPanelRoot");
                    Check(settingsRoot != null && settingsRoot.Find("SafeAreaContent/SettingsCard/Viewport/Content/ArSection/ResetButton") != null,
                        "设置面板为全屏滚动页且 AR 重置入口位于 AR 会话区");

                    // 计划 006：登录全屏面板与历史列表面板
                    var loginRoot = appUi.Find("LoginPanelRoot");
                    Check(loginRoot != null && loginRoot.Find("SafeAreaContent/LoginScroll/Viewport/Content/LoginCard") != null
                        && loginRoot.GetComponent<Inspection.UI.SafeAreaFitter>() != null,
                        "登录为安全区全屏可滚动卡片");
                    var quickLoginButton = loginRoot != null
                        ? loginRoot.Find("SafeAreaContent/LoginScroll/Viewport/Content/LoginCard/QuickLoginButton")
                        : null;
                    Check(quickLoginButton != null
                        && quickLoginButton.GetComponent<Button>() != null
                        && quickLoginButton.Find("Label")?.GetComponent<Text>()?.text == "开发快捷登录（巡检员）",
                        "登录页含开发快捷登录入口（仅开发构建显示）");
                    var historyRoot = appUi.Find("HistoryPanelRoot");
                    Check(historyRoot != null
                        && historyRoot.Find("SafeAreaContent/Header") != null
                        && historyRoot.Find("SafeAreaContent/ScrollView/Viewport/Content/HistoryItemTemplate/PriorityTag/PriorityText") != null
                        && historyRoot.Find("SafeAreaContent/ScrollView/Viewport/Content/HistoryItemTemplate/StatusTag/StatusText") != null,
                        "历史面板存在且含头部与条目模板");

                    // 层级顺序：设置在登录之上（登录前可配置后端），Toast 最上
                    var order = new[] { "FormPanelRoot", "ReportButtonRoot", "TopBar", "FieldGuideRoot", "DevDebugOverlay",
                        "HistoryPanelRoot", "LoginPanelRoot", "SettingsPanelRoot", "ToastRoot" };
                    var orderOk = appUi.childCount == order.Length;
                    if (orderOk)
                    {
                        for (var i = 0; i < order.Length; i++)
                        {
                            orderOk &= appUi.GetChild(i).name == order[i];
                        }
                    }
                    Check(orderOk, "AppUI 渲染层级正确（全屏面板盖住主界面，设置盖住登录，Toast 最上）");

                    // 面板显隐必须落在带背景的根节点上：content 是 SafeAreaContent 子节点，
                    // 若接线到它，关闭面板只关内容、外层不透明背景仍会盖住下层（登录页整屏空白）。
                    CheckPanelRootsOnBackgroundRoot(appUi);
                }
            }

            // 计划 006：登录/历史/设置控制器接线
            var loginPanel = UnityEngine.Object.FindFirstObjectByType<Inspection.UI.LoginPanelController>(FindObjectsInactive.Include);
            if (loginPanel != null)
            {
                var so = new SerializedObject(loginPanel);
                Check(so.FindProperty("apiClient").objectReferenceValue != null
                    && so.FindProperty("usernameInput").objectReferenceValue != null
                    && so.FindProperty("passwordInput").objectReferenceValue != null
                    && so.FindProperty("loginButton").objectReferenceValue != null
                    && so.FindProperty("noticeText").objectReferenceValue != null,
                    "登录面板已接线认证客户端与输入控件");
                Check(so.FindProperty("quickLoginButton").objectReferenceValue != null,
                    "登录面板已接线开发快捷登录按钮");
            }
            else
            {
                Check(false, "场景含 LoginPanelController");
            }

            var historyPanel = UnityEngine.Object.FindFirstObjectByType<Inspection.UI.HistoryPanelController>(FindObjectsInactive.Include);
            if (historyPanel != null)
            {
                var so = new SerializedObject(historyPanel);
                Check(so.FindProperty("apiClient").objectReferenceValue != null
                    && so.FindProperty("authClient").objectReferenceValue != null
                    && so.FindProperty("loginPanel").objectReferenceValue != null
                    && so.FindProperty("listContent").objectReferenceValue != null
                    && so.FindProperty("itemTemplate").objectReferenceValue != null,
                    "历史面板已接线列表客户端/认证/登录面板与列表模板");
            }
            else
            {
                Check(false, "场景含 HistoryPanelController");
            }

            var settingsPanel = UnityEngine.Object.FindFirstObjectByType<Inspection.UI.SettingsPanelController>(FindObjectsInactive.Include);
            if (settingsPanel != null)
            {
                var so = new SerializedObject(settingsPanel);
                Check(settingsPanel != null && so.FindProperty("fullDiagnosticsToggle").objectReferenceValue != null,
                    "设置面板已接线「完整诊断」开关（计划 006 4.8）");
            }

            var root = UnityEngine.Object.FindFirstObjectByType<Canvas>()?.transform.Find("AppUI");
            if (root == null)
            {
                return;
            }
            Check(root.GetComponentsInChildren<Text>(true).All(text => text.font != null),
                "所有 UI 文字均持有有效字体引用（防止真机空文字）");
            Check(root.Find("TopBar/SafeAreaContent/Bar/ResetButton") == null, "顶部栏已移除 AR 重置入口（仅保留在设置 AR 会话区）");
            var priority = root.Find("FormPanelRoot/SafeAreaContent/FormCard/KeyboardArea/Viewport/Content/PrioritySelector");
            Check(priority != null && priority.GetComponent<Inspection.UI.PrioritySelector>() != null
                && priority.childCount == 3 && priority.GetComponentsInChildren<Button>().Length == 3,
                "表单为低/中/高三项可见分段选择，旧下拉已移除");
            var formScroll = root.Find("FormPanelRoot/SafeAreaContent/FormCard/KeyboardArea");
            Check(formScroll != null && formScroll.GetComponent<ScrollRect>() != null
                && formScroll.GetComponent<Inspection.UI.KeyboardInset>() != null,
                "表单含滚动与软键盘避让，按钮上方保留错误区");
            var resetButton = root.Find("SettingsPanelRoot/SafeAreaContent/SettingsCard/Viewport/Content/ArSection/ResetButton");
            Check(resetButton != null && resetButton.GetComponent<Inspection.UI.SessionResetButton>() != null,
                "AR 重置保留二次文字确认");
            var guide = root.Find("FieldGuideRoot");
            Check(guide != null && guide.GetComponent<Inspection.UI.FieldGuideView>() != null
                && guide.GetComponent<Inspection.UI.SafeAreaFitter>() != null,
                "AR 底部任务提示与安全区已接线");
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
