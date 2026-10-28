using UnityEngine;
using UnityEngine.UI;

namespace Inspection.UI
{
    /// <summary>AR 底部任务提示，仅依据草稿存在状态切换，不参与射线与放置判断。</summary>
    public sealed class FieldGuideView : MonoBehaviour
    {
        [SerializeField] private Text message;
        private string lastMessage;

        public void Configure(Text text)
        {
            message = text;
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (message == null)
            {
                return;
            }
            var hasDraft = FindAnyObjectByType<Inspection.AR.PlacementController>(FindObjectsInactive.Include)?.DraftWorldPosition != null;
            var next = hasDraft ? "已放置标记，点标记旁按钮填写并上报" : "移动手机扫描，点击蓝色平面放置标记";
            if (next != lastMessage)
            {
                message.text = next;
                lastMessage = next;
            }
        }
    }
}
