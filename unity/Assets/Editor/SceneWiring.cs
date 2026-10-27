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
    /// 计划 004 阶段 2～5 的场景接线：创建标记/平面可视化 Prefab 与材质、搭建全部 UI
    /// （顶部栏、Toast、跟随上报按钮、表单面板、设置面板，均含全屏蒙层）、挂运行时脚本并接线。
    /// 幂等：重复执行时先销毁既有 AppUI 重建，Prefab 与材质存在则覆盖重存。
    /// 计划 005 起，执行前先经 ProjectSetup.EnsureArcameraInput 统一 AR 输入链路
    /// （输入管理器 + Input System 姿态驱动绑定），保存后复检，避免重建内容时遗漏相机配置。
    /// 菜单「现场巡检 / 搭建运行时内容（004）」，批处理 -executeMethod 同名可用。
    /// </summary>
    public static class SceneWiring
    {
        private const string ScenePath = "Assets/Scenes/Inspection.unity";
        private const string MarkerPrefabPath = "Assets/Prefabs/IssueMarker.prefab";
        private const string PlanePrefabPath = "Assets/Prefabs/PlaneVisualizer.prefab";

        // 配色（深色高对比，AR 实景上可读）
        private static readonly Color PanelBg = new Color(0.09f, 0.11f, 0.16f, 0.96f);
        private static readonly Color TopBarBg = new Color(0.05f, 0.06f, 0.09f, 0.85f);
        private static readonly Color ToastBg = new Color(0.05f, 0.06f, 0.09f, 0.92f);
        private static readonly Color MaskBg = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color InputBg = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color AccentBg = new Color(0.15f, 0.39f, 0.92f, 1f);
        private static readonly Color CancelBg = new Color(0.22f, 0.25f, 0.32f, 1f);
        private static readonly Color TextMain = Color.white;
        private static readonly Color TextSub = new Color(0.72f, 0.75f, 0.80f, 1f);
        private static readonly Color TextPlaceholder = new Color(1f, 1f, 1f, 0.4f);

        [MenuItem("现场巡检/搭建运行时内容（004：Prefab + UI + 脚本接线）")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 计划 005：搭建前先统一 AR 输入链路（AR 输入管理器 + Input System 姿态驱动绑定），
            // 与工程初始化共用 ProjectSetup.EnsureArcameraInput，不能只重建 UI 而遗漏相机配置
            ProjectSetup.EnsureArcameraInput();

            var markerPrefab = BuildMarkerPrefab();
            var planePrefab = BuildPlaneVisualizerPrefab();
            WirePlaneManager(planePrefab);

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                throw new InvalidOperationException("场景缺少 Canvas，先执行「初始化工程」");
            }
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildAppUi(canvas.transform, font, markerPrefab);

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
            Debug.Log("[SceneWiring] 004 运行时内容搭建完成：Prefab、材质、UI 与脚本接线已保存。");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        // ---------- Prefab 与材质 ----------

        private static GameObject BuildMarkerPrefab()
        {
            Directory.CreateDirectory("Assets/Materials");
            var markerMat = LoadOrCreateMaterial("Assets/Materials/IssueMarker.mat", "Standard",
                new Color(1f, 0.82f, 0.10f, 1f));
            markerMat.EnableKeyword("_EMISSION");
            markerMat.SetColor("_EmissionColor", new Color(0.6f, 0.45f, 0.02f));
            EditorUtility.SetDirty(markerMat);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "IssueMarker";
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>()); // AR 射线不用碰撞体
            cube.transform.localScale = Vector3.one * 0.15f;
            cube.GetComponent<MeshRenderer>().sharedMaterial = markerMat;
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

        private static void BuildAppUi(Transform canvasTransform, Font font, GameObject markerPrefab)
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

            var toast = BuildToast(appUi.transform, font);
            var formController = BuildFormPanel(appUi.transform, font, out var titleInput);
            var settingsController = BuildSettingsPanel(appUi.transform, font);
            var reportButton = BuildReportButton(appUi.transform, font);

            BuildTopBar(appUi.transform, font, settingsController);
            var overlay = BuildDevOverlay(appUi.transform, font);

            // 交叉接线（面板/按钮 → 控制器）
            SetRef(formController, "toast", toast);
            SetRef(formController, "apiClient", ((Component)formController).GetComponent<Inspection.API.IssueApiClient>());
            SetRef(reportButton, "formController", formController);
            UnityEventTools.AddVoidPersistentListener(
                ((Component)reportButton).GetComponentInChildren<Button>().onClick, reportButton.OnReportButtonClicked);
            UnityEventTools.AddVoidPersistentListener(titleInput.onValueChanged, formController.OnTitleChanged);

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
            SetRef(placement, "markerPrefab", markerPrefab);
            SetRef(placement, "toast", toast);
            var diagnostics = xrOrigin.GetComponent<Inspection.AR.PlacementDiagnostics>();
            if (diagnostics == null)
            {
                diagnostics = xrOrigin.gameObject.AddComponent<Inspection.AR.PlacementDiagnostics>();
            }
            SetRef(placement, "diagnostics", diagnostics);
            SetRef(formController, "placement", placement);

            // 调试条接线：标记与按钮诊断信息来源
            SetRef(overlay, "placement", placement);
            SetRef(overlay, "reportButton", reportButton);
        }

        private static void BuildTopBar(Transform parent, Font font, Inspection.UI.SettingsPanelController settingsController)
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
            statusRect.offsetMax = new Vector2(-680f, 0f);
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
            var resetButton = MakeButton(barRect, "ResetButton", "重置",
                new Vector2(-555f, 0f), new Vector2(190f, 84f), CancelBg, font, 38);
            resetButton.transform.localScale = Vector3.one;
            var resetRect = (RectTransform)resetButton.transform;
            resetRect.anchorMin = resetRect.anchorMax = new Vector2(1f, 0.5f);
            var session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (session == null)
            {
                throw new InvalidOperationException("场景缺少 ARSession，无法接线重置按钮");
            }
            UnityEventTools.AddVoidPersistentListener(resetButton.onClick, session.Reset);

            // 手电开关（用户授权新增）：暗光环境下辅助平面检测
            var torchButton = MakeButton(barRect, "TorchButton", "手电 关",
                new Vector2(-335f, 0f), new Vector2(190f, 84f), CancelBg, font, 38);
            var torchButtonRect = (RectTransform)torchButton.transform;
            torchButtonRect.anchorMin = torchButtonRect.anchorMax = new Vector2(1f, 0.5f);
            var torch = torchButton.gameObject.AddComponent<Inspection.AR.TorchController>();
            SetRef(torch, "cameraManager", UnityEngine.Object.FindFirstObjectByType<ARCameraManager>());
            SetRef(torch, "button", torchButton);
            SetRef(torch, "label", torchButton.GetComponentInChildren<Text>());
            UnityEventTools.AddVoidPersistentListener(torchButton.onClick, torch.Toggle);

            var settingsButton = MakeButton(barRect, "SettingsButton", "设置",
                new Vector2(-115f, 0f), new Vector2(190f, 84f), CancelBg, font, 38);
            var settingsButtonRect = (RectTransform)settingsButton.transform;
            settingsButtonRect.anchorMin = settingsButtonRect.anchorMax = new Vector2(1f, 0.5f);
            UnityEventTools.AddVoidPersistentListener(settingsButton.onClick, settingsController.Open);
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

        private static Inspection.UI.IssueFormController BuildFormPanel(Transform parent, Font font, out InputField titleInput)
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
            var dropdown = MakeDropdown(cardRect, font, 580);

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
            SetRef(controller, "priorityDropdown", dropdown);
            SetRef(controller, "positionText", positionText);
            SetRef(controller, "submitButton", submit);
            SetRef(controller, "submitLabel", submitLabel);
            UnityEventTools.AddVoidPersistentListener(submit.onClick, controller.OnSubmitClicked);
            UnityEventTools.AddVoidPersistentListener(cancel.onClick, controller.OnCancelClicked);

            root.gameObject.SetActive(false);
            return controller;
        }

        private static Inspection.UI.SettingsPanelController BuildSettingsPanel(Transform parent, Font font)
        {
            var root = new GameObject("SettingsPanelRoot", typeof(RectTransform));
            Stretch(root.GetComponent<RectTransform>(), parent);
            MakeMask(root.transform);

            var card = UIRect(root.transform, "SettingsCard", Vector2.zero, new Vector2(900f, 600f));
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
            card.AddComponent<Image>().color = PanelBg;

            var title = MakeLabel(cardRect, font, "后端地址设置", 50, 70, TextMain);
            title.fontSize = 52;
            title.alignment = TextAnchor.MiddleCenter;
            var addressInput = MakeInput(cardRect, font, "AddressInput", "http://192.168.31.17:8080", 150, 100, multiline: false).input;

            var hint = MakeLabel(cardRect, font,
                "填写电脑上运行后端服务的局域网地址（手机与电脑需同一 Wi-Fi）；保存后立即生效。",
                280, 140, TextSub);
            hint.alignment = TextAnchor.UpperLeft;
            hint.fontSize = 36;

            var save = MakeButton(cardRect, "SaveButton", "保存", new Vector2(-230f, 450f), new Vector2(420f, 96f), AccentBg, font, 44);
            AnchorTopOfCard(save.transform, 450);
            var close = MakeButton(cardRect, "CloseButton", "关闭", new Vector2(230f, 450f), new Vector2(420f, 96f), CancelBg, font, 44);
            AnchorTopOfCard(close.transform, 450);

            // 控制器挂在常激活的 AppUI 根上：挂在默认不激活的面板物体上 Start 永不执行（首启引导会失效）
            var controller = parent.gameObject.AddComponent<Inspection.UI.SettingsPanelController>();
            SetRef(controller, "panelRoot", root.gameObject);
            SetRef(controller, "addressInput", addressInput);
            UnityEventTools.AddVoidPersistentListener(save.onClick, controller.OnSaveClicked);
            UnityEventTools.AddVoidPersistentListener(close.onClick, controller.OnCloseClicked);

            root.gameObject.SetActive(false);
            return controller;
        }

        /// <summary>开发构建调试信息条（两行：会话/平面/点击 + 标记/按钮诊断；正式构建自动隐藏）。</summary>
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
            image.color = MaskBg;
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
    }
}
