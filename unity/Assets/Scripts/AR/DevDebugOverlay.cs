using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Inspection.AR
{
    /// <summary>
    /// 开发构建专用调试信息条：
    /// 第一行：会话状态、未跟踪原因、平面数量与最近一次点击判定结果；
    /// 第二行：草稿标记（位置/距离/视口/渲染器是否在渲染）与跟随按钮（激活/目标/相机/屏幕坐标），
    /// 用于真机定位「已放置标记但方块/按钮不可见」类问题。
    /// 非开发构建（无 DEVELOPMENT_BUILD 宏）自动隐藏，仅剩空组件，无渲染与逻辑开销。
    /// </summary>
    public class DevDebugOverlay : MonoBehaviour
    {
        [SerializeField] private Text text;
        [SerializeField] private PlacementController placement;
        [SerializeField] private UI.MarkerReportButton reportButton;

#if DEVELOPMENT_BUILD
        private ARSession session;
        private ARPlaneManager planeManager;
        private float nextUpdate;

        private void Update()
        {
            // 计划 006 4.8：周期采样（调试条、平面/点击/相机姿态日志）统一受
            // 「完整诊断」运行时开关门控；关闭时隐藏调试条且不做任何周期输出，
            // 业务反馈（Toast/警告/错误）不受影响
            if (!DiagnosticsConfig.FullEnabled)
            {
                if (text != null && text.gameObject.activeSelf)
                {
                    text.gameObject.SetActive(false);
                }
                return;
            }
            if (text != null && !text.gameObject.activeSelf)
            {
                text.gameObject.SetActive(true);
            }

            if (Time.unscaledTime < nextUpdate)
            {
                return;
            }
            nextUpdate = Time.unscaledTime + 0.5f;
            if (session == null)
            {
                session = FindFirstObjectByType<ARSession>();
            }
            if (planeManager == null)
            {
                planeManager = FindFirstObjectByType<ARPlaneManager>();
            }

            var planeCount = 0;
            var trackingCount = 0;
            if (planeManager != null)
            {
                foreach (var plane in planeManager.trackables)
                {
                    planeCount++;
                    if (plane.trackingState == TrackingState.Tracking)
                    {
                        trackingCount++;
                    }
                }
            }

            var sessionState = session != null ? ARSession.state.ToString() : "无ARSession";
            var reason = session != null && ARSession.state == ARSessionState.SessionTracking
                ? "跟踪中"
                : (session != null ? ARSession.notTrackingReason.ToString() : "-");
            var detection = planeManager != null && planeManager.subsystem != null && planeManager.subsystem.running
                ? "开"
                : "关";
            var line1 = $"{sessionState} {reason} | 检测{detection} 平面{planeCount}(跟踪{trackingCount}) | {PlacementController.LastTapInfo}";
            var buttonPart = reportButton != null ? "按钮" + reportButton.DiagState : "按钮未接线";
            var line2 = $"{BuildMarkerPart()} | {buttonPart}";
            text.text = line1 + "\n" + line2;
            // 同步写 logcat：截图 OCR 不可靠时用 adb logcat -s Unity 读同一行状态
            Debug.Log($"[ARDebug] {line1}");
            Debug.Log($"[ARDebug2] {line2}");

            // 计划 005 运动冒烟：不放置也每 0.5s 输出一条相机姿态采样，与前后事件可区分，
            // 用于证明真机上手持设备位置/旋转持续进入渲染相机；仅开发构建
            var poseCamera = Camera.main;
            if (poseCamera != null)
            {
                var euler = poseCamera.transform.rotation.eulerAngles;
                Debug.Log("[ARCameraPose] t=" + Time.unscaledTime.ToString("F1")
                    + " pos=(" + poseCamera.transform.position.x.ToString("F3")
                    + "," + poseCamera.transform.position.y.ToString("F3")
                    + "," + poseCamera.transform.position.z.ToString("F3")
                    + ") euler=(" + euler.x.ToString("F1")
                    + "," + euler.y.ToString("F1")
                    + "," + euler.z.ToString("F1")
                    + ") " + sessionState + "/" + reason);
            }
        }

        /// <summary>草稿标记诊断段：无标记 / 无主相机 / 位置+距离+视口+渲染可见性。</summary>
        private string BuildMarkerPart()
        {
            var marker = placement != null ? placement.MarkerTransform : null;
            if (marker == null)
            {
                return "标记无";
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return "标记有,无主相机";
            }
            var vp = cam.WorldToViewportPoint(marker.position);
            var markerRenderer = marker.GetComponentInChildren<Renderer>();
            return $"标记({marker.position.x:F2},{marker.position.y:F2},{marker.position.z:F2})"
                + $"距{Vector3.Distance(cam.transform.position, marker.position):F2}m"
                + $"视口({vp.x:F2},{vp.y:F2},{vp.z:F2})"
                + (markerRenderer == null ? "无渲染器" : $"渲染{(markerRenderer.isVisible ? "是" : "否")}");
        }
#else
        private void Awake()
        {
            // 正式构建：隐藏调试文本（组件随场景存在但不产生任何开销）
            if (text != null)
            {
                text.gameObject.SetActive(false);
            }
        }
#endif
    }
}
