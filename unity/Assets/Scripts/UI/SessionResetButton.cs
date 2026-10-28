using UnityEngine;
using UnityEngine.UI;

namespace Inspection.UI
{
    /// <summary>AR 会话重置使用两步文字确认，避免单手操作误清地图。</summary>
    public sealed class SessionResetButton : MonoBehaviour
    {
        [SerializeField] private Text label;
        private bool confirming;

        public bool RequiresConfirmation => true;
        public bool IsConfirming => confirming;

        public void Configure(Text buttonLabel)
        {
            label = buttonLabel;
            ResetConfirmation();
        }

        public void ConfirmOrReset()
        {
            if (!confirming)
            {
                confirming = true;
                if (label != null)
                {
                    label.text = "再次点击确认重置";
                }
                CancelInvoke(nameof(ResetConfirmation));
                Invoke(nameof(ResetConfirmation), 5f);
                return;
            }
            var session = Object.FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
            session?.Reset();
            ResetConfirmation();
        }

        private void OnDisable()
        {
            ResetConfirmation();
        }

        private void ResetConfirmation()
        {
            confirming = false;
            CancelInvoke(nameof(ResetConfirmation));
            if (label != null)
            {
                label.text = "重置 AR 会话";
            }
        }
    }
}
