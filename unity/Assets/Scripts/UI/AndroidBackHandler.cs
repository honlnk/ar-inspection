using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Inspection.AR;

namespace Inspection.UI
{
    /// <summary>
    /// Android 系统返回键与边缘返回手势的统一入口（计划 007 4.5）：
    /// 继续使用旧输入管理器的 Escape 事件，Manifest 显式关闭预测性返回回调，
    /// 保证三键导航与手势导航走同一条链路。各页面控制器不各自监听返回。
    /// 优先级判断在 BackRouter（纯逻辑、可测试），本类只收集状态并执行动作。
    /// </summary>
    public sealed class AndroidBackHandler : MonoBehaviour
    {
        /// <summary>键盘收起后忽略紧随其后的 Escape 的窗口（秒）。</summary>
        public const float KeyboardGuardSeconds = 0.35f;

        [SerializeField] private LoginPanelController loginPanel;
        [SerializeField] private SettingsPanelController settingsPanel;
        [SerializeField] private HistoryPanelController historyPanel;
        [SerializeField] private IssueFormController formPanel;
        [SerializeField] private PlacementController placement;
        [SerializeField] private ToastView toast;

        private bool keyboardWasVisible;
        private float keyboardHiddenAt = -100f;
        private float ignoreBackUntil = -100f;
        private float lastExitPromptAt = -100f;

        private void Update()
        {
            TrackKeyboard();

            if (!Input.GetKeyDown(KeyCode.Escape))
            {
                return;
            }

            var now = Time.unscaledTime;
            // 系统可能先收起键盘、同一次返回仍以 Escape 到达；忽略这次，避免顺带关闭页面
            if (now < ignoreBackUntil || now - keyboardHiddenAt < KeyboardGuardSeconds)
            {
                return;
            }

            var state = new BackState(
                TouchScreenKeyboard.visible,
                settingsPanel != null && settingsPanel.IsOpen,
                loginPanel != null && loginPanel.IsOpen,
                historyPanel != null && historyPanel.IsOpen,
                formPanel != null && formPanel.IsOpen,
                formPanel != null && formPanel.IsSubmitting,
                now - lastExitPromptAt <= BackRouter.ExitConfirmWindowSeconds);

            Execute(BackRouter.Decide(state), now);
        }

        private void TrackKeyboard()
        {
            var visible = TouchScreenKeyboard.visible;
            if (keyboardWasVisible && !visible)
            {
                keyboardHiddenAt = Time.unscaledTime;
            }
            keyboardWasVisible = visible;
        }

        private void Execute(BackAction action, float now)
        {
            switch (action)
            {
                case BackAction.DismissKeyboard:
                    DismissKeyboard();
                    ignoreBackUntil = now + KeyboardGuardSeconds;
                    break;
                case BackAction.CloseSettings:
                    settingsPanel.OnCloseClicked();
                    lastExitPromptAt = -100f;
                    break;
                case BackAction.CloseHistory:
                    historyPanel.OnCloseClicked();
                    lastExitPromptAt = -100f;
                    break;
                case BackAction.CollapseForm:
                    formPanel.CollapseKeepingDraft();
                    lastExitPromptAt = -100f;
                    toast?.Show("表单已收起，草稿与已填内容已保留", 2f);
                    break;
                case BackAction.ConfirmExit:
                    lastExitPromptAt = now;
                    toast?.Show(BackRouter.ConfirmExitMessage(placement != null && placement.HasDraft), 2f);
                    break;
                case BackAction.Exit:
                    Quit();
                    break;
            }
        }

        /// <summary>只让当前输入框失焦并由 uGUI 关闭软键盘，不改动页面层级。</summary>
        private static void DismissKeyboard()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var input = selected != null ? selected.GetComponent<InputField>() : null;
            if (input != null)
            {
                input.DeactivateInputField();
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
