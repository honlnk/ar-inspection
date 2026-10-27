using UnityEngine;

namespace Inspection
{
    /// <summary>
    /// 全局帧率上限（真机发热缓解，用户授权新增）：
    /// 默认跟随屏幕高刷新率（144Hz）会让 GPU 与相机链路高负载运行，
    /// 实测机身升温触发 Thermal Status Severe 后 ARCore 识别明显变慢。
    /// 放置/表单交互 30fps 足够，限帧后发热与功耗显著下降。
    /// </summary>
    public class AppFrameRate : MonoBehaviour
    {
        private void Awake()
        {
            Application.targetFrameRate = 30;
        }
    }
}
