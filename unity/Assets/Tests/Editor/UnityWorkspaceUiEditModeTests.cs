using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Inspection.UI;

/// <summary>计划 007 Unity 展示层测试：颜色语义、优先级选择、键盘避让和 AR 重置确认。</summary>
public class UnityWorkspaceUiEditModeTests
{
    [Test]
    public void PrioritySelector_PreservesContractOrderAndTextLabels()
    {
        CollectionAssert.AreEqual(new[] { "medium", "low", "high" }, Inspection.API.IssueContract.PriorityValues);
        Assert.AreEqual("低", PrioritySelector.PriorityLabel("low"));
        Assert.AreEqual("中", PrioritySelector.PriorityLabel("medium"));
        Assert.AreEqual("高", PrioritySelector.PriorityLabel("high"));
        Assert.AreNotEqual(UiVisualStyle.Priority("low"), UiVisualStyle.Priority("medium"));
        Assert.AreNotEqual(UiVisualStyle.Priority("high"), UiVisualStyle.Priority("medium"));
    }

    [Test]
    public void PrioritySelector_SelectRefreshesColorsAndRaisesOriginalIndex()
    {
        var root = new GameObject("PriorityTest");
        try
        {
            var selector = root.AddComponent<PrioritySelector>();
            var backgrounds = new Image[3];
            var labels = new Text[3];
            var buttons = new Button[3];
            for (var i = 0; i < 3; i++)
            {
                var item = new GameObject("Priority" + i);
                item.transform.SetParent(root.transform, false);
                backgrounds[i] = item.AddComponent<Image>();
                buttons[i] = item.AddComponent<Button>();
                var label = new GameObject("Label");
                label.transform.SetParent(item.transform, false);
                labels[i] = label.AddComponent<Text>();
            }
            selector.Configure(0, buttons, backgrounds, labels);
            var notified = -1;
            selector.onValueChanged.AddListener(index => notified = index);
            selector.Select(2);

            Assert.AreEqual(2, selector.Value);
            Assert.AreEqual(2, notified);
            Assert.AreEqual(UiVisualStyle.High, backgrounds[2].color);
            Assert.AreEqual(Color.white, labels[2].color);
            Assert.AreEqual("优先级 高", labels[2].text);
            Assert.AreEqual(UiVisualStyle.Surface, backgrounds[0].color);
            Assert.AreEqual(UiVisualStyle.Medium, labels[0].color);
            selector.SetValueWithoutNotify(1);
            Assert.AreEqual("优先级 低", labels[1].text);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PrioritySelector_InvalidIndexClampsToContractRange()
    {
        var root = new GameObject("PriorityClamp");
        try
        {
            var selector = root.AddComponent<PrioritySelector>();
            selector.Select(99);
            Assert.AreEqual(2, selector.Value);
            selector.Select(-1);
            Assert.AreEqual(0, selector.Value);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void StatusColors_AllSemanticStatesHaveDistinctColor()
    {
        Assert.AreNotEqual(UiVisualStyle.Status("open"), UiVisualStyle.Status("in_progress"));
        Assert.AreNotEqual(UiVisualStyle.Status("in_progress"), UiVisualStyle.Status("resolved"));
    }

    [Test]
    public void KeyboardInset_UsesNormalizedKeyboardTopAndClamps()
    {
        Assert.AreEqual(0.5f, KeyboardInset.BottomAnchor(2400, 1200), 0.001f);
        Assert.AreEqual(0f, KeyboardInset.BottomAnchor(0, 1200));
        Assert.AreEqual(1f, KeyboardInset.BottomAnchor(2400, 9999));
        Assert.AreEqual(0f, KeyboardInset.BottomAnchor(2400, -100));
    }

    [Test]
    public void SessionReset_FirstClickOnlyAsksForConfirmation()
    {
        var root = new GameObject("ResetTest");
        var labelObject = new GameObject("Label");
        try
        {
            labelObject.transform.SetParent(root.transform, false);
            var label = labelObject.AddComponent<Text>();
            var reset = root.AddComponent<SessionResetButton>();
            reset.Configure(label);
            Assert.IsFalse(reset.IsConfirming);
            reset.ConfirmOrReset();
            Assert.IsTrue(reset.IsConfirming);
            Assert.AreEqual("再次点击确认重置", label.text);
            Assert.IsTrue(reset.RequiresConfirmation);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(labelObject);
        }
    }

    [Test]
    public void TouchSizes_At1080PhysicalWidth_MeetMinimumDp()
    {
        Assert.GreaterOrEqual(UiVisualStyle.MinimumButtonHeight * 160f / 480f, 48f);
        Assert.GreaterOrEqual(UiVisualStyle.MinimumInputHeight * 160f / 480f, 44f);
        Assert.GreaterOrEqual(UiVisualStyle.BodyFontSize * 160f / 480f, 14f);
        Assert.GreaterOrEqual(UiVisualStyle.ButtonFontSize * 160f / 480f, 16f);
    }

    [Test]
    public void LoginFeedback_AndHistoryTags_UseSeparateTextAndBackgroundObjects()
    {
        var login = typeof(LoginFeedbackView);
        var tag = typeof(HistoryTagView);
        Assert.IsNotNull(login.GetField("background", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.IsNotNull(tag.GetField("background", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.IsNotNull(login.GetField("text", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.IsNotNull(tag.GetField("text", BindingFlags.NonPublic | BindingFlags.Instance));
    }
}
