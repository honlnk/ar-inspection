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
            var formController = BuildFormPanel(appUi.transform, font, out var titleInput, out var priorityDropdown);
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
            // 优先级切换：表单内换优先级立即重着色已放置草稿（计划 006 4.5）
            UnityEventTools.AddIntPersistentListener(priorityDropdown.onValueChanged, formController.OnPriorityChanged, 0);

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

            // 调试条接线：标记与按钮诊断信息来源
            SetRef(overlay, "placement", placement);
            SetRef(overlay, "reportButton", reportButton);

            // 最终渲染层级（index 0 在最底层）：全屏面板必须盖住顶部栏与跟随按钮，
            // 设置面板盖住登录面板（登录前也能配置后端地址），Toast 永远最上
            var siblingOrder = new[]
            {
                "FormPanelRoot", "ReportButtonRoot", "TopBar", "DevDebugOverlay",
                "HistoryPanelRoot", "LoginPanelRoot", "SettingsPanelRoot", "ToastRoot",
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
            var bar = new GameObject("TopBar", typeof(RectTransform));
            var barRect = (RectTransform)bar.transform;
            barRect.SetParent(parent, false);
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.offsetMin = new Vector2(0f, -120f);
            barRect.offsetMax = Vector2.zero;
            var barImage = bar.AddComponent<Image>();
            barImage.color = TopBarBg;
            barImage.raycastTarget = true; // 顶部栏区域点击不放置

            var statusGo = new GameObject("BackendStatus", typeof(RectTransform));
            var statusRect = (RectTransform)statusGo.transform;
            statusRect.SetParent(barRect, false);
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(24f, 0f);
            statusRect.offsetMax = new Vector2(-900f, 0f);
            var statusText = statusGo.AddComponent<Text>();
            statusText.font = font;
            statusText.fontSize = 32;
            statusText.color = TextSub;
            statusText.alignment = TextAnchor.MiddleLeft;
            statusText.horizontalOverflow = HorizontalWrapMode.Overflow; // 局域网地址不换行
            statusText.text = "未配置后端";
            statusText.raycastTarget = true;
            SetRef(settingsController, "topBarStatusText", statusText);

            // 重置 AR 会话（真机攻坚新增）：跟踪漂移产生伪平面后一键重建地图
            var resetButton = MakeTopBarButton(barRect, "ResetButton", "重置", -775f, CancelBg, font);
            var session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (session == null)
            {
                throw new InvalidOperationException("场景缺少 ARSession，无法接线重置按钮");
            }
            UnityEventTools.AddVoidPersistentListener(resetButton.onClick, session.Reset);

            // 手电开关（用户授权新增）：暗光环境下辅助平面检测
            var torchButton = MakeTopBarButton(barRect, "TorchButton", "手电 关", -555f, CancelBg, font);
            var torch = torchButton.gameObject.AddComponent<Inspection.AR.TorchController>();
            SetRef(torch, "cameraManager", UnityEngine.Object.FindFirstObjectByType<ARCameraManager>());
            SetRef(torch, "button", torchButton);
            SetRef(torch, "label", torchButton.GetComponentInChildren<Text>());
            UnityEventTools.AddVoidPersistentListener(torchButton.onClick, torch.Toggle);

            // 历史入口（计划 006 4.6）：只读历史问题列表
            var historyButton = MakeTopBarButton(barRect, "HistoryButton", "历史", -335f, CancelBg, font);

            var settingsButton = MakeTopBarButton(barRect, "SettingsButton", "设置", -115f, CancelBg, font);
            UnityEventTools.AddVoidPersistentListener(settingsButton.onClick, settingsController.Open);
            return historyButton;
        }

        private static Button MakeTopBarButton(RectTransform bar, string name, string label, float xFromRight,
            Color bg, Font font)
        {
            var button = MakeButton(bar, name, label, new Vector2(xFromRight, 0f), new Vector2(190f, 84f), bg, font, 38);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(xFromRight, 0f);
            return button;
        }

        private static Inspection.UI.ToastView BuildToast(Transform parent, Font font)
        {
            var root = UIRect(parent, "ToastRoot", new Vector2(0f, -170f), new Vector2(940f, 110f));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = ToastBg;
            var text = MakeText(root.transform, "Message", font, 40, TextMain, TextAnchor.MiddleCenter);
            StretchPad(text.rectTransform, 20f, 8f);
            text.text = string.Empty;
            var toast = root.gameObject.AddComponent<Inspection.UI.ToastView>();
            SetRef(toast, "root", root.gameObject);
            SetRef(toast, "messageText", text);
            root.gameObject.SetActive(false);
            return toast;
        }

        private static Inspection.UI.MarkerReportButton BuildReportButton(Transform parent, Font font)
        {
            var holder = new GameObject("ReportButtonRoot", typeof(RectTransform));
            Stretch(holder.GetComponent<RectTransform>(), parent);
            var buttonGo = UIRect(holder.transform, "ReportButton", Vector2.zero, new Vector2(420f, 120f));
            MakeButtonOn(buttonGo, "上报此标记", AccentBg, font, 44);
            var reportButton = holder.gameObject.AddComponent<Inspection.UI.MarkerReportButton>();
            SetRef(reportButton, "movable", (RectTransform)buttonGo.transform);
            holder.gameObject.SetActive(false);
            return reportButton;
        }

        private static Inspection.UI.IssueFormController BuildFormPanel(Transform parent, Font font,
            out InputField titleInput, out Dropdown priorityDropdown)
        {
            var root = new GameObject("FormPanelRoot", typeof(RectTransform));
            Stretch(root.GetComponent<RectTransform>(), parent);
            MakeMask(root.transform);

            var card = new GameObject("FormCard", typeof(RectTransform));
            var cardRect = (RectTransform)card.transform;
            cardRect.SetParent(root.transform, false);
            cardRect.anchorMin = new Vector2(0f, 0f);
            cardRect.anchorMax = new Vector2(1f, 0f);
            cardRect.pivot = new Vector2(0.5f, 0f);
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = new Vector2(0f, 1020f);
            card.AddComponent<Image>().color = PanelBg;

            MakeLabel(cardRect, font, "标题（必填）", 36, 60, TextSub);
            titleInput = MakeInput(cardRect, font, "TitleInput", "如：B 区入口墙面裂缝", 112, 92, multiline: false).input;

            MakeLabel(cardRect, font, "描述（可选）", 230, 56, TextSub);
            var descriptionInput = MakeInput(cardRect, font, "DescInput", "补充位置细节、影响范围等", 298, 188, multiline: true).input;

            MakeLabel(cardRect, font, "优先级", 512, 56, TextSub);
            priorityDropdown = MakeDropdown(cardRect, font, 580);

            MakeLabel(cardRect, font, "位置（自动带出，只读）", 700, 56, TextSub);
            var positionText = MakeLabel(cardRect, font, "x 0.000   y 0.000   z 0.000", 768, 80, TextMain);

            var submit = MakeButton(cardRect, "SubmitButton", "提交", new Vector2(-250f, 890f), new Vector2(440f, 96f), AccentBg, font, 44);
            AnchorTopOfCard(submit.transform, 842);
            var cancel = MakeButton(cardRect, "CancelButton", "取消", new Vector2(250f, 890f), new Vector2(440f, 96f), CancelBg, font, 44);
            AnchorTopOfCard(cancel.transform, 842);
            var submitLabel = submit.GetComponentInChildren<Text>();

            var apiClient = root.gameObject.AddComponent<Inspection.API.IssueApiClient>();
            var controller = root.gameObject.AddComponent<Inspection.UI.IssueFormController>();
            SetRef(controller, "panelRoot", root.gameObject);
            SetRef(controller, "titleInput", titleInput);
            SetRef(controller, "descriptionInput", descriptionInput);
            SetRef(controller, "priorityDropdown", priorityDropdown);
            SetRef(controller, "positionText", positionText);
            SetRef(controller, "submitButton", submit);
            SetRef(controller, "submitLabel", submitLabel);
            UnityEventTools.AddVoidPersistentListener(submit.onClick, controller.OnSubmitClicked);
            UnityEventTools.AddVoidPersistentListener(cancel.onClick, controller.OnCancelClicked);

            root.gameObject.SetActive(false);
            return controller;
        }

        /// <summary>
        /// 登录全屏面板（计划 006 4.6）：默认激活（未登录不能进入 AR 主界面），
        /// 控制器与认证客户端挂在常激活的 AppUI 根上（Start 会做会话恢复）；
        /// 卡片上的「后端地址设置」入口直接打开设置面板（设置层级在登录之上）。
        /// </summary>
        private static Inspection.UI.LoginPanelController BuildLoginPanel(Transform parent, Font font,
            Inspection.UI.SettingsPanelController settingsController)
        {
            var root = new GameObject("LoginPanelRoot", typeof(RectTransform));
            Stretch(root.GetComponent<RectTransform>(), parent);
            var bg = root.AddComponent<Image>();
            bg.color = SolidBg; // 不透明：未登录完全遮挡 AR 主界面
            bg.raycastTarget = true;

            var card = UIRect(root.transform, "LoginCard", Vector2.zero, new Vector2(900f, 1080f));
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
            card.AddComponent<Image>().color = PanelBg;

            var title = MakeLabel(cardRect, font, "现场巡检问题标注", 60, 80, TextMain);
            title.fontSize = 52;
            title.alignment = TextAnchor.MiddleCenter;
            var subtitle = MakeLabel(cardRect, font, "巡检端 · 请使用巡检员账号登录", 150, 56, TextSub);
            subtitle.alignment = TextAnchor.MiddleCenter;

            MakeLabel(cardRect, font, "用户名", 230, 52, TextSub);
            var usernameInput = MakeInput(cardRect, font, "UsernameInput", "inspector", 300, 92, multiline: false).input;
            MakeLabel(cardRect, font, "密码", 430, 52, TextSub);
            var passwordInput = MakeInput(cardRect, font, "PasswordInput", "巡检员账号密码", 500, 92, multiline: false).input;
            passwordInput.contentType = InputField.ContentType.Password;

            var notice = MakeLabel(cardRect, font, "请登录巡检员账号", 630, 120, TextWarning);
            notice.alignment = TextAnchor.MiddleCenter;
            notice.fontSize = 36;

            var login = MakeButton(cardRect, "LoginButton", "登录", new Vector2(0f, 790f), new Vector2(920f, 96f), AccentBg, font, 44);
            AnchorTopOfCard(login.transform, 790);
            var loginLabel = login.GetComponentInChildren<Text>();
            var settingsEntry = MakeButton(cardRect, "SettingsEntry", "后端地址设置", new Vector2(0f, 950f), new Vector2(420f, 84f), CancelBg, font, 38);
            AnchorTopOfCard(settingsEntry.transform, 950);

            // 控制器与认证客户端挂在常激活的 AppUI 根上：面板本体默认激活（挡住主界面），
            // Start 的会话恢复逻辑必须随场景启动执行
            var authClient = parent.gameObject.AddComponent<Inspection.API.AuthApiClient>();
            var controller = parent.gameObject.AddComponent<Inspection.UI.LoginPanelController>();
            SetRef(controller, "panelRoot", root.gameObject);
            SetRef(controller, "apiClient", authClient);
            SetRef(controller, "usernameInput", usernameInput);
            SetRef(controller, "passwordInput", passwordInput);
            SetRef(controller, "loginButton", login);
            SetRef(controller, "loginLabel", loginLabel);
            SetRef(controller, "noticeText", notice);
            UnityEventTools.AddVoidPersistentListener(login.onClick, controller.OnLoginClicked);
            UnityEventTools.AddVoidPersistentListener(settingsEntry.onClick, settingsController.Open);
            root.gameObject.SetActive(true);
            return controller;
        }

        /// <summary>历史问题列表面板（计划 006 4.6）：全屏只读列表 + 头部（账号/刷新/退出/关闭）。</summary>
        private static Inspection.UI.HistoryPanelController BuildHistoryPanel(Transform parent, Font font,
            Inspection.UI.LoginPanelController loginPanel)
        {
            var root = new GameObject("HistoryPanelRoot", typeof(RectTransform));
            Stretch(root.GetComponent<RectTransform>(), parent);
            var bg = root.AddComponent<Image>();
            bg.color = SolidBg;
            bg.raycastTarget = true;

            // 头部：标题/账号 + 刷新/退出/关闭 + 状态行
            var header = new GameObject("Header", typeof(RectTransform));
            var headerRect = (RectTransform)header.transform;
            headerRect.SetParent(root.transform, false);
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -360f);
            headerRect.offsetMax = Vector2.zero;
            header.AddComponent<Image>().color = PanelBg;

            var title = MakeLabel(headerRect, font, "历史上报", 40, 80, TextMain);
            title.fontSize = 52;
            var accountText = MakeLabel(headerRect, font, "当前账号：", 140, 60, TextSub);
            var statusText = MakeLabel(headerRect, font, string.Empty, 230, 96, TextWarning);
            statusText.fontSize = 36;

            var refresh = MakeButton(headerRect, "RefreshButton", "刷新", new Vector2(-560f, 150f), new Vector2(300f, 88f), AccentBg, font, 40);
            AnchorTopOfCard(refresh.transform, 110);
            var refreshLabel = refresh.GetComponentInChildren<Text>();
            var logout = MakeButton(headerRect, "LogoutButton", "退出登录", new Vector2(-230f, 150f), new Vector2(300f, 88f), CancelBg, font, 40);
            AnchorTopOfCard(logout.transform, 110);
            var close = MakeButton(headerRect, "CloseButton", "关闭", new Vector2(370f, 60f), new Vector2(240f, 88f), CancelBg, font, 40);
            AnchorTopOfCard(close.transform, 40);

            // 滚动列表：头部以下整屏
            var scrollGo = new GameObject("ScrollView", typeof(RectTransform));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(root.transform, false);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0f, -360f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 40f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetParent(scrollRect, false);
            Stretch(viewportRect, scrollRect);
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.verticalNormalizedPosition = 1f;

            var template = BuildHistoryItemTemplate(contentRect, font);

            var historyClient = parent.gameObject.AddComponent<Inspection.API.HistoryApiClient>();
            var authClient = parent.gameObject.GetComponent<Inspection.API.AuthApiClient>();
            var controller = parent.gameObject.AddComponent<Inspection.UI.HistoryPanelController>();
            SetRef(controller, "panelRoot", root.gameObject);
            SetRef(controller, "apiClient", historyClient);
            SetRef(controller, "authClient", authClient);
            SetRef(controller, "loginPanel", loginPanel);
            SetRef(controller, "accountText", accountText);
            SetRef(controller, "refreshButton", refresh);
            SetRef(controller, "refreshLabel", refreshLabel);
            SetRef(controller, "statusText", statusText);
            SetRef(controller, "listContent", contentRect);
            SetRef(controller, "itemTemplate", template);
            UnityEventTools.AddVoidPersistentListener(refresh.onClick, controller.OnRefreshClicked);
            UnityEventTools.AddVoidPersistentListener(logout.onClick, controller.OnLogoutClicked);
            UnityEventTools.AddVoidPersistentListener(close.onClick, controller.OnCloseClicked);

            root.gameObject.SetActive(false);
            return controller;
        }

        /// <summary>历史条目模板（固定高度卡片：标题 / 优先级+状态 / 描述 / 坐标 / 时间）。</summary>
        private static GameObject BuildHistoryItemTemplate(RectTransform content, Font font)
        {
            var item = new GameObject("HistoryItemTemplate", typeof(RectTransform));
            var rect = (RectTransform)item.transform;
            rect.SetParent(content, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 380f);
            var bg = item.AddComponent<Image>();
            bg.color = InputBg;

            var titleText = MakeLabel(rect, font, "标题", 16, 60, TextMain);
            titleText.fontSize = 42;
            var metaText = MakeLabel(rect, font, "优先级 · 状态", 92, 52, TextSub);
            metaText.fontSize = 36;
            var descText = MakeLabel(rect, font, "描述", 158, 96, TextSub);
            descText.fontSize = 34;
            var positionText = MakeLabel(rect, font, "x 0.000   y 0.000   z 0.000", 268, 48, TextSub);
            positionText.fontSize = 32;
            var timeText = MakeLabel(rect, font, "创建 · 更新", 324, 48, TextSub);
            timeText.fontSize = 32;

            item.SetActive(false);
            return item;
        }

        private static Inspection.UI.SettingsPanelController BuildSettingsPanel(Transform parent, Font font)
        {
            var root = new GameObject("SettingsPanelRoot", typeof(RectTransform));
            Stretch(root.GetComponent<RectTransform>(), parent);
            MakeMask(root.transform);

            var card = UIRect(root.transform, "SettingsCard", Vector2.zero, new Vector2(900f, 800f));
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
            card.AddComponent<Image>().color = PanelBg;

            var title = MakeLabel(cardRect, font, "设置", 50, 70, TextMain);
            title.fontSize = 52;
            title.alignment = TextAnchor.MiddleCenter;
            var addressInput = MakeInput(cardRect, font, "AddressInput", "http://192.168.31.17:8080", 150, 100, multiline: false).input;

            var hint = MakeLabel(cardRect, font,
                "后端地址：填写电脑上运行后端服务的局域网地址（手机与电脑需同一 Wi-Fi）；保存后立即生效。",
                290, 120, TextSub);
            hint.alignment = TextAnchor.UpperLeft;
            hint.fontSize = 34;

            // 完整诊断开关（计划 006 4.8）：默认关闭、持久化、即时生效
            var toggle = MakeToggle(cardRect, font, "FullDiagnosticsToggle", "完整诊断（仅排错时开启）", 430);
            var diagHint = MakeLabel(cardRect, font,
                "开启后会记录设备日志；放置诊断包含实景截图与坐标。默认关闭，重启保留开关状态。",
                540, 130, TextSub);
            diagHint.alignment = TextAnchor.UpperLeft;
            diagHint.fontSize = 32;

            var save = MakeButton(cardRect, "SaveButton", "保存", new Vector2(-230f, 660f), new Vector2(420f, 96f), AccentBg, font, 44);
            AnchorTopOfCard(save.transform, 660);
            var close = MakeButton(cardRect, "CloseButton", "关闭", new Vector2(230f, 660f), new Vector2(420f, 96f), CancelBg, font, 44);
            AnchorTopOfCard(close.transform, 660);

            // 控制器挂在常激活的 AppUI 根上：挂在默认不激活的面板物体上 Start 永不执行（首启引导会失效）
            var controller = parent.gameObject.AddComponent<Inspection.UI.SettingsPanelController>();
            SetRef(controller, "panelRoot", root.gameObject);
            SetRef(controller, "addressInput", addressInput);
            SetRef(controller, "fullDiagnosticsToggle", toggle);
            UnityEventTools.AddVoidPersistentListener(save.onClick, controller.OnSaveClicked);
            UnityEventTools.AddVoidPersistentListener(close.onClick, controller.OnCloseClicked);
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
            rect.anchoredPosition = new Vector2(20f, -126f);
            rect.sizeDelta = new Vector2(1040f, 128f);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 24;
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
            if (prop == null || !prop.isArray || prop.arraySize != values.Length)
            {
                throw new InvalidOperationException($"{component.GetType().Name} 缺少数组序列化字段 {field} 或长度不匹配");
            }
            for (var i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
