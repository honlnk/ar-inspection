using UnityEngine;

namespace Inspection.UI
{
    /// <summary>软键盘弹起时缩小可用布局；关闭后恢复全屏，不影响未聚焦的 AR 操作。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class KeyboardInset : MonoBehaviour
    {
        private RectTransform target;

        private void Awake()
        {
            target = (RectTransform)transform;
        }

        private void OnEnable()
        {
            Apply(false);
        }

        private void OnDisable()
        {
            Apply(false);
        }

        private void Update()
        {
            Apply(TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible);
        }

        public static float BottomAnchor(int screenHeight, float keyboardTop)
        {
            return screenHeight <= 0 ? 0f : Mathf.Clamp01(keyboardTop / screenHeight);
        }

        private void Apply(bool keyboardVisible)
        {
            if (target == null)
            {
                return;
            }
            var inset = keyboardVisible ? TouchScreenKeyboard.area.y : 0f;
            target.offsetMin = new Vector2(0f, Mathf.Max(0f, inset));
        }
    }
}
