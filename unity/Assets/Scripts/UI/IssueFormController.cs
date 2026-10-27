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
        [SerializeField] private InputField titleInput;
        [SerializeField] private InputField descriptionInput;
        [SerializeField] private Dropdown priorityDropdown;
        [SerializeField] private Text positionText;
        [SerializeField] private Button submitButton;
        [SerializeField] private Text submitLabel;

        /// <summary>下拉选项「中 / 低 / 高」对应的后端枚举值（默认 index 0 = medium，计划 4.3）。</summary>
        internal static readonly string[] PriorityValues =
        {
            IssueContract.PriorityMedium,
            IssueContract.PriorityLow,
            IssueContract.PriorityHigh,
        };

        private bool submitting;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

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

        /// <summary>收起表单并清空输入（取消与提交成功共用；输入内容不保留）。</summary>
        public void CloseAndClear()
        {
            panelRoot.SetActive(false);
            submitting = false;
            titleInput.text = string.Empty;
            descriptionInput.text = string.Empty;
            priorityDropdown.value = 0;
            RefreshSubmitState();
        }

        /// <summary>标题输入变化回调（编辑器绑定）：驱动提交按钮本地校验。</summary>
        public void OnTitleChanged()
        {
            RefreshSubmitState();
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
                priority = PriorityValues[Mathf.Clamp(priorityDropdown.value, 0, PriorityValues.Length - 1)],
                // 坐标与表单展示一致保留 3 位小数（四舍五入），避免 float 精度噪音进后端
                position = new IssuePosition
                {
                    x = System.Math.Round(pos.Value.x, 3),
                    y = System.Math.Round(pos.Value.y, 3),
                    z = System.Math.Round(pos.Value.z, 3),
                },
            };
            submitting = true;
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
            // 失败：优先展示后端中文错误；标记与表单全部保留可原样重试；提示常驻不自动消失
            toast.Show(result.ErrorMessage);
            RefreshSubmitState();
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
