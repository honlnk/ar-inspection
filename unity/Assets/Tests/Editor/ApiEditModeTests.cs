using NUnit.Framework;
using UnityEngine;
using Inspection.API;
using Inspection.AR;
using Inspection.UI;

/// <summary>
/// 计划 004 阶段 5 EditMode 测试：DTO 序列化与响应解析契约（不发真实网络请求、不模拟 AR），
/// 外加 tap 判定与跟随按钮投影两个纯逻辑（风险表「非交互逻辑用 EditMode 测试守契约」）。
/// 本文件不建 asmdef，编译进 Assembly-CSharp-Editor 后由 Test Runner 以 EditMode 发现执行。
/// </summary>
public class ApiEditModeTests
{
    // ---------- 创建请求序列化 ----------

    [Test]
    public void CreateRequest_SerializesExactlyFourClientFields()
    {
        var body = new CreateIssueRequest
        {
            title = "入口墙面破损",
            description = "左侧裂缝",
            priority = IssueContract.PriorityHigh,
            position = new IssuePosition { x = 0.42, y = 0.03, z = 1.26 },
        };
        var json = JsonUtility.ToJson(body);

        // 恰好 4 个顶层字段（id/status/时间不得出现），position 为嵌套对象
        Assert.AreEqual(1, CountOccurrences(json, "\"title\""));
        Assert.AreEqual(1, CountOccurrences(json, "\"description\""));
        Assert.AreEqual(1, CountOccurrences(json, "\"priority\""));
        Assert.AreEqual(1, CountOccurrences(json, "\"position\""));
        Assert.AreEqual(0, CountOccurrences(json, "\"id\""));
        Assert.AreEqual(0, CountOccurrences(json, "\"status\""));
        Assert.AreEqual(0, CountOccurrences(json, "\"createdAt\""));
        Assert.AreEqual(0, CountOccurrences(json, "\"updatedAt\""));
        StringAssert.Contains("\"title\":\"入口墙面破损\"", json);
        StringAssert.Contains("\"priority\":\"high\"", json);
        StringAssert.Contains("\"x\":0.42", json);
        StringAssert.Contains("\"y\":0.03", json);
        StringAssert.Contains("\"z\":1.26", json);
    }

    [Test]
    public void CreateRequest_EmptyDescription_SerializesAsEmptyString()
    {
        // JsonUtility 不支持字段省略：空描述序列化为 ""（后端对缺省与空串同等处理，见 IssueDtos 注释）
        var body = new CreateIssueRequest
        {
            title = "t",
            description = "",
            priority = IssueContract.PriorityMedium,
            position = new IssuePosition { x = 0, y = 0, z = 0 },
        };
        var json = JsonUtility.ToJson(body);
        StringAssert.Contains("\"description\":\"\"", json);
    }

    // ---------- 响应解析（成功 / 后端错误 / 网络失败 / 异常结构） ----------

    [Test]
    public void ParseResponse_201WithFullDto_SucceedsAndKeepsFields()
    {
        var body = "{\"data\":{\"id\":\"abc-123\",\"title\":\"入口墙面破损\",\"description\":\"裂缝\",\"priority\":\"high\"," +
                   "\"status\":\"open\",\"position\":{\"x\":0.42,\"y\":0.03,\"z\":1.26}," +
                   "\"createdAt\":\"2026-10-28T10:00:00+08:00\",\"updatedAt\":\"2026-10-28T10:00:00+08:00\"}}";
        var result = IssueApiClient.ParseCreateResponse(201, body, null);

        Assert.IsTrue(result.Success, "合法 201 响应应判定成功");
        Assert.AreEqual("abc-123", result.Issue.id);
        Assert.AreEqual("入口墙面破损", result.Issue.title);
        Assert.AreEqual(IssueContract.PriorityHigh, result.Issue.priority);
        Assert.AreEqual(IssueContract.StatusOpen, result.Issue.status);
        Assert.AreEqual(0.42f, result.Issue.position.x, 1e-4f);
        Assert.AreEqual(1.26f, result.Issue.position.z, 1e-4f);
    }

    [Test]
    public void ParseResponse_201MissingId_TreatedAsFormatError_NotSuccess()
    {
        var body = "{\"data\":{\"title\":\"t\",\"priority\":\"medium\",\"status\":\"open\"}}";
        var result = IssueApiClient.ParseCreateResponse(201, body, null);
        Assert.IsFalse(result.Success, "201 缺 id 不得伪造成功");
        StringAssert.Contains("格式异常", result.ErrorMessage);
    }

    [Test]
    public void ParseResponse_201WrongStatus_TreatedAsFormatError()
    {
        var body = "{\"data\":{\"id\":\"x1\",\"status\":\"in_progress\"}}";
        var result = IssueApiClient.ParseCreateResponse(201, body, null);
        Assert.IsFalse(result.Success, "201 但 status != open 不得判定成功");
    }

    [Test]
    public void ParseResponse_400WithChineseError_ShowsBackendMessage()
    {
        var body = "{\"error\":{\"code\":\"invalid_argument\",\"message\":\"标题不能为空\"}}";
        var result = IssueApiClient.ParseCreateResponse(400, body, null);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("标题不能为空", result.ErrorMessage);
    }

    [Test]
    public void ParseResponse_TransportError_ShowsConnectionHint()
    {
        var result = IssueApiClient.ParseCreateResponse(0, null, "Request timeout");
        Assert.IsFalse(result.Success);
        Assert.AreEqual("无法连接后端，请检查地址与网络", result.ErrorMessage);
    }

    [Test]
    public void ParseResponse_500WithEnvelope_ShowsBackendMessage()
    {
        var body = "{\"error\":{\"code\":\"internal_error\",\"message\":\"存储读写失败\"}}";
        var result = IssueApiClient.ParseCreateResponse(500, body, null);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("存储读写失败", result.ErrorMessage);
    }

    [Test]
    public void ParseResponse_MalformedBody_DoesNotThrowAndNotSuccess()
    {
        Assert.DoesNotThrow(() =>
        {
            var result = IssueApiClient.ParseCreateResponse(500, "<html>bad gateway</html>", null);
            Assert.IsFalse(result.Success);
        });
    }

    [Test]
    public void ParseResponse_201EmptyBody_TreatedAsFormatError()
    {
        var result = IssueApiClient.ParseCreateResponse(201, string.Empty, null);
        Assert.IsFalse(result.Success);
    }

    // ---------- tap 判定阈值 ----------

    [Test]
    public void TapJudge_QuickSmallTouch_IsTap()
    {
        Assert.IsTrue(PlacementController.IsTap(0.12f, 4f));
        Assert.IsTrue(PlacementController.IsTap(0.5f, 24f * 24f)); // 恰好在阈值边界（≤）
    }

    [Test]
    public void TapJudge_SlowOrMovedTouch_NotTap()
    {
        Assert.IsFalse(PlacementController.IsTap(0.51f, 4f)); // 超时
        Assert.IsFalse(PlacementController.IsTap(0.1f, 25f * 25f)); // 位移超限
    }

    // ---------- 放置距离校验（伪平面防线） ----------

    [Test]
    public void PlacementDistance_TabletopHit_Valid()
    {
        Assert.IsTrue(PlacementController.IsPlacementDistanceValid(0.5f));
        Assert.IsTrue(PlacementController.IsPlacementDistanceValid(
            PlacementController.MaxPlacementDistanceMeters)); // 边界（≤）合法
    }

    [Test]
    public void PlacementDistance_BogusFarPlane_Invalid()
    {
        // 真机实测：跟踪漂移产生伪平面，点击桌面命中 8.13m 外
        Assert.IsFalse(PlacementController.IsPlacementDistanceValid(8.13f));
        Assert.IsFalse(PlacementController.IsPlacementDistanceValid(
            PlacementController.MaxPlacementDistanceMeters + 0.01f));
    }

    [Test]
    public void PlacementInFront_PointAheadOfCamera_Valid()
    {
        var camPos = Vector3.zero;
        var forward = Vector3.forward;
        Assert.IsTrue(PlacementController.IsPlacementInFront(camPos, forward, new Vector3(0f, -0.5f, 0.6f)));
        Assert.IsTrue(PlacementController.IsPlacementInFront(camPos, forward,
            new Vector3(0f, 0f, PlacementController.MinForwardDistanceMeters))); // 边界（≥）合法
    }

    [Test]
    public void PlacementInFront_BehindOrAtCamera_Invalid()
    {
        var camPos = Vector3.zero;
        var forward = Vector3.forward;
        // 真机实测：伪命中落在相机背后（视口 z=-0.11）却通过了距离校验
        Assert.IsFalse(PlacementController.IsPlacementInFront(camPos, forward, new Vector3(0.5f, 0f, -0.11f)));
        Assert.IsFalse(PlacementController.IsPlacementInFront(camPos, forward, Vector3.zero)); // 与相机重合
    }

    // ---------- 跟随按钮屏幕投影 ----------

    private Camera testCamera;

    [SetUp]
    public void SetUpCamera()
    {
        testCamera = new GameObject("TestCamera").AddComponent<Camera>();
        testCamera.transform.position = Vector3.zero;
        testCamera.transform.rotation = Quaternion.identity; // 朝 +z
    }

    [TearDown]
    public void TearDownCamera()
    {
        if (testCamera != null)
        {
            UnityEngine.Object.DestroyImmediate(testCamera.gameObject);
        }
    }

    [Test]
    public void ReportButton_PointInFrontAndOnScreen_VisibleWithOffsetAbove()
    {
        var pos = MarkerReportButton.ComputeButtonScreenPosition(
            new Vector3(0f, 0f, 5f), testCamera, 1080, 2400, out var visible);
        Assert.IsTrue(visible);
        Assert.AreEqual(1080f / 2f, pos.x, 1f);
        Assert.AreEqual(2400f / 2f + MarkerReportButton.OffsetAboveMarkerPixels, pos.y, 1f);
    }

    [Test]
    public void ReportButton_PointBehindCamera_Hidden()
    {
        MarkerReportButton.ComputeButtonScreenPosition(
            new Vector3(0f, 0f, -1f), testCamera, 1080, 2400, out var visible);
        Assert.IsFalse(visible, "标记在相机背后应隐藏");
    }

    [Test]
    public void ReportButton_PointOffScreen_Hidden()
    {
        MarkerReportButton.ComputeButtonScreenPosition(
            new Vector3(50f, 0f, 5f), testCamera, 1080, 2400, out var visible);
        Assert.IsFalse(visible, "标记移出屏幕应隐藏");
    }

    [Test]
    public void ReportButton_PointNearTopEdge_ClampedToScreen()
    {
        // 视口 y≈0.95（相机原点朝 +z、FOV60）：屏幕投影 + 上偏移后越出上沿，应被夹回屏幕内
        var pos = MarkerReportButton.ComputeButtonScreenPosition(
            new Vector3(0f, 2.75f, 5f), testCamera, 1080, 2400, out var visible);
        Assert.IsTrue(visible);
        Assert.LessOrEqual(pos.y, 2400f - MarkerReportButton.ScreenMarginPixels);
    }

    // ---------- 后端地址配置 ----------

    private const string BaseUrlKey = "inspection.backend.base_url";
    private const string GuidedKey = "inspection.backend.first_run_guided";

    [TearDown]
    public void ClearPrefs()
    {
        PlayerPrefs.DeleteKey(BaseUrlKey);
        PlayerPrefs.DeleteKey(GuidedKey);
    }

    [Test]
    public void BackendConfig_SaveTrimsAndRoundTrips()
    {
        Assert.IsFalse(BackendConfig.HasSaved);
        BackendConfig.Save("  http://192.168.31.17:8080/  ");
        Assert.IsTrue(BackendConfig.HasSaved);
        Assert.AreEqual("http://192.168.31.17:8080/", BackendConfig.GetBaseUrl());
    }

    [Test]
    public void BackendConfig_FirstRunGuided_FlagRoundTrips()
    {
        Assert.IsFalse(BackendConfig.FirstRunGuided);
        BackendConfig.FirstRunGuided = true;
        Assert.IsTrue(BackendConfig.FirstRunGuided);
    }

    [Test]
    public void BackendConfig_ShortenForDisplay_StripsSchemeAndTruncates()
    {
        Assert.AreEqual("192.168.31.17:8080", BackendConfig.ShortenForDisplay("http://192.168.31.17:8080"));
        var longUrl = "http://" + new string('a', 60) + ":8080";
        var shortened = BackendConfig.ShortenForDisplay(longUrl);
        Assert.LessOrEqual(shortened.Length, 26, "超长地址应截断加省略号");
        StringAssert.Contains("…", shortened);
    }

    private static int CountOccurrences(string hay, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = hay.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
