using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Inspection.API;

namespace Inspection.UI
{
    /// <summary>表单内的三项优先级选择，保留既有 medium / low / high 索引与材质映射。</summary>
    public sealed class PrioritySelector : MonoBehaviour
    {
        [SerializeField] private Button[] buttons;
        [SerializeField] private Image[] backgrounds;
        [SerializeField] private Text[] labels;
        [SerializeField] private int value;

        public UnityEvent<int> onValueChanged = new UnityEvent<int>();
        public int Value => value;

        public void Configure(int initialValue, Button[] optionButtons, Image[] optionBackgrounds, Text[] optionLabels)
        {
            buttons = optionButtons;
            backgrounds = optionBackgrounds;
            labels = optionLabels;
            value = Mathf.Clamp(initialValue, 0, 2);
            for (var i = 0; i < buttons.Length; i++)
            {
                var index = i;
                buttons[i].onClick.AddListener(() => Select(index));
            }
            Refresh();
        }

        public void Select(int index)
        {
            value = Mathf.Clamp(index, 0, 2);
            Refresh();
            onValueChanged.Invoke(value);
        }

        public void SetValueWithoutNotify(int index)
        {
            value = Mathf.Clamp(index, 0, 2);
            Refresh();
        }

        public static Color SelectedBackground(string priority)
        {
            return UiVisualStyle.Priority(priority);
        }

        public static string PriorityLabel(string priority)
        {
            if (priority == "low") return "低";
            if (priority == "high") return "高";
            return "中";
        }

        private void Awake()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (backgrounds == null || labels == null)
            {
                return;
            }
            for (var i = 0; i < backgrounds.Length; i++)
            {
                var selected = i == value;
                var color = UiVisualStyle.Priority(IssueContract.PriorityValues[i]);
                backgrounds[i].color = selected ? color : UiVisualStyle.Surface;
                labels[i].text = "优先级 " + PriorityLabel(IssueContract.PriorityValues[i]);
                labels[i].color = selected ? Color.white : color;
            }
        }
    }
}
