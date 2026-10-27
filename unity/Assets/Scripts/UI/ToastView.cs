using UnityEngine;
using UnityEngine.UI;

namespace Inspection.UI
{
    /// <summary>
    /// 轻提示（Toast）：一次性结果文案的显示与清除。
    /// 失败类提示常驻，直到下一次提交或取消由调用方清除（计划 4.3）；
    /// 成功类提示自动消失。
    /// </summary>
    public class ToastView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text messageText;

        private float hideAt;
        private bool persistent;

        private void Update()
        {
            if (root.activeSelf && !persistent && hideAt > 0f && Time.unscaledTime >= hideAt)
            {
                HideNow();
            }
        }

        /// <summary>显示提示；autoHideSeconds ≤ 0 表示常驻（直到 HideNow）。</summary>
        public void Show(string message, float autoHideSeconds = 0f)
        {
            if (messageText != null)
            {
                messageText.text = message ?? string.Empty;
            }
            root.SetActive(true);
            persistent = autoHideSeconds <= 0f;
            hideAt = persistent ? 0f : Time.unscaledTime + autoHideSeconds;
        }

        /// <summary>立即清除提示。</summary>
        public void HideNow()
        {
            root.SetActive(false);
            persistent = false;
            hideAt = 0f;
        }
    }
}
