using UnityEngine;
using UnityEngine.UI;
using Inspection.API;

namespace Inspection.UI
{
    /// <summary>
    /// 设置面板（居中弹窗 + 全屏蒙层）：后端地址编辑与保存（PlayerPrefs 持久化、保存即生效），
    /// 顶部栏状态同步，以及未配置地址时的首次启动引导（仅一次，计划 4.5）。
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private InputField addressInput;
        [SerializeField] private Text topBarStatusText;

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

        /// <summary>打开面板：输入框回显已保存地址（没有则空）。</summary>
        public void Open()
        {
            addressInput.text = BackendConfig.GetBaseUrl();
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

        private void RefreshTopBar()
        {
            var url = BackendConfig.GetBaseUrl();
            topBarStatusText.text = url.Length > 0 ? BackendConfig.ShortenForDisplay(url) : "未配置后端";
        }
    }
}
