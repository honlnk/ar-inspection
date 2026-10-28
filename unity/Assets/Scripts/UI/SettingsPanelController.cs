using UnityEngine;
using UnityEngine.UI;
using Inspection.API;
using Inspection.AR;

namespace Inspection.UI
{
    /// <summary>
    /// 设置面板（居中弹窗 + 全屏蒙层）：后端地址编辑与保存（PlayerPrefs 持久化、保存即生效），
    /// 顶部栏状态同步，未配置地址时的首次启动引导（仅一次），以及「完整诊断」运行时开关
    /// （计划 006 4.8：默认关闭、持久化、即时生效，门控周期日志/网络详细日志/放置采集）。
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private InputField addressInput;
        [SerializeField] private Text topBarStatusText;
        [SerializeField] private Toggle fullDiagnosticsToggle;
        [SerializeField] private LoginPanelController loginPanel;
        [SerializeField] private HistoryPanelController historyPanel;
        [SerializeField] private AuthApiClient authClient;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Start()
        {
            RefreshTopBar();
            // 首启引导：从未保存过后端地址且从未引导过 → 自动弹设置面板一次
            if (!BackendConfig.HasSaved && !BackendConfig.FirstRunGuided)
            {
                BackendConfig.FirstRunGuided = true;
                Open();
            }
        }

        /// <summary>打开面板：输入框回显已保存地址（没有则空）；诊断开关回显当前持久值。</summary>
        public void Open()
        {
            addressInput.text = BackendConfig.GetBaseUrl();
            if (fullDiagnosticsToggle != null)
            {
                fullDiagnosticsToggle.SetIsOnWithoutNotify(DiagnosticsConfig.FullEnabled);
            }
            panelRoot.SetActive(true);
        }

        /// <summary>保存按钮（编辑器绑定）：原样保存（去首尾空白），立即生效并刷新顶部栏。</summary>
        public void OnSaveClicked()
        {
            BackendConfig.Save(addressInput.text);
            RefreshTopBar();
            panelRoot.SetActive(false);
        }

        /// <summary>关闭按钮（编辑器绑定）：不保存直接收起（蒙层点击不关闭，关闭一律走本按钮）。</summary>
        public void OnCloseClicked()
        {
            panelRoot.SetActive(false);
        }

        /// <summary>账号区退出登录，沿用历史页的撤销会话与草稿保留语义。</summary>
        public void OnLogoutClicked()
        {
            historyPanel?.OnLogoutClicked();
        }

        /// <summary>完整诊断开关回调（编辑器绑定，持久化监听的参数只是占位值）：
        /// 读开关当前状态即时写入持久配置；状态变化日志由 DiagnosticsConfig 统一输出。</summary>
        public void OnFullDiagnosticsToggled(bool placeholder)
        {
            if (fullDiagnosticsToggle != null)
            {
                DiagnosticsConfig.FullEnabled = fullDiagnosticsToggle.isOn;
            }
        }

        private void RefreshTopBar()
        {
            var url = BackendConfig.GetBaseUrl();
            topBarStatusText.text = url.Length > 0 ? BackendConfig.ShortenForDisplay(url) : "未配置后端";
        }
    }
}
