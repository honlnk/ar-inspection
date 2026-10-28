using System;

namespace Inspection.API
{
    /// <summary>登录请求体 {username,password}。注意：任何日志都不得输出该请求体（含密码）。</summary>
    [Serializable]
    public class LoginRequest
    {
        public string username;
        public string password;
    }

    /// <summary>当前用户信息（登录成功与 /api/auth/me 共用）：username / displayName / role。</summary>
    [Serializable]
    public class AuthUserDto
    {
        public string username;
        public string displayName;
        public string role;
    }

    /// <summary>认证接口统一信封：成功 {"data":{...用户...}}，失败 {"error":{...}}。</summary>
    [Serializable]
    public class AuthEnvelope
    {
        public AuthUserDto data;
        public ApiErrorBody error;
    }

    /// <summary>
    /// 登录结果：成功携带用户与从 Set-Cookie 解析出的会话令牌（HttpOnly 对程序化客户端
    /// 不构成限制，Unity 从响应头读取后以 Bearer 头携带）；失败携带中文提示。
    /// </summary>
    public readonly struct LoginResult
    {
        public readonly bool Success;
        public readonly AuthUserDto User;
        public readonly string Token;
        public readonly string ErrorMessage;

        public static LoginResult Ok(AuthUserDto user, string token) =>
            new LoginResult(true, user, token, null);

        public static LoginResult Fail(string message) =>
            new LoginResult(false, null, null, message);

        private LoginResult(bool success, AuthUserDto user, string token, string errorMessage)
        {
            Success = success;
            User = user;
            Token = token;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// 会话恢复（GET /api/auth/me）结果：Valid 表示令牌仍有效；
    /// SessionExpired=true 表示 401（需重新登录），网络等其它失败两者皆假。
    /// </summary>
    public readonly struct MeResult
    {
        public readonly bool Valid;
        public readonly bool SessionExpired;
        public readonly AuthUserDto User;
        public readonly string ErrorMessage;

        public static MeResult Ok(AuthUserDto user) => new MeResult(true, false, user, null);
        public static MeResult Expired(string message) => new MeResult(false, true, null, message);
        public static MeResult Fail(string message) => new MeResult(false, false, null, message);

        private MeResult(bool valid, bool expired, AuthUserDto user, string errorMessage)
        {
            Valid = valid;
            SessionExpired = expired;
            User = user;
            ErrorMessage = errorMessage;
        }
    }
}
