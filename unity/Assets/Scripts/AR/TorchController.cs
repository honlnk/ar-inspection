using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Inspection.AR
{
    /// <summary>
    /// 手电筒（Torch）开关：暗光环境下照亮场景，辅助 AR 跟踪与平面检测
    /// （计划 004 施工中用户授权新增，解决深夜验收光照不足问题）。
    /// 设备是否支持运行时探测，不支持时按钮禁用并提示。
    /// </summary>
    public class TorchController : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Button button;
        [SerializeField] private Text label;

        private bool on;
        private bool supportKnown;
        private bool supported;

        /// <summary>当前手电是否为开启状态（requested 口径）。</summary>
        public bool IsOn => on;

        private void Update()
        {
            // 会话启动前 subsystem 为空：每帧轻探一次直到可判定
            if (!supportKnown)
            {
                supported = cameraManager != null && cameraManager.subsystem != null
                    && cameraManager.subsystem.running
                    && cameraManager.DoesCurrentCameraSupportTorch();
                if (cameraManager != null && cameraManager.subsystem != null && cameraManager.subsystem.running)
                {
                    supportKnown = true;
                    Refresh();
                }
            }
        }

        /// <summary>切换手电（按钮点击，编辑器绑定）。</summary>
        public void Toggle()
        {
            if (!supported && supportKnown)
            {
                return;
            }
            on = !on;
            cameraManager.requestedCameraTorchMode = on ? XRCameraTorchMode.On : XRCameraTorchMode.Off;
            Refresh();
        }

        private void Refresh()
        {
            if (supportKnown && !supported)
            {
                button.interactable = false;
                label.text = "手电不支持";
                return;
            }
            label.text = on ? "手电 开" : "手电 关";
        }
    }
}
