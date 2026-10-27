using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Inspection.UI;

namespace Inspection.AR
{
    /// <summary>
    /// 点击放置控制器（AC-01/AC-02 核心）：
    /// tap 判定（旧输入 Input.touches）→ UI 防误触 → ARRaycastManager 平面射线 →
    /// ARAnchorManager 平面附锚 → 实例化标记 Prefab；同一时刻至多一个草稿标记。
    /// 阈值按计划 4.1：单指按下到抬起 ≤ 500ms 且累计位移 ≤ 24px 视为 tap。
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        [SerializeField] private IssueFormController formController;
        [SerializeField] private MarkerReportButton reportButton;
        [SerializeField] private SettingsPanelController settingsPanel;
        [SerializeField] private GameObject markerPrefab;
        [SerializeField] private UI.ToastView toast;
        [SerializeField] private PlacementDiagnostics diagnostics;

        /// <summary>tap 判定阈值：时长上限（秒）与累计位移上限（像素，参照 1080×2400 屏）。</summary>
        public const float MaxTapDurationSeconds = 0.5f;
        public const float MaxTapMovementPixels = 24f;

        /// <summary>
        /// 放置距离上限（米）：真机实测 ARCore 跟踪漂移会产生映射到数米外的伪平面，
        /// 点击可见桌面却命中远处平面（标记 8m 外不可见）。超过该距离的命中一律拒绝并提示重置会话。
        /// </summary>
        public const float MaxPlacementDistanceMeters = 2f;

        private ARRaycastManager raycastManager;
        private ARAnchorManager anchorManager;
        private static readonly System.Collections.Generic.List<ARRaycastHit> Hits = new System.Collections.Generic.List<ARRaycastHit>();

        private ARAnchor currentAnchor;
        private GameObject currentMarker;

        // tap 追踪状态；null 表示当前无有效按下（被取消/未开始/落在 UI 上）
        private int? trackedFingerId;
        private float trackStartTime;
        private Vector2 trackStartPos;

        /// <summary>最近一次点击链路的判定结果（开发构建调试条展示；正式构建无消费方，开销可忽略）。</summary>
        public static string LastTapInfo = "无点击";

        /// <summary>当前是否存在草稿标记。</summary>
        public bool HasDraft => currentMarker != null;

        /// <summary>当前草稿标记的变换（调试条诊断用；锚点被系统移除等情况会变回 null）。</summary>
        public Transform MarkerTransform => currentMarker != null ? currentMarker.transform : null;

        /// <summary>草稿标记锚定时的世界坐标（表单位置只读行取值）；无草稿为 null。</summary>
        public Vector3? DraftWorldPosition => currentMarker != null ? currentMarker.transform.position : (Vector3?)null;

        private void Awake()
        {
            raycastManager = GetComponent<ARRaycastManager>();
            anchorManager = GetComponent<ARAnchorManager>();
        }

        private void Update()
        {
            if (Input.touchCount == 0)
            {
                return;
            }
            // 多指触摸一律不触发放置（计划 4.4 第 5 条）：出现第二根手指即取消当前判定
            if (Input.touchCount > 1)
            {
                trackedFingerId = null;
                return;
            }

            var touch = Input.GetTouch(0);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    trackedFingerId = null;
                    // 面板（表单/设置）打开即冻结放置（计划 4.4 第 3 条）；键盘弹起时触摸落在蒙层/输入区，同样被拦
                    if (AnyPanelOpen())
                    {
                        LastTapInfo = "面板打开，冻结放置";
                        break;
                    }
                    // 触点落在任何 UI 上（按钮/输入框/下拉/蒙层/顶部栏）不进入射线（计划 4.4 第 1 条）
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                    {
                        LastTapInfo = "点击在UI上，不放置";
                        break;
                    }
                    trackedFingerId = touch.fingerId;
                    trackStartTime = Time.unscaledTime;
                    trackStartPos = touch.position;
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    // 拖动超出位移阈值即取消（只在 Ended 时按累计位移判定，中途超限提前作废）
                    if (trackedFingerId == touch.fingerId
                        && (touch.position - trackStartPos).sqrMagnitude > MaxTapMovementPixels * MaxTapMovementPixels)
                    {
                        trackedFingerId = null;
                    }
                    break;

                case TouchPhase.Ended:
                    if (trackedFingerId != touch.fingerId)
                    {
                        break;
                    }
                    trackedFingerId = null;
                    if (!IsTap(Time.unscaledTime - trackStartTime, (touch.position - trackStartPos).sqrMagnitude))
                    {
                        LastTapInfo = "非tap（超时或拖动）";
                        break;
                    }
                    TryPlaceAt(touch.position);
                    break;
            }
        }

        /// <summary>tap 判定纯逻辑（供 EditMode 测试）：时长与累计位移都在阈值内。</summary>
        public static bool IsTap(float durationSeconds, float movementSqrMagnitude)
        {
            return durationSeconds <= MaxTapDurationSeconds
                && movementSqrMagnitude <= MaxTapMovementPixels * MaxTapMovementPixels;
        }

        /// <summary>放置距离校验（供 EditMode 测试）：命中点距相机过远视为伪平面数据。</summary>
        public static bool IsPlacementDistanceValid(float distanceMeters)
        {
            return distanceMeters <= MaxPlacementDistanceMeters;
        }

        /// <summary>
        /// 命中点须在相机前方（供 EditMode 测试）：真机实测出现过距 1.58m（通过距离校验）
        /// 却在相机背后的伪命中（视口 z 为负，画不出来）。留 5cm 余量滤掉贴近视平面的退化命中。
        /// </summary>
        public const float MinForwardDistanceMeters = 0.05f;

        /// <summary>命中点在相机视线方向上的投影深度是否有效。</summary>
        public static bool IsPlacementInFront(Vector3 cameraPosition, Vector3 cameraForward, Vector3 hitPosition)
        {
            return Vector3.Dot(cameraForward, hitPosition - cameraPosition) >= MinForwardDistanceMeters;
        }

        private bool AnyPanelOpen()
        {
            return (formController != null && formController.IsOpen)
                || (settingsPanel != null && settingsPanel.IsOpen);
        }

        private void TryPlaceAt(Vector2 screenPosition)
        {
            if (raycastManager == null || anchorManager == null || markerPrefab == null)
            {
                return;
            }
            // 只对检测出的平面多边形内部命中（计划 4.1）；未命中：无动作、不报错
            if (!raycastManager.Raycast(screenPosition, Hits, TrackableType.PlaneWithinPolygon) || Hits.Count == 0)
            {
                LastTapInfo = "射线未命中平面";
                return;
            }

            var hit = Hits[0];
            // 命中点距相机过远 → 命中的是跟踪漂移产生的伪平面（真机实测案例：8m 外），
            // 拒绝放置并引导重置，避免"已放置标记"但方块在数米外不可见的困惑状态
            var cam = Camera.main;
            if (cam != null)
            {
                var hitDistance = Vector3.Distance(cam.transform.position, hit.pose.position);
                if (!IsPlacementDistanceValid(hitDistance)
                    || !IsPlacementInFront(cam.transform.position, cam.transform.forward, hit.pose.position))
                {
                    LastTapInfo = $"命中异常平面({hitDistance:F1}m)，已拒绝";
                    if (toast != null)
                    {
                        toast.Show("平面数据异常：请点顶部「重置」后重新扫描", 4f);
                    }
                    return;
                }
            }

            // 草稿语义：已有草稿则先销毁旧锚点与标记，在新位置重建（计划 4.1）
            DestroyDraft();

            // 命中即取姿态，锚到被命中的平面上（平面细化时标记跟随平面，钉地不漂移）
            if (!(hit.trackable is ARPlane plane))
            {
                return;
            }
            var anchor = anchorManager.AttachAnchor(plane, hit.pose);
            if (anchor == null)
            {
                Debug.LogWarning("[Placement] 锚点创建失败，本次放置取消");
                return;
            }
            currentAnchor = anchor;
            currentMarker = Instantiate(markerPrefab, hit.pose.position, hit.pose.rotation, anchor.transform);
            reportButton.SetTarget(currentMarker.transform);
            LastTapInfo = "已放置标记";
            if (diagnostics != null)
            {
                diagnostics.RecordPlacement(screenPosition, hit, anchor, currentMarker.transform);
            }
        }

        /// <summary>销毁草稿（取消、提交成功、换位重建共用）。</summary>
        public void DestroyDraft()
        {
            if (currentMarker != null)
            {
                Destroy(currentMarker);
                currentMarker = null;
            }
            if (currentAnchor != null && anchorManager != null)
            {
                anchorManager.TryRemoveAnchor(currentAnchor);
                currentAnchor = null;
            }
            if (reportButton != null)
            {
                reportButton.SetTarget(null);
            }
        }
    }
}
