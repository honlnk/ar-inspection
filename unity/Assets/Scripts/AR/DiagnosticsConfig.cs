using UnityEngine;

namespace Inspection.AR
{
    /// <summary>
    /// 「完整诊断」运行时开关（计划 006 4.8）：默认关闭、PlayerPrefs 持久化、
    /// 重启 App 保留。统一门控周期采样日志（DevDebugOverlay）、创建/历史请求
    /// 详细网络日志（IssueApiClient/HistoryApiClient）与成功放置诊断采集
    /// （PlacementDiagnostics）；业务反馈 Toast 与 Unity 警告/错误不受此开关影响。
    /// 状态变化输出一条不含敏感信息的日志。编辑器与自动化测试行为不受影响。
    /// </summary>
    public static class DiagnosticsConfig
    {
        private const string FullDiagnosticsKey = "inspection.diagnostics.full";

        /// <summary>完整诊断是否开启（默认关闭；首次安装与升级都是关）。</summary>
        public static bool FullEnabled
        {
            get => PlayerPrefs.GetInt(FullDiagnosticsKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(FullDiagnosticsKey, value ? 1 : 0);
                PlayerPrefs.Save();
                // 开关状态变化的必要日志：不含账号、令牌、地址等敏感信息
                Debug.Log($"[Diagnostics] 完整诊断已{(value ? "开启" : "关闭")}");
            }
        }
    }
}
