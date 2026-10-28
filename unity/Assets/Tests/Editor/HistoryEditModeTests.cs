using NUnit.Framework;
using Inspection.API;
using Inspection.UI;

/// <summary>
/// 计划 006 历史列表契约 EditMode 测试：列表响应解析（成功/空/401/错误/网络）、
/// 按创建时间倒序、无效条目过滤、北京时间格式化与枚举中文标签。不发真实网络请求。
/// </summary>
public class HistoryEditModeTests
{
    private static IssueDto MakeIssue(string id, string createdAt, string priority = "medium", string status = "open")
    {
        return new IssueDto
        {
            id = id,
            title = "标题-" + id,
            description = "描述",
            priority = priority,
            status = status,
            position = new IssuePosition { x = 1, y = 2, z = 3 },
            createdAt = createdAt,
            updatedAt = createdAt,
        };
    }

    // ---------- 列表响应解析 ----------

    [Test]
    public void ParseList_200TwoIssues_SucceedsSortedDescending()
    {
        var body = "{\"data\":[" +
            MakeIssueJson("id-old", "2026-10-27T09:00:00+08:00") + "," +
            MakeIssueJson("id-new", "2026-10-28T10:30:00+08:00") + "]}";
        var result = HistoryApiClient.ParseListResponse(200, body, null);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Issues.Length);
        Assert.AreEqual("id-new", result.Issues[0].id, "新问题应排在最前（创建时间倒序）");
        Assert.AreEqual("id-old", result.Issues[1].id);
    }

    [Test]
    public void ParseList_200EmptyArray_SuccessWithZeroItems()
    {
        var result = HistoryApiClient.ParseListResponse(200, "{\"data\":[]}", null);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.Issues.Length, "空列表是成功状态（面板显示「暂无我的上报」）");
    }

    [Test]
    public void ParseList_401_MarksSessionExpiredNotEmptyList()
    {
        var body = "{\"error\":{\"code\":\"authentication_required\",\"message\":\"未认证\"}}";
        var result = HistoryApiClient.ParseListResponse(401, body, null);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.SessionExpired, "401 必须标记会话过期（保留面板跳登录，不得显示成空列表）");
    }

    [Test]
    public void ParseList_500ShowsBackendMessage()
    {
        var body = "{\"error\":{\"code\":\"internal_error\",\"message\":\"存储读写失败\"}}";
        var result = HistoryApiClient.ParseListResponse(500, body, null);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("存储读写失败", result.ErrorMessage);
    }

    [Test]
    public void ParseList_TransportError_ShowsConnectionHint()
    {
        var result = HistoryApiClient.ParseListResponse(0, null, "Request timeout");
        Assert.IsFalse(result.Success);
        Assert.AreEqual("无法连接后端，请检查地址与网络", result.ErrorMessage);
    }

    [Test]
    public void ParseList_MalformedBody_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            var result = HistoryApiClient.ParseListResponse(200, "<html>", null);
            Assert.IsFalse(result.Success);
        });
    }

    [Test]
    public void ParseList_FiltersEntriesWithoutId()
    {
        var body = "{\"data\":[" +
            MakeIssueJson("id-1", "2026-10-28T10:00:00+08:00") + "," +
            "{\"title\":\"坏条目无id\",\"priority\":\"low\"}]}";
        var result = HistoryApiClient.ParseListResponse(200, body, null);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.Issues.Length);
        Assert.AreEqual("id-1", result.Issues[0].id);
    }

    private static string MakeIssueJson(string id, string createdAt)
    {
        return "{\"id\":\"" + id + "\",\"title\":\"t\",\"description\":\"d\",\"priority\":\"medium\"," +
            "\"status\":\"open\",\"position\":{\"x\":1,\"y\":2,\"z\":3}," +
            "\"createdAt\":\"" + createdAt + "\",\"updatedAt\":\"" + createdAt + "\"}";
    }

    // ---------- 排序与过滤纯函数 ----------

    [Test]
    public void Sort_DescendingByCreatedAt_AndKeepsInputUnmodified()
    {
        var input = new[]
        {
            MakeIssue("a", "2026-10-26T08:00:00+08:00"),
            MakeIssue("c", "2026-10-28T08:00:00+08:00"),
            MakeIssue("b", "2026-10-27T08:00:00+08:00"),
        };
        var sorted = HistoryApiClient.SortByCreatedAtDescending(input);
        Assert.AreEqual(new[] { "c", "b", "a" }, new[] { sorted[0].id, sorted[1].id, sorted[2].id });
        Assert.AreEqual("a", input[0].id, "排序不得改写调用方数组");
    }

    [Test]
    public void FilterValid_NullOrEmptySource_ReturnsEmptyArray()
    {
        Assert.AreEqual(0, HistoryApiClient.FilterValid(null).Length);
        Assert.AreEqual(0, HistoryApiClient.FilterValid(new IssueDto[0]).Length);
    }

    // ---------- 展示格式化 ----------

    [Test]
    public void FormatTime_Rfc3339WithOffset_ConvertsToBeijingDisplay()
    {
        Assert.AreEqual("2026/10/28 07:15:24",
            HistoryApiClient.FormatBeijingTime("2026-10-28T07:15:24+08:00"));
    }

    [Test]
    public void FormatTime_GarbageInput_ReturnsOriginal()
    {
        Assert.AreEqual("not-a-time", HistoryApiClient.FormatBeijingTime("not-a-time"));
        Assert.AreEqual(string.Empty, HistoryApiClient.FormatBeijingTime(null));
    }

    [Test]
    public void Labels_PriorityAndStatus_MappedToChinese()
    {
        Assert.AreEqual("低", HistoryApiClient.PriorityLabel("low"));
        Assert.AreEqual("中", HistoryApiClient.PriorityLabel("medium"));
        Assert.AreEqual("高", HistoryApiClient.PriorityLabel("high"));
        Assert.AreEqual("-", HistoryApiClient.PriorityLabel(null));
        Assert.AreEqual("待处理", HistoryApiClient.StatusLabel("open"));
        Assert.AreEqual("处理中", HistoryApiClient.StatusLabel("in_progress"));
        Assert.AreEqual("已解决", HistoryApiClient.StatusLabel("resolved"));
    }

    [Test]
    public void FormatPosition_ThreeDecimals_NullSafe()
    {
        Assert.AreEqual("x 1.500   y 2.000   z -0.500",
            HistoryApiClient.FormatPosition(new IssuePosition { x = 1.5, y = 2, z = -0.5 }));
        Assert.AreEqual("-", HistoryApiClient.FormatPosition(null));
    }

    // ---------- 历史页按巡检员隔离（账号行与换账号清理） ----------

    [Test]
    public void AccountLine_DisplayName_StatesOwnScope()
    {
        Assert.AreEqual("当前账号：张三（仅显示本人上报）",
            HistoryPanelController.AccountLine("张三", "zhangsan"));
    }

    [Test]
    public void AccountLine_EmptyDisplayName_FallsBackToUsernameThenInspector()
    {
        Assert.AreEqual("当前账号：zhangsan（仅显示本人上报）",
            HistoryPanelController.AccountLine(null, "zhangsan"));
        Assert.AreEqual("当前账号：巡检员（仅显示本人上报）",
            HistoryPanelController.AccountLine("", ""));
    }

    [Test]
    public void IsAccountChanged_OnlyWhenLoadedAccountDiffers()
    {
        Assert.IsTrue(HistoryPanelController.IsAccountChanged("inspector", "zhangsan"),
            "加载过 inspector 的列表后切换到 zhangsan，必须清理旧账号数据");
        Assert.IsFalse(HistoryPanelController.IsAccountChanged("inspector", "inspector"));
        Assert.IsFalse(HistoryPanelController.IsAccountChanged("", "zhangsan"),
            "尚未加载过列表时没有需要清理的旧账号数据");
    }

    // ---------- 创建接口 401 语义（会话过期引导重登） ----------

    [Test]
    public void ParseCreate_401_SessionExpiredKeepsDraftGuidance()
    {
        var body = "{\"error\":{\"code\":\"authentication_required\",\"message\":\"未认证\"}}";
        var result = IssueApiClient.ParseCreateResponse(401, body, null);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.SessionExpired, "创建接口 401 必须带会话过期标记");
        StringAssert.Contains("重新登录", result.ErrorMessage);
    }

    [Test]
    public void ParseCreate_400NotExpired()
    {
        var result = IssueApiClient.ParseCreateResponse(400,
            "{\"error\":{\"code\":\"invalid_argument\",\"message\":\"标题不能为空\"}}", null);
        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.SessionExpired);
    }
}
