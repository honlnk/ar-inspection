using UnityEngine;
using UnityEngine.UI;

namespace Inspection.UI
{
    /// <summary>历史条目的独立语义标签：文案与颜色同步变化，保留明确的“优先级”前缀。</summary>
    public sealed class HistoryTagView : MonoBehaviour
    {
        [SerializeField] private Text text;
        [SerializeField] private Image background;
        [SerializeField] private bool priority;
        private string lastText;

        public void Configure(Text label, Image tagBackground, bool isPriority)
        {
            text = label;
            background = tagBackground;
            priority = isPriority;
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        /// <summary>按当前文案同步文字与底板颜色（运行时每帧检查，测试可直接调用）。</summary>
        public void Refresh()
        {
            if (text == null || text.text == lastText)
            {
                return;
            }
            lastText = text.text;
            var color = priority
                ? UiVisualStyle.Priority(lastText.Contains("低") ? "low" : lastText.Contains("高") ? "high" : "medium")
                : UiVisualStyle.Status(lastText.Contains("处理中") ? "in_progress" : lastText.Contains("已解决") ? "resolved" : "open");
            text.color = color;
            if (background != null)
            {
                background.color = UiVisualStyle.Tint(color);
            }
        }
    }
}
