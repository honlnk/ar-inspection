using UnityEngine;
using UnityEngine.UI;
using Inspection.API;
using Inspection.AR;

namespace Inspection.UI
{
    /// <summary>
    /// 上报表单（底部卡片 + 全屏蒙层）：标题必填、描述可选、优先级三选一、位置只读带出；
    /// 提交按钮三态（可提交/标题空禁用/请求中禁用）；取消与失败路径按计划 4.3 处理草稿与输入。
    /// </summary>
    public class IssueFormController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private PlacementController placement;
        [SerializeField] private IssueApiClient apiClient;
        [SerializeField] private ToastView toast;
        [SerializeField] private LoginPanelController loginPanel;
        [SerializeField] private InputField titleInput;
        [SerializeField] private InputField descriptionInput;
        [SerializeField] private PrioritySelector prioritySelector;
        [SerializeField] private Text positionText;
        [SerializeField] private Button submitButton;
        [SerializeField] private Text submitLabel;
        [SerializeField] private Text errorText;

        /// <summary>下拉选项「中 / 低 / 高」对应的后端枚举值（默认 index 0 = medium，计划 4.3）。</summary>
        internal static readonly string[] PriorityValues = IssueContract.PriorityValues;

        private bool submitting;

        /// <summary>用户最后一次正常输入的内容：仅在软键盘被系统取消导致回滚时用于恢复。</summary>
        private string titleBuffer = string.Empty;
        private string descriptionBuffer = string.Empty;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>是否正在提交：系统返回在提交期间不收起表单，避免请求结果无处展示。</summary>
        public bool IsSubmitting => submitting;

        /// <summary>当前优先级分段选择索引；0=medium / 1=low / 2=high。</summary>
        public int CurrentPriorityIndex =>
            prioritySelector != null ? prioritySelector.Value : 0;

        /// <summary>
        /// 打开表单：位置只读行取草稿标记锚定坐标；输入内容保持上次状态（失败重试不丢输入）。
        /// </summary>
        public void OpenForDraft()
        {
            var pos = placement.DraftWorldPosition;
            positionText.text = pos.HasValue
                ? $"x {pos.Value.x:F3}   y {pos.Value.y:F3}   z {pos.Value.z:F3}"
                : "（无草稿标记）";
            panelRoot.SetActive(true);
            RefreshSubmitState();
        }

        /// <summary>
        /// 系统返回（计划 007 4.5）：收起表单但保留草稿标记与已填内容，
        /// 不执行「取消」的销毁动作；再次点标记旁按钮可带原输入回到表单。
        /// </summary>
        public void CollapseKeepingDraft()
        {
            if (submitting)
            {
                return;
            }
            panelRoot.SetActive(false);
            SetError(string.Empty);
            RefreshSubmitState();
        }

        /// <summary>收起表单并清空输入（取消与提交成功共用；输入内容不保留）。</summary>
        public void CloseAndClear()
        {
            panelRoot.SetActive(false);
            submitting = false;
            // 与输入框文本一起清掉取消恢复的缓存，避免下次聚焦后按返回时回填旧内容
            titleBuffer = string.Empty;
            descriptionBuffer = string.Empty;
            titleInput.text = string.Empty;
            descriptionInput.text = string.Empty;
            prioritySelector.SetValueWithoutNotify(0);
            SetError(string.Empty);
            RefreshSubmitState();
        }

        /// <summary>标题输入变化回调（编辑器绑定）：驱动提交按钮本地校验。</summary>
        public void OnTitleChanged()
        {
            // 只在软键盘可见（用户真实输入）时记录缓存：系统返回收起键盘时
            // uGUI 会先把文本写成空串再置 wasCanceled，那一拍不能覆盖缓存
            if (TouchScreenKeyboard.visible)
            {
                titleBuffer = titleInput.text;
            }
            RefreshSubmitState();
        }

        /// <summary>标题结束编辑（编辑器绑定）：系统取消输入时把用户已输入内容恢复回来。</summary>
        public void OnTitleEndEdit(string placeholder)
        {
            RestoreCanceledText(titleInput, ref titleBuffer);
            RefreshSubmitState();
        }

        /// <summary>描述输入变化回调（编辑器绑定）：只维护取消恢复缓存。</summary>
        public void OnDescriptionChanged()
        {
            // 与标题同理：键盘不可见时的写入属于系统取消回滚，不覆盖缓存
            if (TouchScreenKeyboard.visible)
            {
                descriptionBuffer = descriptionInput.text;
            }
        }

        /// <summary>描述结束编辑（编辑器绑定）：系统取消输入时恢复已输入内容。</summary>
        public void OnDescriptionEndEdit(string placeholder)
        {
            RestoreCanceledText(descriptionInput, ref descriptionBuffer);
        }

        /// <summary>uGUI 取消输入会回滚文本；取消时统一恢复成用户最后一次真实输入的内容。</summary>
        private static void RestoreCanceledText(InputField input, ref string buffer)
        {
            if (input.wasCanceled && input.text != buffer)
            {
                input.text = buffer;
            }
        }

        /// <summary>
        /// 优先级下拉变化回调（编辑器绑定，持久化监听的参数只是占位值）：
        /// 读取下拉当前值，立即把已放置草稿标记换成对应优先级颜色
        /// （计划 006 4.5：低=绿 / 中=橙 / 高=红；几何、锚点与坐标不受换色影响）。
        /// </summary>
        public void OnPriorityChanged(int placeholder)
        {
            placement?.ApplyDraftPriority(PriorityValues[CurrentPriorityIndex]);
        }

        /// <summary>提交按钮点击（编辑器绑定）：防连点 + 本地拦截空标题，后端仍会兜底 400（双保险）。</summary>
        public void OnSubmitClicked()
        {
            if (submitting || !TitleIsValid())
            {
                return;
            }
            var pos = placement.DraftWorldPosition;
            if (!pos.HasValue)
            {
                toast.Show("请先放置标记再上报");
                return;
            }

            var body = new CreateIssueRequest
            {
                title = titleInput.text.Trim(),
                description = descriptionInput.text.Trim(),
                priority = PriorityValues[CurrentPriorityIndex],
                // 坐标与表单展示一致保留 3 位小数（四舍五入），避免 float 精度噪音进后端
                position = new IssuePosition
                {
                    x = System.Math.Round(pos.Value.x, 3),
                    y = System.Math.Round(pos.Value.y, 3),
                    z = System.Math.Round(pos.Value.z, 3),
                },
            };
            submitting = true;
            SetError(string.Empty);
            RefreshSubmitState();
            StartCoroutine(apiClient.CreateIssue(body, OnCreateFinished));
        }

        /// <summary>取消按钮点击（编辑器绑定）：销毁草稿标记并收起表单，清除提示。</summary>
        public void OnCancelClicked()
        {
            placement.DestroyDraft();
            CloseAndClear();
            toast.HideNow();
        }

        private void OnCreateFinished(CreateIssueResult result)
        {
            submitting = false;
            if (result.Success)
            {
                toast.Show("问题已上报", 3f);
                placement.DestroyDraft();
                CloseAndClear();
                return;
            }
            if (result.SessionExpired)
            {
                // 会话过期：草稿标记与全部输入保留，跳转登录（计划 006）
                toast.Show(result.ErrorMessage);
                RefreshSubmitState();
                loginPanel?.ShowForSessionExpired();
                return;
            }
            // 失败：错误紧邻提交按钮展示；顶部 Toast 保留原反馈，表单与标记原样保留。
            SetError(result.ErrorMessage);
            toast.Show(result.ErrorMessage);
            RefreshSubmitState();
        }

        private void SetError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message ?? string.Empty;
            }
        }

        private bool TitleIsValid()
        {
            return titleInput != null && titleInput.text.Trim().Length > 0;
        }

        private void RefreshSubmitState()
        {
            if (submitButton == null)
            {
                return;
            }
            submitButton.interactable = !submitting && TitleIsValid();
            if (submitLabel != null)
            {
                submitLabel.text = submitting ? "提交中…" : "提交";
            }
        }
    }
}
