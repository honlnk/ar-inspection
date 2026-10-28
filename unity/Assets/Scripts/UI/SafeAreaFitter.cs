using UnityEngine;

namespace Inspection.UI
{
    /// <summary>仅调整已创建的安全区内容容器；根背景始终铺满屏幕，不搬迁运行时 UI 树。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform content;
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        private void Awake()
        {
            content = (RectTransform)transform.Find("SafeAreaContent");
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != lastSafeArea || Screen.width != lastScreen.x || Screen.height != lastScreen.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (content == null)
            {
                return;
            }
            var area = Screen.safeArea;
            content.anchorMin = new Vector2(area.xMin / Mathf.Max(1, Screen.width), area.yMin / Mathf.Max(1, Screen.height));
            content.anchorMax = new Vector2(area.xMax / Mathf.Max(1, Screen.width), area.yMax / Mathf.Max(1, Screen.height));
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            lastSafeArea = area;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
        }
    }
}
