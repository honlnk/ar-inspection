namespace Inspection.UI
{
    /// <summary>系统返回在某一层级应执行的动作（计划 007 4.5）。</summary>
    public enum BackAction
    {
        /// <summary>不处理：仅用于提交中的表单，避免返回打断请求并隐藏进度。</summary>
        None,
        /// <summary>只收起软键盘，不关闭页面、不退出。</summary>
        DismissKeyboard,
        /// <summary>关闭设置页，回到打开它的页面。</summary>
        CloseSettings,
        /// <summary>关闭历史页，回到 AR 主界面。</summary>
        CloseHistory,
        /// <summary>收起上报表单但保留草稿标记与已填内容。</summary>
        CollapseForm,
        /// <summary>首次返回：提示再按一次退出，不退出应用。</summary>
        ConfirmExit,
        /// <summary>确认窗口内再次返回：退出应用。</summary>
        Exit,
    }

    /// <summary>一次返回事件发生时的界面状态快照。</summary>
    public readonly struct BackState
    {
        public BackState(bool keyboardActive, bool settingsOpen, bool loginOpen, bool historyOpen,
            bool formOpen, bool formSubmitting, bool exitArmed)
        {
            KeyboardActive = keyboardActive;
            SettingsOpen = settingsOpen;
            LoginOpen = loginOpen;
            HistoryOpen = historyOpen;
            FormOpen = formOpen;
            FormSubmitting = formSubmitting;
            ExitArmed = exitArmed;
        }

        public bool KeyboardActive { get; }
        public bool SettingsOpen { get; }
        public bool LoginOpen { get; }
        public bool HistoryOpen { get; }
        public bool FormOpen { get; }
        public bool FormSubmitting { get; }
        public bool ExitArmed { get; }
    }

    /// <summary>
    /// 返回优先级纯逻辑：软键盘 → 设置 → 登录 → 历史 → 表单 → AR 主界面。
    /// 与 MonoBehaviour 分离，便于 EditMode 覆盖全部分支；运行时由 AndroidBackHandler 执行。
    /// </summary>
    public static class BackRouter
    {
        /// <summary>根页面与登录页「再按一次退出」的确认窗口（秒）。</summary>
        public const float ExitConfirmWindowSeconds = 2f;

        public static BackAction Decide(in BackState state)
        {
            // 软键盘弹起时返回只收键盘；页面显隐与退出都不参与
            if (state.KeyboardActive)
            {
                return BackAction.DismissKeyboard;
            }

            // 设置页可叠加在登录页之上，先关闭最上层
            if (state.SettingsOpen)
            {
                return BackAction.CloseSettings;
            }

            // 登录页不允许被返回绕过（否则会露出下层 AR 主界面）
            if (state.LoginOpen)
            {
                return state.ExitArmed ? BackAction.Exit : BackAction.ConfirmExit;
            }

            if (state.HistoryOpen)
            {
                return BackAction.CloseHistory;
            }

            if (state.FormOpen)
            {
                // 提交中忽略返回，等请求结束后再允许收起
                return state.FormSubmitting ? BackAction.None : BackAction.CollapseForm;
            }

            return state.ExitArmed ? BackAction.Exit : BackAction.ConfirmExit;
        }

        /// <summary>退出确认提示文案；有草稿时明确说明会丢失未上报标记。</summary>
        public static string ConfirmExitMessage(bool hasDraft)
        {
            return hasDraft
                ? "再按一次返回键退出应用，未上报的草稿标记将丢失"
                : "再按一次返回键退出应用";
        }
    }
}
