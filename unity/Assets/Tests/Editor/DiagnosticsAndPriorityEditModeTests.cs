using NUnit.Framework;
using UnityEngine;
using Inspection.AR;

/// <summary>
/// 计划 006 诊断开关与 AR 优先级映射 EditMode 测试：
/// 开关默认关闭、持久化往返；优先级字符串 → 材质数组下标映射（0=medium/1=low/2=high）。
/// </summary>
public class DiagnosticsAndPriorityEditModeTests
{
    private const string FullDiagnosticsKey = "inspection.diagnostics.full";

    [TearDown]
    public void ClearPrefs()
    {
        PlayerPrefs.DeleteKey(FullDiagnosticsKey);
    }

    // ---------- 完整诊断开关 ----------

    [Test]
    public void FullDiagnostics_DefaultOffForFreshInstall()
    {
        PlayerPrefs.DeleteKey(FullDiagnosticsKey);
        Assert.IsFalse(DiagnosticsConfig.FullEnabled, "首次安装默认关闭（计划 006 4.8）");
    }

    [Test]
    public void FullDiagnostics_ToggleRoundTripsAndPersists()
    {
        DiagnosticsConfig.FullEnabled = true;
        Assert.IsTrue(DiagnosticsConfig.FullEnabled);
        DiagnosticsConfig.FullEnabled = false;
        Assert.IsFalse(DiagnosticsConfig.FullEnabled, "关闭后立即生效");
        Assert.AreEqual(0, PlayerPrefs.GetInt(FullDiagnosticsKey, -1), "关闭值持久化为 0（重启保留）");
    }

    // ---------- 优先级 → 材质下标映射 ----------

    [Test]
    public void PriorityMapping_MatchesFormValueOrder()
    {
        // IssueFormController.PriorityValues = { medium, low, high }，材质数组下标必须一致
        Assert.AreEqual(0, PlacementController.PriorityToMaterialIndex("medium"));
        Assert.AreEqual(1, PlacementController.PriorityToMaterialIndex("low"));
        Assert.AreEqual(2, PlacementController.PriorityToMaterialIndex("high"));
    }

    [Test]
    public void PriorityMapping_UnknownOrNullFallsBackToMedium()
    {
        Assert.AreEqual(0, PlacementController.PriorityToMaterialIndex("urgent"));
        Assert.AreEqual(0, PlacementController.PriorityToMaterialIndex(null));
    }
}
