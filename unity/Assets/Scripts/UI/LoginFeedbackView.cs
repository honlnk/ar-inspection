using UnityEngine;
using UnityEngine.UI;

namespace Inspection.UI
{
    /// <summary>按加载、错误、提示区分登录反馈，布局不随文案长度跳动。</summary>
    public sealed class LoginFeedbackView : MonoBehaviour
    {
        [SerializeField] private Text text;
        [SerializeField] private Image background;
        private string lastMessage;

        public void Configure(Text messageText, Image messageBackground)
        {
            text = messageText;
            background = messageBackground;
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (text == null || text.text == lastMessage)
            {
                return;
            }
            lastMessage = text.text;
            var loading = lastMessage.Contains("登录中") || lastMessage.Contains("恢复会话");
            var error = !loading && (lastMessage.Contains("失败") || lastMessage.Contains("错误")
                || lastMessage.Contains("请输入") || lastMessage.Contains("未配置") || lastMessage.Contains("无权")
                || lastMessage.Contains("过期") || lastMessage.Contains("管理员"));
            var color = loading ? UiVisualStyle.InProgress : error ? UiVisualStyle.Error : UiVisualStyle.Secondary;
            text.color = color;
            if (background != null)
            {
                background.color = UiVisualStyle.Tint(color);
            }
        }
    }
}
