using NUnit.Framework;
using UnityEngine;
using Inspection.API;

/// <summary>
/// 计划 006 认证契约 EditMode 测试：登录/会话恢复响应解析、Set-Cookie 令牌提取、
/// 登录请求体字段（含密码，仅验证序列化形状）、AuthSession 本地存取（不保存密码）。
/// 不发真实网络请求。
/// </summary>
public class AuthEditModeTests
{
    private const string TokenKey = "inspection.auth.token";
    private const string DisplayNameKey = "inspection.auth.display_name";
    private const string RoleKey = "inspection.auth.role";

    [TearDown]
    public void ClearPrefs()
    {
        PlayerPrefs.DeleteKey(TokenKey);
        PlayerPrefs.DeleteKey(DisplayNameKey);
        PlayerPrefs.DeleteKey(RoleKey);
    }

    // ---------- Set-Cookie 令牌提取 ----------

    [Test]
    public void ExtractToken_FullSetCookieHeader_GetsTokenOnly()
    {
        var header = "inspection_session=abc123def456; Path=/; Max-Age=43200; HttpOnly; SameSite=Lax";
        Assert.AreEqual("abc123def456", AuthApiClient.ExtractSessionToken(header));
    }

    [Test]
    public void ExtractToken_BareCookieValue_GetsToken()
    {
        Assert.AreEqual("tok", AuthApiClient.ExtractSessionToken("inspection_session=tok"));
    }

    [Test]
    public void ExtractToken_NullOrEmptyOrOtherCookie_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, AuthApiClient.ExtractSessionToken(null));
        Assert.AreEqual(string.Empty, AuthApiClient.ExtractSessionToken(string.Empty));
        Assert.AreEqual(string.Empty, AuthApiClient.ExtractSessionToken("other_cookie=x; Path=/"));
    }

    // ---------- 登录响应解析 ----------

    [Test]
    public void ParseLogin_200InspectorWithToken_Succeeds()
    {
        var body = "{\"data\":{\"username\":\"inspector\",\"displayName\":\"巡检员\",\"role\":\"inspector\"}}";
        var result = AuthApiClient.ParseLoginResponse(200, body, null, "tok-1");
        Assert.IsTrue(result.Success);
        Assert.AreEqual("inspector", result.User.username);
        Assert.AreEqual("巡检员", result.User.displayName);
        Assert.AreEqual("inspector", result.User.role);
        Assert.AreEqual("tok-1", result.Token);
    }

    [Test]
    public void ParseLogin_200Admin_ParsesSuccessfully_RoleRejectedByController()
    {
        // 管理员登录在协议层同样成功；「拒绝进入巡检端」由 LoginPanelController 按 role 判定
        var body = "{\"data\":{\"username\":\"admin\",\"displayName\":\"管理员\",\"role\":\"admin\"}}";
        var result = AuthApiClient.ParseLoginResponse(200, body, null, "tok-2");
        Assert.IsTrue(result.Success);
        Assert.AreEqual("admin", result.User.role);
    }

    [Test]
    public void ParseLogin_200MissingToken_FailsWithClearMessage()
    {
        var body = "{\"data\":{\"username\":\"inspector\",\"displayName\":\"巡检员\",\"role\":\"inspector\"}}";
        var result = AuthApiClient.ParseLoginResponse(200, body, null, string.Empty);
        Assert.IsFalse(result.Success);
        StringAssert.Contains("会话令牌", result.ErrorMessage);
    }

    [Test]
    public void ParseLogin_401ShowsBackendUniformMessage()
    {
        var body = "{\"error\":{\"code\":\"invalid_credentials\",\"message\":\"用户名或密码错误\"}}";
        var result = AuthApiClient.ParseLoginResponse(401, body, null, null);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("用户名或密码错误", result.ErrorMessage);
    }

    [Test]
    public void ParseLogin_TransportError_ShowsConnectionHint()
    {
        var result = AuthApiClient.ParseLoginResponse(0, null, "Request timeout", null);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("无法连接后端，请检查地址与网络", result.ErrorMessage);
    }

    [Test]
    public void ParseLogin_MalformedBody_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            var result = AuthApiClient.ParseLoginResponse(500, "<html>", null, null);
            Assert.IsFalse(result.Success);
        });
    }

    // ---------- 会话恢复（me）解析 ----------

    [Test]
    public void ParseMe_200Inspector_Valid()
    {
        var body = "{\"data\":{\"username\":\"inspector\",\"displayName\":\"巡检员\",\"role\":\"inspector\"}}";
        var result = AuthApiClient.ParseMeResponse(200, body, null);
        Assert.IsTrue(result.Valid);
        Assert.IsFalse(result.SessionExpired);
        Assert.AreEqual("巡检员", result.User.displayName);
    }

    [Test]
    public void ParseMe_401_SessionExpiredNotPlainFailure()
    {
        var body = "{\"error\":{\"code\":\"authentication_required\",\"message\":\"未认证\"}}";
        var result = AuthApiClient.ParseMeResponse(401, body, null);
        Assert.IsFalse(result.Valid);
        Assert.IsTrue(result.SessionExpired, "401 必须标记为会话过期（区别于网络失败）");
    }

    [Test]
    public void ParseMe_TransportError_PlainFailureNotExpired()
    {
        var result = AuthApiClient.ParseMeResponse(0, null, "Request timeout");
        Assert.IsFalse(result.Valid);
        Assert.IsFalse(result.SessionExpired, "网络失败不得当作会话过期清除本地会话");
    }

    // ---------- 登录请求体 ----------

    [Test]
    public void LoginRequest_SerializesExactlyTwoFields()
    {
        var json = JsonUtility.ToJson(new LoginRequest { username = "inspector", password = "pw" });
        StringAssert.Contains("\"username\":\"inspector\"", json);
        StringAssert.Contains("\"password\":\"pw\"", json);
        Assert.AreEqual(2, CountFields(json));
    }

    private static int CountFields(string json)
    {
        var count = 0;
        foreach (var token in new[] { "username", "password", "token", "role" })
        {
            if (json.Contains("\"" + token + "\":"))
            {
                count++;
            }
        }
        return count;
    }

    // ---------- AuthSession 本地存取 ----------

    [Test]
    public void AuthSession_SaveAndClear_RoundTripsWithoutPassword()
    {
        Assert.IsFalse(AuthSession.HasToken);
        AuthSession.Save("tok-abc", "巡检员", "inspector");
        Assert.IsTrue(AuthSession.HasToken);
        Assert.IsTrue(AuthSession.IsInspector);
        Assert.AreEqual("tok-abc", AuthSession.Token);
        Assert.AreEqual("巡检员", AuthSession.DisplayName);

        AuthSession.Clear();
        Assert.IsFalse(AuthSession.HasToken);
        Assert.IsFalse(AuthSession.IsInspector);
        Assert.AreEqual(string.Empty, AuthSession.Token);
    }

    [Test]
    public void AuthSession_AdminRole_IsNotInspector()
    {
        AuthSession.Save("tok", "管理员", "admin");
        Assert.IsTrue(AuthSession.HasToken);
        Assert.IsFalse(AuthSession.IsInspector, "管理员令牌存在也不得进入巡检主界面");
    }
}
