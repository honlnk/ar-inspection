using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using Inspection.UI;

namespace Inspection.EditorTools
{
    /// <summary>
    /// 场景接线：创建标记/平面可视化 Prefab 与材质（计划 006 起标记为三份优先级共享材质：
    /// 低=绿 / 中=橙 / 高=红）、搭建全部 UI（顶部栏、Toast、跟随上报按钮、表单面板、
    /// 设置面板（含完整诊断开关）、登录全屏面板、历史列表全屏面板，均含全屏蒙层或纯色背景）、
    /// 挂运行时脚本并接线。幂等：重复执行时先销毁既有 AppUI 重建，Prefab 与材质存在则覆盖重存，
    /// 旧的单色 IssueMarker.mat 首次迁移时删除。
    /// 计划 005 起，执行前先经 ProjectSetup.EnsureArcameraInput 统一 AR 输入链路
    /// （输入管理器 + Input System 姿态驱动绑定），保存后复检，避免重建内容时遗漏相机配置。
    /// 菜单「现场巡检 / 搭建运行时内容」，批处理 -executeMethod 同名可用。
    /// </summary>
    public static class SceneWiring
    {
        private const string ScenePath = "Assets/Scenes/Inspection.unity";
        private const string MarkerPrefabPath = "Assets/Prefabs/IssueMarker.prefab";
        private const string PlanePrefabPath = "Assets/Prefabs/PlaneVisualizer.prefab";
        private const string LegacyMarkerMaterialPath = "Assets/Materials/IssueMarker.mat";

        // 配色（深色高对比，AR 实景上可读）
        private static readonly Color PanelBg = new Color(0.09f, 0.11f, 0.16f, 0.96f);
        private static readonly Color SolidBg = new Color(0.07f, 0.09f, 0.13f, 1f);
        private static readonly Color TopBarBg = new Color(0.05f, 0.06f, 0.09f, 0.85f);
        private static readonly Color ToastBg = new Color(0.05f, 0.06f, 0.09f, 0.92f);
        private static readonly Color InputBg = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color AccentBg = new Color(0.15f, 0.39f, 0.92f, 1f);
        private static readonly Color CancelBg = new Color(0.22f, 0.25f, 0.32f, 1f);
        private static readonly Color TextMain = Color.white;
        private static readonly Color TextSub = new Color(0.72f, 0.75f, 0.80f, 1f);
        private static readonly Color TextPlaceholder = new Color(1f, 1f, 1f, 0.4f);
        private static readonly Color TextWarning = new Color(1f, 0.72f, 0.30f, 1f);

        [MenuItem("现场巡检/搭建运行时内容（Prefab + UI + 脚本接线）")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 计划 005：搭建前先统一 AR 输入链路（AR 输入管理器 + Input System 姿态驱动绑定），
            // 与工程初始化共用 ProjectSetup.EnsureArcameraInput，不能只重建 UI 而遗漏相机配置
            ProjectSetup.EnsureArcameraInput();

            var markerMaterials = BuildMarkerMaterials();
            var markerPrefab = BuildMarkerPrefab(markerMaterials);
            var planePrefab = BuildPlaneVisualizerPrefab();
            WirePlaneManager(planePrefab);

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                throw new InvalidOperationException("场景缺少 Canvas，先执行「初始化工程」");
            }
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            UnityWorkspaceUi.Font = font;
            BuildAppUi(canvas.transform, font, markerPrefab, markerMaterials);

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            // 计划 005：保存后按阶段门禁同口径复检 AR 输入链路，失败立即中止并保留失败信息，
            // 不生成"看似完整"的场景
            var inputFailures = GateVerify.GetArcameraInputFailures();
            if (inputFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    "AR 输入链路保存后复检失败：" + string.Join("；", inputFailures));
            }
            Debug.Log("[SceneWiring] AR 输入链路复检通过：输入管理器、姿态驱动与绑定均已写入场景。");
            Debug.Log("[SceneWiring] 运行时内容搭建完成：三色标记材质、登录/历史面板与全部接线已保存。");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        // ---------- Prefab 与材质 ----------

        /// <summary>优先级共享材质路径：下标与 IssueFormController.PriorityValues 一致（medium/low/high）。</summary>
        private static readonly string[] MarkerMaterialPaths =
        {
            "Assets/Materials/IssueMarker-Medium.mat",
            "Assets/Materials/IssueMarker-Low.mat",
            "Assets/Materials/IssueMarker-High.mat",
        };

        /// <summary>计划 006 4.5 三色：中=橙（默认）/ 低=绿 / 高=红，均为共享材质不实例化。</summary>
        private static Material[] BuildMarkerMaterials()
        {
            Directory.CreateDirectory("Assets/Materials");
            var colors = new[]
            {
                new Color(0.94f, 0.55f, 0.14f, 1f), // medium 橙
                new Color(0.22f, 0.72f, 0.35f, 1f), // low 绿
                new Color(0.90f, 0.22f, 0.22f, 1f), // high 红
            };
            var emissions = new[]
            {
                new Color(0.55f, 0.30f, 0.05f),
                new Color(0.10f, 0.45f, 0.15f),
                new Color(0.55f, 0.08f, 0.08f),
            };
            var materials = new Material[MarkerMaterialPaths.Length];
            for (var i = 0; i < MarkerMaterialPaths.Length; i++)
            {
                var mat = LoadOrCreateMaterial(MarkerMaterialPaths[i], "Standard", colors[i]);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emissions[i]);
                EditorUtility.SetDirty(mat);
                materials[i] = mat;
            }

            // 首次迁移：删除计划 004 的单色材质（预制体已改用 Medium 作为默认）
            if (File.Exists(LegacyMarkerMaterialPath))
            {
                AssetDatabase.DeleteAsset(LegacyMarkerMaterialPath);
            }
            return materials;
        }

        private static GameObject BuildMarkerPrefab(Material[] markerMaterials)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "IssueMarker";
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>()); // AR 射线不用碰撞体
            cube.transform.localScale = Vector3.one * 0.15f;
            // Prefab 默认材质=中（表单默认优先级 medium）；放置/切换时按优先级覆盖 sharedMaterial
            cube.GetComponent<MeshRenderer>().sharedMaterial = markerMaterials[0];
            var prefab = PrefabUtility.SaveAsPrefabAsset(cube, MarkerPrefabPath);
            UnityEngine.Object.DestroyImmediate(cube);
            return prefab;
        }

        private static GameObject BuildPlaneVisualizerPrefab()
        {
            var planeMat = LoadOrCreateMaterial("Assets/Materials/PlaneVisualizer.mat", "Sprites/Default",
                new Color(0.25f, 0.85f, 1.00f, 0.45f));
            var lineMat = LoadOrCreateMaterial("Assets/Materials/PlaneBoundaryLine.mat", "Sprites/Default",
                new Color(0.40f, 1.00f, 1.00f, 1f));

            var root = new GameObject("PlaneVisualizer");
            root.AddComponent<MeshFilter>();
            var meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = planeMat;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            root.AddComponent<ARPlaneMeshVisualizer>(); // RequireComponent 自动带 ARPlane

            var lineGo = new GameObject("Boundary");
            lineGo.transform.SetParent(root.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.material = lineMat;
            line.loop = true;
            line.useWorldSpace = false;
            line.widthMultiplier = 0.016f;
            line.positionCount = 0;

            var boundary = root.AddComponent<Inspection.AR.PlaneBoundaryVisualizer>();
            SetRef(boundary, "lineRenderer", line);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlanePrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static void WirePlaneManager(GameObject planePrefab)
        {
            var planeManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>();
            if (planeManager == null)
            {
                throw new InvalidOperationException("场景缺少 ARPlaneManager");
            }
            planeManager.planePrefab = planePrefab;
            EditorUtility.SetDirty(planeManager);
        }

        private static Material LoadOrCreateMaterial(string path, string shaderName, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shaderName)) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- UI ----------

        private static void BuildAppUi(Transform canvasTransform, Font font, GameObject markerPrefab,
            Material[] markerMaterials)
        {
            // 幂等：整体重建 AppUI
            var old = canvasTransform.Find("AppUI");
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            var appUi = new GameObject("AppUI", typeof(RectTransform));
            Stretch(appUi.GetComponent<RectTransform>(), canvasTransform);
            appUi.AddComponent<Inspection.AppFrameRate>(); // 常激活根上：限帧 30fps 降发热

            // 层级通过末尾统一排序控制（自下而上）：表单 → 跟随按钮 → 顶部栏 → 调试条 →
            // 历史面板 → 登录面板 → 设置面板（可在登录界面之上配置后端地址）→ Toast
            var toast = BuildToast(appUi.transform, font);
            var formController = BuildFormPanel(appUi.transform, font, out var titleInput,
                out var descriptionInput, out var prioritySelector);
            var reportButton = BuildReportButton(appUi.transform, font);
            var settingsController = BuildSettingsPanel(appUi.transform, font);
            var loginController = BuildLoginPanel(appUi.transform, font, settingsController);
            var historyController = BuildHistoryPanel(appUi.transform, font, loginController);
            var historyButton = BuildTopBar(appUi.transform, font, settingsController);
            var overlay = BuildDevOverlay(appUi.transform, font);

            // 交叉接线（面板/按钮 → 控制器）
            SetRef(formController, "toast", toast);
            SetRef(formController, "apiClient", ((Component)formController).GetComponent<Inspection.API.IssueApiClient>());
            SetRef(formController, "loginPanel", loginController);
            SetRef(reportButton, "formController", formController);
            UnityEventTools.AddVoidPersistentListener(
                ((Component)reportButton).GetComponentInChildren<Button>().onClick, reportButton.OnReportButtonClicked);
            UnityEventTools.AddVoidPersistentListener(titleInput.onValueChanged, formController.OnTitleChanged);
            // 系统返回收起软键盘会把输入回滚（uGUI wasCanceled）：缓存已输入内容并在结束编辑时恢复
            UnityEventTools.AddStringPersistentListener(titleInput.onEndEdit, formController.OnTitleEndEdit, string.Empty);
            UnityEventTools.AddVoidPersistentListener(descriptionInput.onValueChanged, formController.OnDescriptionChanged);
            UnityEventTools.AddStringPersistentListener(descriptionInput.onEndEdit, formController.OnDescriptionEndEdit, string.Empty);
            // 优先级分段按钮变化时只替换草稿材质（几何/锚点/坐标仍由 PlacementController 管理）。
            UnityEventTools.AddIntPersistentListener(prioritySelector.onValueChanged, formController.OnPriorityChanged, 0);
            BuildFieldGuide(appUi.transform, font);
            var formError = appUi.transform.Find("FormPanelRoot/SafeAreaContent/FormCard/KeyboardArea/Viewport/Content/ErrorText");
            if (formError == null)
            {
                throw new InvalidOperationException("表单缺少错误提示区");
            }
            SetRef(formController, "errorText", formError.GetComponent<Text>());


            // 顶部栏：历史入口（计划 006 4.6）
            UnityEventTools.AddVoidPersistentListener(historyButton.onClick, historyController.Open);

            // 放置控制器挂到 XR Origin（同物体已有射线/锚点管理器）
            var xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                throw new InvalidOperationException("场景缺少 XR Origin");
            }
            var placement = xrOrigin.GetComponent<Inspection.AR.PlacementController>();
            if (placement == null)
            {
                placement = xrOrigin.gameObject.AddComponent<Inspection.AR.PlacementController>();
            }
            SetRef(placement, "formController", formController);
            SetRef(placement, "reportButton", reportButton);
            SetRef(placement, "settingsPanel", settingsController);
            SetRef(placement, "loginPanel", loginController);
            SetRef(placement, "historyPanel", historyController);
            SetRef(placement, "markerPrefab", markerPrefab);
            SetRef(placement, "toast", toast);
            var diagnostics = xrOrigin.GetComponent<Inspection.AR.PlacementDiagnostics>();
            if (diagnostics == null)
            {
                diagnostics = xrOrigin.gameObject.AddComponent<Inspection.AR.PlacementDiagnostics>();
            }
            SetRef(placement, "diagnostics", diagnostics);
            SetRef(placement, "priorityMaterials", markerMaterials);
            SetRef(formController, "placement", placement);

            // 系统返回统一分发（计划 007 4.5）：软键盘 → 设置 → 登录 → 历史 → 表单 → AR 主界面
            var backHandler = appUi.AddComponent<Inspection.UI.AndroidBackHandler>();
            SetRef(backHandler, "loginPanel", loginController);
            SetRef(backHandler, "settingsPanel", settingsController);
            SetRef(backHandler, "historyPanel", historyController);
            SetRef(backHandler, "formPanel", formController);
            SetRef(backHandler, "placement", placement);
            SetRef(backHandler, "toast", toast);

            // 调试条接线：标记与按钮诊断信息来源
            SetRef(overlay, "placement", placement);
            SetRef(overlay, "reportButton", reportButton);
            SetRef(settingsController, "loginPanel", loginController);
            SetRef(settingsController, "historyPanel", historyController);
            var authClient = appUi.GetComponent<Inspection.API.AuthApiClient>();
            SetRef(settingsController, "authClient", authClient);
            var logoutButton = appUi.transform.Find("SettingsPanelRoot/SafeAreaContent/SettingsCard/Viewport/Content/AccountSection/LogoutButton")?.GetComponent<Button>();
            if (logoutButton == null)
            {
                throw new InvalidOperationException("设置面板缺少退出登录按钮");
            }
            UnityEventTools.AddVoidPersistentListener(logoutButton.onClick, settingsController.OnLogoutClicked);

            // 最终渲染层级（index 0 在最底层）：表单必须盖住顶部栏、引导与跟随按钮，
            // 历史/登录/设置依次盖住表单（设置可在登录前配置后端地址），Toast 永远最上
            var siblingOrder = new[]
            {
                "ReportButtonRoot", "TopBar", "FieldGuideRoot", "DevDebugOverlay",
                "FormPanelRoot", "HistoryPanelRoot", "LoginPanelRoot", "SettingsPanelRoot", "ToastRoot",
            };
            for (var i = 0; i < siblingOrder.Length; i++)
            {
                var child = appUi.transform.Find(siblingOrder[i]);
                if (child == null)
                {
                    throw new InvalidOperationException($"AppUI 缺少子节点 {siblingOrder[i]}，层级排序中止");
                }
                child.SetSiblingIndex(i);
            }
        }

        private static Button BuildTopBar(Transform parent, Font font,
            Inspection.UI.SettingsPanelController settingsController)
        {
            var root = UnityWorkspaceUi.Full(parent, "TopBar", Color.clear, safeArea: true, out var content);
            var bar = UnityWorkspaceUi.Rect(content, "Bar", Vector2.zero, Vector2.zero);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, 168f);
            UnityWorkspaceUi.Surface(bar, UiVisualStyle.TopBar);

            var status = UnityWorkspaceUi.Text(bar, "BackendStatus", "未配置后端", 36, Color.white);
            status.rectTransform.anchorMin = Vector2.zero;
            status.rectTransform.anchorMax = Vector2.one;
            status.rectTransform.offsetMin = new Vector2(32f, 0f);
            status.rectTransform.offsetMax = new Vector2(-660f, 0f);
            status.horizontalOverflow = HorizontalWrapMode.Overflow;
            UnityWorkspaceUi.SetRef(settingsController, "topBarStatusText", status);

            var settingsButton = UnityWorkspaceUi.HeaderButton(bar, "SettingsButton", "设置", 120f);
            UnityWorkspaceUi.Click(settingsButton, settingsController.Open);
            var torchButton = UnityWorkspaceUi.HeaderButton(bar, "TorchButton", "手电 关", 320f);
            var torch = torchButton.gameObject.AddComponent<Inspection.AR.TorchController>();
            UnityWorkspaceUi.SetRef(torch, "cameraManager", UnityEngine.Object.FindFirstObjectByType<ARCameraManager>());
            UnityWorkspaceUi.SetRef(torch, "button", torchButton);
            UnityWorkspaceUi.SetRef(torch, "label", torchButton.GetComponentInChildren<Text>());
            UnityWorkspaceUi.Click(torchButton, torch.Toggle);
            return UnityWorkspaceUi.HeaderButton(bar, "HistoryButton", "历史", 520f);
        }

        private static void BuildFieldGuide(Transform parent, Font font)
        {
            var root = UnityWorkspaceUi.Full(parent, "FieldGuideRoot", Color.clear, safeArea: true, out var content);
            var guide = UnityWorkspaceUi.Rect(content, "Guide", Vector2.zero, Vector2.zero);
            UnityWorkspaceUi.Bottom(guide, 44f, 108f);
            var guideImage = UnityWorkspaceUi.Surface(guide, UiVisualStyle.TopBar);
            UnityWorkspaceUi.Round(guideImage, UiVisualStyle.ControlRadius);
            var text = UnityWorkspaceUi.Text(guide, "Message", "移动手机扫描，点击蓝色平面放置标记",
                42, Color.white, TextAnchor.MiddleCenter);
            UnityWorkspaceUi.StretchPad(text.rectTransform, 16f, 4f);
            root.gameObject.AddComponent<Inspection.UI.FieldGuideView>().Configure(text);
        }

        private static Inspection.UI.ToastView BuildToast(Transform parent, Font font)
        {
            var root = UnityWorkspaceUi.Full(parent, "ToastRoot", Color.clear, safeArea: true, out var content);
            var card = UnityWorkspaceUi.Rect(content, "ToastCard", Vector2.zero, Vector2.zero);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -196f);
            card.sizeDelta = new Vector2(960f, 132f);
            var toastImage = UnityWorkspaceUi.Surface(card, new Color(0.05f, 0.08f, 0.14f, 0.96f));
            UnityWorkspaceUi.Round(toastImage, UiVisualStyle.CardRadius);
            var text = UnityWorkspaceUi.Text(card, "Message", string.Empty, 42, Color.white, TextAnchor.MiddleCenter);
            UnityWorkspaceUi.StretchPad(text.rectTransform, 24f, 8f);
            var toast = root.gameObject.AddComponent<Inspection.UI.ToastView>();
            UnityWorkspaceUi.SetRef(toast, "root", root.gameObject);
            UnityWorkspaceUi.SetRef(toast, "messageText", text);
            root.gameObject.SetActive(false);
            return toast;
        }

        private static Inspection.UI.MarkerReportButton BuildReportButton(Transform parent, Font font)
        {
            var holder = UnityWorkspaceUi.Full(parent, "ReportButtonRoot", Color.clear, safeArea: false);
            var buttonGo = UnityWorkspaceUi.Rect(holder, "ReportButton", Vector2.zero, new Vector2(448f, 144f));
            UnityWorkspaceUi.ButtonOn(buttonGo, "上报此标记", UiVisualStyle.Primary, Color.white, true);
            var reportButton = holder.gameObject.AddComponent<Inspection.UI.MarkerReportButton>();
            UnityWorkspaceUi.SetRef(reportButton, "movable", buttonGo);
            holder.gameObject.SetActive(false);
            return reportButton;
        }

        private static Inspection.UI.IssueFormController BuildFormPanel(Transform parent, Font font,
            out InputField titleInput, out InputField descriptionInput,
            out Inspection.UI.PrioritySelector prioritySelector)
        {
            var root = UnityWorkspaceUi.Full(parent, "FormPanelRoot", Color.clear, safeArea: true, out var safeArea);
            UnityWorkspaceUi.Mask(safeArea);
            // 底部卡片：横向拉满、纵向固定 1600 高并贴安全区底边。
            // 不能用全屏拉伸锚点再加 sizeDelta.y=1600：卡片实际高度会变成
            // 屏幕高度 + 1600，顶部对齐卡片顶的滚动内容整体排到屏幕外，表单只剩白底。
            var card = UnityWorkspaceUi.Rect(safeArea, "FormCard", Vector2.zero, Vector2.zero);
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(1f, 0f);
            card.pivot = new Vector2(0.5f, 0f);
            card.offsetMin = Vector2.zero;
            card.offsetMax = new Vector2(0f, 1600f);
            UnityWorkspaceUi.Surface(card, UiVisualStyle.Surface);
            UnityWorkspaceUi.Round(card.GetComponent<Image>(), UiVisualStyle.FormCardRadius);
            var scroll = UnityWorkspaceUi.Scroll(card, "KeyboardArea", out var content, keyboardInset: true);
            scroll.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            scroll.GetComponent<RectTransform>().offsetMax = Vector2.zero;

            UnityWorkspaceUi.Label(content, "Header", "上报问题", 28f, 96f, UiVisualStyle.Ink, 58);
            var cancel = UnityWorkspaceUi.Button(content, "CancelButton", "取消", Vector2.zero, new Vector2(184f, 128f),
                UiVisualStyle.Surface, UiVisualStyle.Secondary);
            var cancelRect = (RectTransform)cancel.transform;
            cancelRect.anchorMin = cancelRect.anchorMax = cancelRect.pivot = new Vector2(1f, 1f);
            cancelRect.anchoredPosition = new Vector2(-24f, -24f);
            UnityWorkspaceUi.Label(content, "TitleLabel", "标题（必填）", 152f, 56f, UiVisualStyle.Secondary, 38);
            titleInput = UnityWorkspaceUi.Input(content, "TitleInput", "如：入口墙面破损", 220f, 128f, multiline: false);
            UnityWorkspaceUi.Label(content, "DescLabel", "描述（可选）", 384f, 56f, UiVisualStyle.Secondary, 38);
            descriptionInput = UnityWorkspaceUi.Input(content, "DescInput", "补充位置细节、影响范围等", 452f, 176f, multiline: true);
            UnityWorkspaceUi.Label(content, "PriorityLabel", "优先级", 664f, 56f, UiVisualStyle.Secondary, 38);
            prioritySelector = BuildPrioritySelector(content, 736f);
            UnityWorkspaceUi.Label(content, "PositionLabel", "位置（自动带出，只读）", 928f, 56f, UiVisualStyle.Secondary, 38);
            var positionText = UnityWorkspaceUi.Label(content, "PositionText", "x 0.000   y 0.000   z 0.000",
                996f, 72f, UiVisualStyle.Ink, 40);
            var errorText = UnityWorkspaceUi.Label(content, "ErrorText", string.Empty, 1104f, 128f, UiVisualStyle.Error, 38);
            errorText.verticalOverflow = VerticalWrapMode.Overflow;
            var submit = UnityWorkspaceUi.Button(content, "SubmitButton", "提交", Vector2.zero, Vector2.zero,
                UiVisualStyle.Primary, Color.white, true);
            UnityWorkspaceUi.Top((RectTransform)submit.transform, 1248f, 144f);
            UnityWorkspaceUi.ScrolledContent(content, 1424f);

            var apiClient = root.gameObject.AddComponent<Inspection.API.IssueApiClient>();
            var controller = root.gameObject.AddComponent<Inspection.UI.IssueFormController>();
            UnityWorkspaceUi.SetRef(controller, "panelRoot", root.gameObject);
            UnityWorkspaceUi.SetRef(controller, "titleInput", titleInput);
            UnityWorkspaceUi.SetRef(controller, "descriptionInput", descriptionInput);
            UnityWorkspaceUi.SetRef(controller, "prioritySelector", prioritySelector);
            UnityWorkspaceUi.SetRef(controller, "positionText", positionText);
            UnityWorkspaceUi.SetRef(controller, "submitButton", submit);
            UnityWorkspaceUi.SetRef(controller, "submitLabel", submit.GetComponentInChildren<Text>());
            UnityWorkspaceUi.SetRef(controller, "errorText", errorText);
            UnityWorkspaceUi.Click(submit, controller.OnSubmitClicked);
            UnityWorkspaceUi.Click(cancel, controller.OnCancelClicked);
            root.gameObject.SetActive(false);
            return controller;
        }

        private static Inspection.UI.PrioritySelector BuildPrioritySelector(Transform parent, float y)
        {
            var root = UnityWorkspaceUi.Rect(parent, "PrioritySelector", Vector2.zero, Vector2.zero);
            UnityWorkspaceUi.Top(root, y, 144f);
            var selector = root.gameObject.AddComponent<Inspection.UI.PrioritySelector>();
            var buttons = new Button[3];
            var backgrounds = new Image[3];
            var labels = new Text[3];
            for (var i = 0; i < 3; i++)
            {
                var color = UiVisualStyle.Priority(Inspection.API.IssueContract.PriorityValues[i]);
                var button = UnityWorkspaceUi.Button(root, "Priority_" + i, string.Empty, Vector2.zero, Vector2.zero,
                    UiVisualStyle.Surface, color);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(i / 3f, 0f);
                rect.anchorMax = new Vector2((i + 1) / 3f, 1f);
                rect.offsetMin = new Vector2(i == 0 ? 0f : 8f, 0f);
                rect.offsetMax = new Vector2(i == 2 ? 0f : -8f, 0f);
                buttons[i] = button;
                backgrounds[i] = button.GetComponent<Image>();
                labels[i] = button.GetComponentInChildren<Text>();
                UnityWorkspaceUi.SelectPriority(button, selector, i);
            }
            selector.SetValueWithoutNotify(0);
            SetRef(selector, "buttons", buttons);
            SetRef(selector, "backgrounds", backgrounds);
            SetRef(selector, "labels", labels);
            return selector;
        }

        /// <summary>全屏登录页：浅中性底、白色登录卡片，软键盘弹起时可滚动到错误与登录按钮。</summary>
        private static Inspection.UI.LoginPanelController BuildLoginPanel(Transform parent, Font font,
            Inspection.UI.SettingsPanelController settingsController)
        {
            var root = UnityWorkspaceUi.Full(parent, "LoginPanelRoot", UiVisualStyle.Page, safeArea: true,
                out var safeArea);
            var scroll = UnityWorkspaceUi.Scroll(safeArea, "LoginScroll", out var content, keyboardInset: true);
            UnityWorkspaceUi.Label(content, "ProductName", "现场巡检问题标注", 100f, 92f, UiVisualStyle.Ink, 64,
                TextAnchor.MiddleCenter);
            UnityWorkspaceUi.Label(content, "ProductDescription", "巡检员端 · 现场问题上报与处理", 208f, 64f,
                UiVisualStyle.Secondary, 42, TextAnchor.MiddleCenter);
            var card = UnityWorkspaceUi.Card(content, "LoginCard", 360f, 1280f);
            UnityWorkspaceUi.Label(card, "CardTitle", "巡检员登录", 44f, 80f, UiVisualStyle.Ink, 52);
            UnityWorkspaceUi.Label(card, "CardHint", "进入园区巡检工作台", 136f, 56f, UiVisualStyle.Secondary, 38);
            UnityWorkspaceUi.Label(card, "UsernameLabel", "用户名", 236f, 56f, UiVisualStyle.Secondary, 38);
            var usernameInput = UnityWorkspaceUi.Input(card, "UsernameInput", "请输入巡检员账号", 300f, 128f, false);
            UnityWorkspaceUi.Label(card, "PasswordLabel", "密码", 464f, 56f, UiVisualStyle.Secondary, 38);
            var passwordInput = UnityWorkspaceUi.Input(card, "PasswordInput", "请输入账号密码", 528f, 128f, false,
                password: true);
            var noticeBackground = UnityWorkspaceUi.Rect(card, "NoticeBackground", Vector2.zero, Vector2.zero);
            UnityWorkspaceUi.Top(noticeBackground, 708f, 144f);
            var noticeImage = UnityWorkspaceUi.Surface(noticeBackground, UiVisualStyle.Tint(UiVisualStyle.Secondary), false);
            UnityWorkspaceUi.Round(noticeImage, UiVisualStyle.ControlRadius);
            var notice = UnityWorkspaceUi.Text(noticeBackground, "NoticeText", "请登录巡检员账号", 38,
                UiVisualStyle.Secondary, TextAnchor.MiddleCenter);
            UnityWorkspaceUi.StretchPad(notice.rectTransform, 16f, 4f);
            var feedback = card.gameObject.AddComponent<Inspection.UI.LoginFeedbackView>();
            feedback.Configure(notice, noticeBackground.GetComponent<Image>());
            var login = UnityWorkspaceUi.Button(card, "LoginButton", "登录", Vector2.zero, Vector2.zero,
                UiVisualStyle.Primary, Color.white, true);
            UnityWorkspaceUi.Top((RectTransform)login.transform, 896f, 144f);
            var settingsEntry = UnityWorkspaceUi.Button(card, "SettingsEntry", "后端地址设置", Vector2.zero, Vector2.zero,
                UiVisualStyle.Surface, UiVisualStyle.Primary);
            UnityWorkspaceUi.Top((RectTransform)settingsEntry.transform, 1072f, 136f);
            UnityWorkspaceUi.Label(content, "AccountNotice", "本地演示账号由后端环境配置，登录密码不保存在设备上。",
                1700f, 112f, UiVisualStyle.Secondary, 36, TextAnchor.MiddleCenter);
            UnityWorkspaceUi.ScrolledContent(content, 1860f);

            var authClient = parent.gameObject.AddComponent<Inspection.API.AuthApiClient>();
            var controller = parent.gameObject.AddComponent<Inspection.UI.LoginPanelController>();
            UnityWorkspaceUi.SetRef(controller, "panelRoot", root.gameObject);
            UnityWorkspaceUi.SetRef(controller, "apiClient", authClient);
            UnityWorkspaceUi.SetRef(controller, "usernameInput", usernameInput);
            UnityWorkspaceUi.SetRef(controller, "passwordInput", passwordInput);
            UnityWorkspaceUi.SetRef(controller, "loginButton", login);
            UnityWorkspaceUi.SetRef(controller, "loginLabel", login.GetComponentInChildren<Text>());
            UnityWorkspaceUi.SetRef(controller, "noticeText", notice);
            UnityWorkspaceUi.Click(login, controller.OnLoginClicked);
            UnityWorkspaceUi.Click(settingsEntry, settingsController.Open);
            root.gameObject.SetActive(true);
            return controller;
        }

        /// <summary>历史改为全屏工作台；卡片使用独立的优先级与状态文字标签。</summary>
        private static Inspection.UI.HistoryPanelController BuildHistoryPanel(Transform parent, Font font,
            Inspection.UI.LoginPanelController loginPanel)
        {
            var root = UnityWorkspaceUi.Full(parent, "HistoryPanelRoot", UiVisualStyle.Page, safeArea: true,
                out var safeArea);
            var header = UnityWorkspaceUi.Rect(safeArea, "Header", Vector2.zero, Vector2.zero);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = Vector2.zero;
            header.offsetMax = Vector2.zero;
            header.sizeDelta = new Vector2(0f, 280f);
            UnityWorkspaceUi.Surface(header, UiVisualStyle.Surface);
            UnityWorkspaceUi.Label(header, "Title", "历史上报", 32f, 76f, UiVisualStyle.Ink, 58);
            var accountText = UnityWorkspaceUi.Label(header, "AccountText", "当前账号：", 116f, 60f,
                UiVisualStyle.Secondary, 38);
            var statusText = UnityWorkspaceUi.Label(header, "StatusText", string.Empty, 184f, 76f,
                UiVisualStyle.Secondary, 38);
            var close = UnityWorkspaceUi.Button(header, "CloseButton", "返回", Vector2.zero, new Vector2(152f, 128f),
                UiVisualStyle.Surface, UiVisualStyle.Primary);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-104f, -80f);
            var refresh = UnityWorkspaceUi.Button(header, "RefreshButton", "刷新", Vector2.zero, new Vector2(176f, 128f),
                UiVisualStyle.Primary, Color.white, true);
            var refreshRect = (RectTransform)refresh.transform;
            refreshRect.anchorMin = refreshRect.anchorMax = new Vector2(1f, 1f);
            refreshRect.anchoredPosition = new Vector2(-296f, -80f);
            var logout = UnityWorkspaceUi.Button(header, "LogoutButton", "退出登录", Vector2.zero, new Vector2(240f, 128f),
                UiVisualStyle.Surface, UiVisualStyle.Error);
            var logoutRect = (RectTransform)logout.transform;
            logoutRect.anchorMin = logoutRect.anchorMax = new Vector2(1f, 1f);
            logoutRect.anchoredPosition = new Vector2(-152f, -200f);

            var scroll = UnityWorkspaceUi.Scroll(safeArea, "ScrollView", out var content);
            var scrollRect = scroll.GetComponent<RectTransform>();
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0f, -280f);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 36, 48);
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var template = BuildHistoryItemTemplate(content, font);

            var historyClient = parent.gameObject.AddComponent<Inspection.API.HistoryApiClient>();
            var authClient = parent.gameObject.GetComponent<Inspection.API.AuthApiClient>();
            var controller = parent.gameObject.AddComponent<Inspection.UI.HistoryPanelController>();
            UnityWorkspaceUi.SetRef(controller, "panelRoot", root.gameObject);
            UnityWorkspaceUi.SetRef(controller, "apiClient", historyClient);
            UnityWorkspaceUi.SetRef(controller, "authClient", authClient);
            UnityWorkspaceUi.SetRef(controller, "loginPanel", loginPanel);
            UnityWorkspaceUi.SetRef(controller, "accountText", accountText);
            UnityWorkspaceUi.SetRef(controller, "refreshButton", refresh);
            UnityWorkspaceUi.SetRef(controller, "refreshLabel", refresh.GetComponentInChildren<Text>());
            UnityWorkspaceUi.SetRef(controller, "statusText", statusText);
            UnityWorkspaceUi.SetRef(controller, "listContent", content);
            UnityWorkspaceUi.SetRef(controller, "itemTemplate", template);
            UnityWorkspaceUi.Click(refresh, controller.OnRefreshClicked);
            UnityWorkspaceUi.Click(logout, controller.OnLogoutClicked);
            UnityWorkspaceUi.Click(close, controller.OnCloseClicked);
            root.gameObject.SetActive(false);
            return controller;
        }

        private static GameObject BuildHistoryItemTemplate(RectTransform content, Font font)
        {
            // 行高按两行描述与两行时间预留，避免长文案被截断（计划 007 历史卡片）
            const float itemHeight = 520f;
            var item = UnityWorkspaceUi.Rect(content, "HistoryItemTemplate", Vector2.zero, Vector2.zero);
            item.sizeDelta = new Vector2(0f, itemHeight);
            item.gameObject.AddComponent<LayoutElement>().preferredHeight = itemHeight;
            var itemImage = UnityWorkspaceUi.Surface(item, UiVisualStyle.Surface);
            UnityWorkspaceUi.Round(itemImage, UiVisualStyle.CardRadius);
            UnityWorkspaceUi.Label(item, "TitleText", "标题", 28f, 72f, UiVisualStyle.Ink, 48);
            BuildHistoryTag(item, "PriorityTag", "PriorityText", "优先级 中", 48f, 124f, 300f,
                UiVisualStyle.Medium, true);
            BuildHistoryTag(item, "StatusTag", "StatusText", "待处理", 372f, 124f, 280f,
                UiVisualStyle.Open, false);
            UnityWorkspaceUi.Label(item, "DescText", "描述", 224f, 100f, UiVisualStyle.Secondary, 40);
            UnityWorkspaceUi.Label(item, "PositionText", "x 0.000   y 0.000   z 0.000", 340f, 48f,
                UiVisualStyle.Secondary, 36);
            UnityWorkspaceUi.Label(item, "TimeText", "创建 · 更新", 404f, 88f, UiVisualStyle.Secondary, 34);
            item.gameObject.SetActive(false);
            return item.gameObject;
        }

        /// <summary>
        /// 历史卡片标签：左上角固定宽度，先定锚点再定位置。不能先 Top() 再改 offset，
        /// 否则纵向位置会被重置到卡片顶部（表现为标签压住标题、或高度为负不可见）。
        /// </summary>
        private static void BuildHistoryTag(RectTransform item, string name, string textName,
            string initialText, float x, float y, float width, Color color, bool priority)
        {
            var tag = UnityWorkspaceUi.Rect(item, name, Vector2.zero, new Vector2(width, 72f));
            tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
            tag.pivot = new Vector2(0f, 1f);
            tag.anchoredPosition = new Vector2(x, -y);
            var background = UnityWorkspaceUi.Surface(tag, UiVisualStyle.Tint(color), false);
            UnityWorkspaceUi.Round(background, UiVisualStyle.TagRadius);
            var label = UnityWorkspaceUi.Text(tag, textName, initialText, 38, color);
            UnityWorkspaceUi.StretchPad(label.rectTransform, 16f, 0f);
            tag.gameObject.AddComponent<Inspection.UI.HistoryTagView>().Configure(label, background, priority);
        }

        /// <summary>设置全屏页：后端地址、账号、诊断与 AR 会话四区；危险操作保留文字二次确认。</summary>
        private static Inspection.UI.SettingsPanelController BuildSettingsPanel(Transform parent, Font font)
        {
            var root = UnityWorkspaceUi.Full(parent, "SettingsPanelRoot", UiVisualStyle.Page, safeArea: true,
                out var safeArea);
            var scroll = UnityWorkspaceUi.Scroll(safeArea, "SettingsCard", out var content, keyboardInset: true);
            UnityWorkspaceUi.Label(content, "Title", "设置", 36f, 88f, UiVisualStyle.Ink, 58);
            var close = UnityWorkspaceUi.Button(content, "CloseButton", "返回", Vector2.zero, new Vector2(160f, 128f),
                UiVisualStyle.Surface, UiVisualStyle.Primary);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-96f, -52f);

            var backend = UnityWorkspaceUi.Section(content, "BackendSection", "后端地址", 172f, 504f);
            var addressInput = UnityWorkspaceUi.Input(backend, "AddressInput", "http://192.168.31.17:8080",
                116f, 128f, false);
            UnityWorkspaceUi.Label(backend, "Hint", "填写电脑后端的局域网地址，手机与电脑需同一 Wi-Fi。",
                268f, 96f, UiVisualStyle.Secondary, 38);
            var save = UnityWorkspaceUi.Button(backend, "SaveButton", "保存地址", Vector2.zero, Vector2.zero,
                UiVisualStyle.Primary, Color.white, true);
            UnityWorkspaceUi.Top((RectTransform)save.transform, 384f, 128f);

            // 账号区：说明独占整行，退出按钮另起一行，避免覆盖文字（计划 007 真机排版缺陷）
            var account = UnityWorkspaceUi.Section(content, "AccountSection", "账号", 708f, 380f);
            UnityWorkspaceUi.Label(account, "Description", "账号仅保存在当前会话中，不保存密码。",
                108f, 88f, UiVisualStyle.Secondary, 38);
            var logout = UnityWorkspaceUi.Button(account, "LogoutButton", "退出登录", Vector2.zero, new Vector2(320f, 128f),
                UiVisualStyle.Surface, UiVisualStyle.Error);
            var logoutRect = (RectTransform)logout.transform;
            logoutRect.anchorMin = logoutRect.anchorMax = new Vector2(0f, 1f);
            logoutRect.pivot = new Vector2(0f, 1f);
            logoutRect.anchoredPosition = new Vector2(48f, -220f);

            var diagnostics = UnityWorkspaceUi.Section(content, "DiagnosticsSection", "诊断", 1120f, 448f);
            var toggle = UnityWorkspaceUi.Toggle(diagnostics, "FullDiagnosticsToggle", "完整诊断（仅排错时开启）", 108f, 136f);
            UnityWorkspaceUi.Label(diagnostics, "Hint",
                "开启后显示屏幕调试信息，记录详细设备日志；放置诊断包含实景截图与坐标。默认关闭，重启保留设置。",
                268f, 136f, UiVisualStyle.Secondary, 38);

            var ar = UnityWorkspaceUi.Section(content, "ArSection", "AR 会话", 1600f, 364f);
            UnityWorkspaceUi.Label(ar, "Description", "跟踪或落点异常时重建地图；当前草稿会被清除，需二次确认。",
                108f, 88f, UiVisualStyle.Secondary, 38);
            var reset = UnityWorkspaceUi.Button(ar, "ResetButton", "重置 AR 会话", Vector2.zero, Vector2.zero,
                UiVisualStyle.Surface, UiVisualStyle.Error);
            UnityWorkspaceUi.Top((RectTransform)reset.transform, 216f, 144f);
            var resetController = reset.gameObject.AddComponent<Inspection.UI.SessionResetButton>();
            resetController.Configure(reset.GetComponentInChildren<Text>());
            UnityWorkspaceUi.Click(reset, resetController.ConfirmOrReset);
            UnityWorkspaceUi.ScrolledContent(content, 2020f);

            var controller = parent.gameObject.AddComponent<Inspection.UI.SettingsPanelController>();
            UnityWorkspaceUi.SetRef(controller, "panelRoot", root.gameObject);
            UnityWorkspaceUi.SetRef(controller, "addressInput", addressInput);
            UnityWorkspaceUi.SetRef(controller, "fullDiagnosticsToggle", toggle);
            UnityWorkspaceUi.Click(save, controller.OnSaveClicked);
            UnityWorkspaceUi.Click(close, controller.OnCloseClicked);
            UnityEventTools.AddBoolPersistentListener(toggle.onValueChanged, controller.OnFullDiagnosticsToggled, false);
            root.gameObject.SetActive(false);
            return controller;
        }

        /// <summary>开发构建调试信息条（两行：会话/平面/点击 + 标记/按钮诊断；受完整诊断开关门控）。</summary>
        private static Inspection.AR.DevDebugOverlay BuildDevOverlay(Transform parent, Font font)
        {
            var go = new GameObject("DevDebugOverlay", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            // 顶部栏高 168：调试条从工具栏下方开始，避免黄字盖住后端地址与按钮（计划 007）
            rect.anchoredPosition = new Vector2(20f, -200f);
            rect.sizeDelta = new Vector2(1040f, 150f);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 28;
            text.color = Color.yellow;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false; // 调试条不吃触摸
            text.text = "调试初始化…";
            var overlay = go.AddComponent<Inspection.AR.DevDebugOverlay>();
            SetRef(overlay, "text", text);
            return overlay;
        }

        // ---------- uGUI 构建小工具 ----------

        /// <summary>卡片内子元素统一锚在卡片顶部中点（y 为距卡片顶部距离，向下生长）。</summary>
        private static void AnchorTopOfCard(Transform t, float yFromTop)
        {
            var rect = (RectTransform)t;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -yFromTop);
        }

        private static GameObject UIRect(Transform parent, string name, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            return go;
        }

        private static void Stretch(RectTransform rect, Transform parent)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void StretchPad(RectTransform rect, float padX, float padY)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padX, padY);
            rect.offsetMax = new Vector2(-padX, -padY);
        }

        private static Text MakeText(Transform parent, string name, Font font, int size, Color color, TextAnchor align)
        {
            var go = UIRect(parent, name, Vector2.zero, Vector2.zero);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        private static Text MakeLabel(Transform card, Font font, string content, float yFromTop, float height, Color color)
        {
            var label = MakeText(card, "Label_" + yFromTop, font, 40, color, TextAnchor.MiddleLeft);
            var rect = (RectTransform)label.transform;
            AnchorTopOfCard(rect, yFromTop);
            rect.sizeDelta = new Vector2(920f, height);
            label.text = content;
            return label;
        }

        private static void MakeMask(Transform parent)
        {
            var mask = new GameObject("Mask", typeof(RectTransform));
            Stretch(mask.GetComponent<RectTransform>(), parent);
            var image = mask.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.55f);
            image.raycastTarget = true; // 蒙层吃掉触摸：不放置、也不关闭面板（计划 4.2）
        }

        private static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size,
            Color bg, Font font, int fontSize)
        {
            var go = UIRect(parent, name, pos, size);
            return MakeButtonOn(go, label, bg, font, fontSize);
        }

        private static Button MakeButtonOn(GameObject go, string label, Color bg, Font font, int fontSize)
        {
            var image = go.AddComponent<Image>();
            image.color = bg;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var text = MakeText(go.transform, "Label", font, fontSize, TextMain, TextAnchor.MiddleCenter);
            StretchPad(text.rectTransform, 8f, 4f);
            text.text = label;
            return button;
        }

        private static Toggle MakeToggle(Transform card, Font font, string name, string label, float yFromTop)
        {
            var root = UIRect(card, name, Vector2.zero, new Vector2(920f, 88f));
            AnchorTopOfCard(root.transform, yFromTop);
            var bg = root.AddComponent<Image>();
            bg.color = InputBg;
            bg.raycastTarget = true;

            var checkGo = UIRect(root.transform, "Checkmark", Vector2.zero, Vector2.zero);
            var checkRect = (RectTransform)checkGo.transform;
            checkRect.anchorMin = checkRect.anchorMax = checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.anchoredPosition = new Vector2(28f, 0f);
            checkRect.sizeDelta = new Vector2(44f, 44f);
            var check = checkGo.AddComponent<Image>();
            check.color = AccentBg;
            check.raycastTarget = false;

            var text = MakeText(root.transform, "Label", font, 38, TextMain, TextAnchor.MiddleLeft);
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(96f, 0f);
            textRect.offsetMax = new Vector2(-24f, 0f);
            text.text = label;
            text.raycastTarget = false;

            var toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = check;
            toggle.isOn = false;
            return toggle;
        }

        private sealed class InputResult
        {
            public InputField input;
        }

        private static InputResult MakeInput(Transform card, Font font, string name, string placeholder,
            float yFromTop, float height, bool multiline)
        {
            var root = UIRect(card, name, Vector2.zero, new Vector2(920f, height));
            var rootRect = (RectTransform)root.transform;
            AnchorTopOfCard(rootRect, yFromTop);
            var bg = root.AddComponent<Image>();
            bg.color = InputBg;

            var text = MakeText(rootRect, "Text", font, 40, TextMain,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            StretchPad(text.rectTransform, 20f, 10f);
            text.raycastTarget = true;

            var ph = MakeText(rootRect, "Placeholder", font, 40, TextPlaceholder,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            StretchPad(ph.rectTransform, 20f, 10f);
            ph.text = placeholder;

            var input = root.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.caretColor = TextMain;
            input.selectionColor = new Color(0.15f, 0.39f, 0.92f, 0.6f);
            return new InputResult { input = input };
        }

        private static Dropdown MakeDropdown(Transform card, Font font, float yFromTop)
        {
            var root = UIRect(card, "PriorityDropdown", Vector2.zero, new Vector2(920f, 92f));
            AnchorTopOfCard(root.transform, yFromTop);
            root.AddComponent<Image>().color = InputBg;

            var caption = MakeText(root.transform, "Label", font, 40, TextMain, TextAnchor.MiddleLeft);
            StretchPad(caption.rectTransform, 20f, 4f);

            var template = new GameObject("Template", typeof(RectTransform));
            var templateRect = (RectTransform)template.transform;
            templateRect.SetParent(root.transform, false);
            templateRect.anchorMin = new Vector2(0f, 1f);
            templateRect.anchorMax = new Vector2(1f, 1f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.offsetMin = new Vector2(0f, -300f);
            templateRect.offsetMax = Vector2.zero;
            template.AddComponent<Image>().color = PanelBg;

            var item = new GameObject("Item", typeof(RectTransform));
            var itemRect = (RectTransform)item.transform;
            itemRect.SetParent(templateRect, false);
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.pivot = new Vector2(0.5f, 1f);
            itemRect.offsetMin = new Vector2(0f, -100f);
            itemRect.offsetMax = Vector2.zero;
            var itemBg = item.AddComponent<Image>();
            itemBg.color = new Color(1f, 1f, 1f, 0.08f);
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = itemBg;

            var itemLabel = MakeText(itemRect, "ItemLabel", font, 40, TextMain, TextAnchor.MiddleLeft);
            StretchPad(itemLabel.rectTransform, 20f, 4f);
            itemLabel.raycastTarget = true;

            var dropdown = root.AddComponent<Dropdown>();
            dropdown.template = templateRect;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            dropdown.AddOptions(new List<string> { "中", "低", "高" });
            dropdown.value = 0;
            dropdown.RefreshShownValue();
            template.SetActive(false);
            return dropdown;
        }

        private static void SetRef(Component component, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                throw new InvalidOperationException($"{component.GetType().Name} 缺少序列化字段 {field}");
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>数组字段接线（如优先级材质数组）：逐元素写入后整体应用。</summary>
        private static void SetRef(Component component, string field, UnityEngine.Object[] values)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(field);
            if (prop == null || !prop.isArray)
            {
                throw new InvalidOperationException($"{component.GetType().Name} 缺少数组序列化字段 {field}");
            }
            prop.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
