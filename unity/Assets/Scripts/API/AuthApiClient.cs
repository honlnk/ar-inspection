using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Inspection.API
{
    /// <summary>
    /// 认证客户端（计划 006）：登录 / 会话恢复 / 退出。响应判定抽成纯函数供
    /// EditMode 测试与真机共用同一套契约。日志只输出阶段与状态码，
    /// 永远不输出请求体（含密码）、令牌或 Cookie。
    /// </summary>
    public class AuthApiClient : MonoBehaviour
    {
        public const int TimeoutSeconds = 10;
        private const string SessionCookieName = "inspection_session";

        /// <summary>发起登录请求，完成后一次性回调（主线程）。</summary>
        public IEnumerator Login(string username, string password, Action<LoginResult> onDone)
        {
            var baseUrl = BackendConfig.GetBaseUrl();
            if (baseUrl.Length == 0)
            {
                onDone?.Invoke(LoginResult.Fail("未配置后端地址，请先在设置中填写"));
                yield break;
            }

            var url = baseUrl.TrimEnd('/') + "/api/auth/login";
            var json = JsonUtility.ToJson(new LoginRequest { username = username, password = password });
            Debug.Log("[AuthApiClient] 登录请求开始");

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.timeout = TimeoutSeconds;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                var transportError = request.result == UnityWebRequest.Result.ConnectionError
                    ? request.error
                    : null;
                var token = ExtractSessionToken(GetHeader(request, "SET-COOKIE"));
                var result = ParseLoginResponse(request.responseCode, request.downloadHandler?.text, transportError, token);
                Debug.Log($"[AuthApiClient] 登录结束 code={request.responseCode} result={(result.Success ? "成功 role=" + result.User.role : result.ErrorMessage)}");
                onDone?.Invoke(result);
            }
        }

        /// <summary>恢复会话（GET /api/auth/me，Bearer 携带本地令牌），完成后一次性回调。</summary>
        public IEnumerator RestoreSession(Action<MeResult> onDone)
        {
            var baseUrl = BackendConfig.GetBaseUrl();
            if (baseUrl.Length == 0 || !AuthSession.HasToken)
            {
                onDone?.Invoke(MeResult.Fail("无已保存会话"));
                yield break;
            }

            var url = baseUrl.TrimEnd('/') + "/api/auth/me";
            Debug.Log("[AuthApiClient] 会话恢复检查开始");

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.timeout = TimeoutSeconds;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + AuthSession.Token);
                yield return request.SendWebRequest();

                var transportError = request.result == UnityWebRequest.Result.ConnectionError
                    ? request.error
                    : null;
                var result = ParseMeResponse(request.responseCode, request.downloadHandler?.text, transportError);
                Debug.Log($"[AuthApiClient] 会话恢复结束 code={request.responseCode} valid={result.Valid}");
                onDone?.Invoke(result);
            }
        }

        /// <summary>退出登录（POST /api/auth/logout）：服务端尽力撤销，本地会话无论如何都会清除。</summary>
        public IEnumerator Logout(Action onDone)
        {
            var baseUrl = BackendConfig.GetBaseUrl();
            if (baseUrl.Length == 0 || !AuthSession.HasToken)
            {
                onDone?.Invoke();
                yield break;
            }

            var url = baseUrl.TrimEnd('/') + "/api/auth/logout";
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.timeout = TimeoutSeconds;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + AuthSession.Token);
                yield return request.SendWebRequest();
                // 幂等接口：任何结果（含网络失败/401）都不影响本地清除
                Debug.Log($"[AuthApiClient] 退出请求结束 code={request.responseCode}");
                onDone?.Invoke();
            }
        }

        /// <summary>从响应头字典取值（Unity 头名大小写行为不定，显式忽略大小写查找）。</summary>
        private static string GetHeader(UnityWebRequest request, string name)
        {
            var headers = request.GetResponseHeaders();
            if (headers == null)
            {
                return null;
            }
            foreach (var pair in headers)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// 从 Set-Cookie 头解析会话令牌（纯函数，供测试）：
        /// 形如 "inspection_session=abc…; Path=/; Max-Age=43200; HttpOnly; SameSite=Lax"。
        /// </summary>
        public static string ExtractSessionToken(string setCookieHeader)
        {
            if (string.IsNullOrEmpty(setCookieHeader))
            {
                return string.Empty;
            }
            foreach (var part in setCookieHeader.Split(';'))
            {
                var cookie = part.Trim();
                var eq = cookie.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                if (cookie.Substring(0, eq) == SessionCookieName)
                {
                    return cookie.Substring(eq + 1);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 登录响应判定（纯函数）：成功 = HTTP 200、data 用户完整（username/role 非空）且令牌非空；
        /// 401 优先展示后端统一提示（凭据错误）；网络失败给可理解的连接提示。
        /// </summary>
        public static LoginResult ParseLoginResponse(long statusCode, string bodyText, string transportError, string token)
        {
            if (!string.IsNullOrEmpty(transportError))
            {
                return LoginResult.Fail("无法连接后端，请检查地址与网络");
            }

            AuthEnvelope envelope = null;
            var hasBody = !string.IsNullOrEmpty(bodyText);
            if (hasBody)
            {
                try
                {
                    envelope = JsonUtility.FromJson<AuthEnvelope>(bodyText);
                }
                catch (ArgumentException)
                {
                    envelope = null;
                }
            }

            if (statusCode == 200)
            {
                var user = envelope?.data;
                if (user != null && !string.IsNullOrEmpty(user.username) && !string.IsNullOrEmpty(user.role))
                {
                    if (string.IsNullOrEmpty(token))
                    {
                        return LoginResult.Fail("登录成功但未收到会话令牌，无法保持登录");
                    }
                    return LoginResult.Ok(user, token);
                }
                return LoginResult.Fail("后端响应格式异常，请稍后重试");
            }

            if (envelope?.error != null && !string.IsNullOrEmpty(envelope.error.message))
            {
                return LoginResult.Fail(envelope.error.message);
            }
            return LoginResult.Fail($"登录失败（HTTP {statusCode}），请稍后重试");
        }

        /// <summary>会话恢复判定（纯函数）：200 且用户完整 = 有效；401 = 过期；其余按失败处理。</summary>
        public static MeResult ParseMeResponse(long statusCode, string bodyText, string transportError)
        {
            if (!string.IsNullOrEmpty(transportError))
            {
                return MeResult.Fail("无法连接后端，请检查地址与网络");
            }

            AuthEnvelope envelope = null;
            if (!string.IsNullOrEmpty(bodyText))
            {
                try
                {
                    envelope = JsonUtility.FromJson<AuthEnvelope>(bodyText);
                }
                catch (ArgumentException)
                {
                    envelope = null;
                }
            }

            if (statusCode == 200)
            {
                var user = envelope?.data;
                if (user != null && !string.IsNullOrEmpty(user.username) && !string.IsNullOrEmpty(user.role))
                {
                    return MeResult.Ok(user);
                }
                return MeResult.Fail("后端响应格式异常，请重新登录");
            }
            if (statusCode == 401)
            {
                return MeResult.Expired("登录已过期，请重新登录");
            }
            return MeResult.Fail($"会话检查失败（HTTP {statusCode}），请重新登录");
        }
    }
}
