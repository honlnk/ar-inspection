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

    [Test]
    public void BackRouter_KeyboardTakesPriorityOverEveryPanel()
    {
        var state = new BackState(keyboardActive: true, settingsOpen: true, loginOpen: true,
            historyOpen: true, formOpen: true, formSubmitting: false, exitArmed: true);
        Assert.AreEqual(BackAction.DismissKeyboard, BackRouter.Decide(state));
    }

    [Test]
    public void BackRouter_ClosesTopmostPanelInLayerOrder()
    {
        // 设置叠加在登录之上：先关设置，不退出也不隐藏登录
        Assert.AreEqual(BackAction.CloseSettings,
            BackRouter.Decide(new BackState(false, true, true, true, false, false, false)));
        Assert.AreEqual(BackAction.CloseHistory,
            BackRouter.Decide(new BackState(false, false, false, true, false, false, false)));
        Assert.AreEqual(BackAction.CollapseForm,
            BackRouter.Decide(new BackState(false, false, false, false, true, false, false)));
    }

    [Test]
    public void BackRouter_LoginNeverClosesAndNeedsDoubleBackToExit()
    {
        Assert.AreEqual(BackAction.ConfirmExit,
            BackRouter.Decide(new BackState(false, false, true, true, false, false, false)));
        Assert.AreEqual(BackAction.Exit,
            BackRouter.Decide(new BackState(false, false, true, true, false, false, true)));
    }

    [Test]
    public void BackRouter_SubmittingFormIgnoresBack()
    {
        Assert.AreEqual(BackAction.None,
            BackRouter.Decide(new BackState(false, false, false, false, true, true, false)));
        Assert.AreEqual(BackAction.CollapseForm,
            BackRouter.Decide(new BackState(false, false, false, false, true, false, false)));
    }

    [Test]
    public void BackRouter_RootNeedsSecondBackWithinTwoSeconds()
    {
        Assert.AreEqual(BackAction.ConfirmExit,
            BackRouter.Decide(new BackState(false, false, false, false, false, false, false)));
        Assert.AreEqual(BackAction.Exit,
            BackRouter.Decide(new BackState(false, false, false, false, false, false, true)));
        Assert.AreEqual(2f, BackRouter.ExitConfirmWindowSeconds);
    }

    [Test]
    public void BackRouter_ExitMessageWarnsOnlyWhenDraftExists()
    {
        StringAssert.Contains("草稿", BackRouter.ConfirmExitMessage(true));
        StringAssert.DoesNotContain("草稿", BackRouter.ConfirmExitMessage(false));
    }

    [Test]
    public void HistoryTagView_TintsTextAndBackgroundFromSemanticText()
    {
        var root = new GameObject("HistoryTag");
        try
        {
            var background = root.AddComponent<Image>();
            background.color = UiVisualStyle.Tint(UiVisualStyle.Open);
            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            var label = labelObject.AddComponent<Text>();
            var tag = root.AddComponent<HistoryTagView>();

            label.text = "优先级 高";
            tag.Configure(label, background, true);
            Assert.AreEqual(UiVisualStyle.High, label.color);
            Assert.AreEqual(UiVisualStyle.Tint(UiVisualStyle.High), background.color);

            label.text = "优先级 低";
            tag.Refresh();
            Assert.AreEqual(UiVisualStyle.Low, label.color);
            Assert.AreEqual(UiVisualStyle.Tint(UiVisualStyle.Low), background.color);

            label.text = "已解决";
            tag.Configure(label, background, false);
            Assert.AreEqual(UiVisualStyle.Resolved, label.color);
            Assert.AreEqual(UiVisualStyle.Tint(UiVisualStyle.Resolved), background.color);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
