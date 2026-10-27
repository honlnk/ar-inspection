using UnityEngine;

namespace Inspection.UI
{
    /// <summary>
    /// 「上报此标记」跟随按钮：草稿标记在屏幕内时显示于其屏幕投影上方并随镜头实时更新，
    /// 出屏隐藏、回屏恢复（计划 4.2）；点击打开上报表单。本物体属 UI，触摸不触发放置。
    /// 注意：显隐分两级——本组件物体在草稿存在期间保持激活（保证 LateUpdate 持续跟随），
    /// 内部 movable 子物体按可见性切换，避免物体停用后跟随逻辑失效。
    /// </summary>
    public class MarkerReportButton : MonoBehaviour
    {
        [SerializeField] private IssueFormController formController;
        [SerializeField] private RectTransform movable;

        /// <summary>按钮中心相对标记投影点的向上偏移（Screen Space Overlay 画布 1:1 像素）。</summary>
        public const float OffsetAboveMarkerPixels = 100f;
        public const float ScreenMarginPixels = 12f;

        private Transform target;
        private Camera arCamera;
        private bool lastVisible;
        private Vector2 lastScreenPos;

        /// <summary>设置跟随目标；null 表示无草稿，按钮整体隐藏。</summary>
        public void SetTarget(Transform markerTransform)
        {
            target = markerTransform;
            gameObject.SetActive(markerTransform != null);
            if (markerTransform != null && movable != null)
            {
                movable.gameObject.SetActive(true);
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                lastVisible = false;
                return;
            }
            if (arCamera == null)
            {
                arCamera = Camera.main;
            }
            if (arCamera == null)
            {
                lastVisible = false;
                return;
            }
            var screenPos = ComputeButtonScreenPosition(
                target.position, arCamera, Screen.width, Screen.height, out var visible);
            lastVisible = visible;
            lastScreenPos = screenPos;
            movable.gameObject.SetActive(visible);
            if (visible)
            {
                movable.position = screenPos;
            }
        }

        /// <summary>调试条诊断状态：激活/目标/相机/最近一次可见性与屏幕坐标（开发构建展示）。</summary>
        public string DiagState =>
            (movable != null && movable.gameObject.activeInHierarchy ? "活" : "停")
            + $"目标{(target != null ? "有" : "无")}相机{(arCamera != null ? "有" : "无")}"
            + $"显{(lastVisible ? "是" : "否")}({lastScreenPos.x:F0},{lastScreenPos.y:F0})";

        /// <summary>
        /// 纯几何逻辑（供 EditMode 测试）：
        /// 标记在相机前方且投影落在屏幕内 → 可见，按钮位置为其上方偏移并做边界约束；
        /// 否则不可见（出屏隐藏）。
        /// </summary>
        public static Vector3 ComputeButtonScreenPosition(
            Vector3 worldPosition, Camera camera, int screenWidth, int screenHeight, out bool visible)
        {
            var viewport = camera.WorldToViewportPoint(worldPosition);
            visible = viewport.z > 0f
                && viewport.x >= 0f && viewport.x <= 1f
                && viewport.y >= 0f && viewport.y <= 1f;
            if (!visible)
            {
                return Vector3.zero;
            }
            var x = Mathf.Clamp(viewport.x * screenWidth, ScreenMarginPixels, screenWidth - ScreenMarginPixels);
            var y = Mathf.Min(viewport.y * screenHeight + OffsetAboveMarkerPixels, screenHeight - ScreenMarginPixels);
            return new Vector3(x, y, 0f);
        }

        /// <summary>按钮点击（编辑器绑定）：打开表单面板。</summary>
        public void OnReportButtonClicked()
        {
            formController.OpenForDraft();
        }
    }
}
