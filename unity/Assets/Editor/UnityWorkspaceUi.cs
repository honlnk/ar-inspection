using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using Inspection.UI;

namespace Inspection.EditorTools
{
    /// <summary>幂等 uGUI 构建原语。布局按安全区容器计算，触控尺寸按 1080px 竖屏参考值设置。</summary>
    internal static class UnityWorkspaceUi
    {
        internal const float ContentWidth = 984f;
        internal static Font Font;

        /// <summary>
        /// 全屏容器：根节点始终铺满屏幕并承载背景，返回值就是根节点——面板显隐必须作用于它，
        /// 否则只关掉内容层、外层不透明背景仍会盖住其它面板。
        /// 安全区只作用于内层 content，背景保持全屏（避免状态栏区域透出 AR 实景）。
        /// </summary>
        internal static RectTransform Full(Transform parent, string name, Color background, bool safeArea)
        {
            return Full(parent, name, background, safeArea, out _);
        }

        internal static RectTransform Full(Transform parent, string name, Color background, bool safeArea,
            out RectTransform content)
        {
            var root = Rect(parent, name, Vector2.zero, Vector2.zero);
            Stretch(root);
            if (background.a > 0f)
            {
                Surface(root, background);
            }
            content = root;
            if (safeArea)
            {
                content = Rect(root, "SafeAreaContent", Vector2.zero, Vector2.zero);
                Stretch(content);
                root.gameObject.AddComponent<SafeAreaFitter>();
            }
            return root;
        }

        internal static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static void StretchPad(RectTransform rect, float x, float y)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(x, y);
            rect.offsetMax = new Vector2(-x, -y);
        }

        internal static void Top(RectTransform rect, float y, float height, float horizontalMargin = 48f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(horizontalMargin, 0f);
            rect.offsetMax = new Vector2(-horizontalMargin, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(0f, -y);
        }

        internal static void Bottom(RectTransform rect, float y, float height, float horizontalMargin = 48f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(horizontalMargin, y);
            rect.offsetMax = new Vector2(-horizontalMargin, y + height);
        }

        internal static Image Surface(Transform parent, Color color, bool raycast = true)
        {
            var image = parent.gameObject.GetComponent<Image>();
            if (image == null)
            {
                image = parent.gameObject.AddComponent<Image>();
            }
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>为 Image 增加运行时圆角（纹理运行时生成，不引入外部美术资源）。</summary>
        internal static void Round(Image image, float radius)
        {
            if (image == null)
            {
                return;
            }
            var rounded = image.GetComponent<Inspection.UI.RoundedImage>();
            if (rounded == null)
            {
                rounded = image.gameObject.AddComponent<Inspection.UI.RoundedImage>();
            }
            rounded.Radius = radius;
        }

        internal static Text Text(Transform parent, string name, string content, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.zero);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        internal static Text Label(Transform parent, string name, string content, float y, float height,
            Color color, int size = UiVisualStyle.BodyFontSize, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var text = Text(parent, name, content, size, color, alignment);
            Top(text.rectTransform, y, height);
            return text;
        }

        internal static Button Button(Transform parent, string name, string text, Vector2 position, Vector2 size,
            Color background, Color foreground, bool primary = false)
        {
            var rect = Rect(parent, name, position, size);
            return ButtonOn(rect, text, background, foreground, primary);
        }

        internal static Button ButtonOn(RectTransform rect, string text, Color background, Color foreground, bool primary)
        {
            var image = Surface(rect, background);
            Round(image, UiVisualStyle.ControlRadius);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.95f, 0.97f, 1f);
            colors.pressedColor = primary ? new Color(0.72f, 0.77f, 0.92f) : new Color(0.89f, 0.92f, 0.96f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.65f, 0.69f, 0.75f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            var label = Text(rect, "Label", text, UiVisualStyle.ButtonFontSize, foreground, TextAnchor.MiddleCenter);
            StretchPad(label.rectTransform, 12f, 4f);
            return button;
        }

        internal static Button HeaderButton(Transform parent, string name, string text, float right, float width = 184f)
        {
            var button = Button(parent, name, text, new Vector2(-right, 0f), new Vector2(width, 136f),
                new Color(1f, 1f, 1f, 0.14f), Color.white);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-right, 0f);
            return button;
        }

        internal static RectTransform Card(Transform parent, string name, float top, float height)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.zero);
            Top(rect, top, height);
            var image = Surface(rect, UiVisualStyle.Surface);
            Round(image, UiVisualStyle.CardRadius);
            return rect;
        }

        internal static InputField Input(Transform parent, string name, string placeholder, float y, float height,
            bool multiline, bool password = false)
        {
            var root = Rect(parent, name, Vector2.zero, Vector2.zero);
            Top(root, y, height);
            var image = Surface(root, UiVisualStyle.Surface);
            Round(image, UiVisualStyle.ControlRadius);
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = UiVisualStyle.Border;
            outline.effectDistance = new Vector2(2f, -2f);
            var text = Text(root, "Text", string.Empty, UiVisualStyle.BodyFontSize, UiVisualStyle.Ink,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            StretchPad(text.rectTransform, 24f, 12f);
            text.raycastTarget = true;
            text.supportRichText = false;
            var hint = Text(root, "Placeholder", placeholder, UiVisualStyle.BodyFontSize, UiVisualStyle.Secondary,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            StretchPad(hint.rectTransform, 24f, 12f);
            var input = root.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.caretColor = UiVisualStyle.Primary;
            input.selectionColor = new Color(0.20f, 0.35f, 0.65f, 0.35f);
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            return input;
        }

        internal static Toggle Toggle(Transform parent, string name, string text, float y, float height)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.zero);
            Top(rect, y, height);
            var image = Surface(rect, UiVisualStyle.Surface);
            Round(image, UiVisualStyle.ControlRadius);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = UiVisualStyle.Border;
            outline.effectDistance = new Vector2(2f, -2f);
            // 关闭态仍要有可见方框：勾选块由 Toggle 淡出，边框必须独立保留
            var frame = Rect(rect, "CheckFrame", Vector2.zero, new Vector2(56f, 56f));
            frame.anchorMin = frame.anchorMax = new Vector2(0f, 0.5f);
            frame.anchoredPosition = new Vector2(48f, 0f);
            var frameImage = Surface(frame, UiVisualStyle.Surface, false);
            Round(frameImage, UiVisualStyle.TagRadius);
            var frameOutline = frame.gameObject.AddComponent<Outline>();
            frameOutline.effectColor = UiVisualStyle.Border;
            frameOutline.effectDistance = new Vector2(2f, -2f);
            var check = Rect(rect, "Checkmark", Vector2.zero, new Vector2(44f, 44f));
            check.anchorMin = check.anchorMax = new Vector2(0f, 0.5f);
            check.anchoredPosition = new Vector2(48f, 0f);
            var checkImage = Surface(check, UiVisualStyle.Primary, false);
            Round(checkImage, UiVisualStyle.TagRadius);
            var label = Text(rect, "Label", text, UiVisualStyle.BodyFontSize, UiVisualStyle.Ink);
            StretchPad(label.rectTransform, 132f, 0f);
            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = image;
            toggle.graphic = checkImage;
            toggle.isOn = false;
            return toggle;
        }

        internal static ScrollRect Scroll(Transform parent, string name, out RectTransform content, bool keyboardInset = false)
        {
            var root = Rect(parent, name, Vector2.zero, Vector2.zero);
            Stretch(root);
            if (keyboardInset)
            {
                root.gameObject.AddComponent<KeyboardInset>();
            }
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            var viewport = Rect(root, "Viewport", Vector2.zero, Vector2.zero);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect(viewport, "Content", Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            // 内容高度由内容自身声明：绝对定位用手工高度（ScrolledContent），
            // 或用 VerticalLayoutGroup（Flow）自动计算。两者都没有时 fitter 会算出 0 高。
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalNormalizedPosition = 1f;
            return scroll;
        }

        internal static void Flow(RectTransform content, float top, float bottom, float gap)
        {
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, Mathf.RoundToInt(top), Mathf.RoundToInt(bottom));
            layout.spacing = gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        internal static void FlowItem(Component component, float height)
        {
            var element = component.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        /// <summary>
        /// 滚动内容高度：内容子元素是用 Top() 绝对定位手工摆放的，没有 VerticalLayoutGroup，
        /// ContentSizeFitter 算不出 preferredHeight 会把内容压成 0 高（整页空白）。
        /// 因此显式声明高度，由 Scroll() 优先采用该值而非让 fitter 自行计算。
        /// </summary>
        internal static void ScrolledContent(RectTransform content, float height)
        {
            var element = content.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = content.gameObject.AddComponent<LayoutElement>();
            }
            element.preferredHeight = height;
            element.minHeight = height;
            element.flexibleHeight = -1f;
            content.sizeDelta = new Vector2(0f, height);
        }

        internal static void SetRef(Component component, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException($"{component.GetType().Name} 缺少序列化字段 {field}");
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void Click(Button button, UnityEngine.Events.UnityAction callback)
        {
            UnityEventTools.AddVoidPersistentListener(button.onClick, callback);
        }

        internal static void SelectPriority(Button button, PrioritySelector selector, int value)
        {
            UnityEventTools.AddIntPersistentListener(button.onClick, selector.Select, value);
        }

        internal static void Mask(Transform root)
        {
            var mask = Rect(root, "Mask", Vector2.zero, Vector2.zero);
            Stretch(mask);
            Surface(mask, new Color(0.04f, 0.06f, 0.11f, 0.60f));
        }

        internal static RectTransform Section(Transform parent, string name, string title, float y, float height)
        {
            var card = Card(parent, name, y, height);
            Label(card, "SectionTitle", title, 20f, 72f, UiVisualStyle.Ink, 46);
            return card;
        }
    }
}
