using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Inspection.API
{
    /// <summary>
    /// 创建问题的结果：成功携带完整 IssueDto；失败携带用户可读的中文提示（不面向用户抛技术细节）。
    /// SessionExpired=true 表示 401：草稿与输入全部保留，由表单引导重新登录（计划 006）。
    /// </summary>
    public readonly struct CreateIssueResult
    {
        public readonly bool Success;
        public readonly IssueDto Issue;
        public readonly string ErrorMessage;
        public readonly bool SessionExpired;

        public static CreateIssueResult Ok(IssueDto issue) => new CreateIssueResult(true, issue, null, false);
        public static CreateIssueResult Fail(string message) => new CreateIssueResult(false, null, message, false);
        public static CreateIssueResult Expired(string message) => new CreateIssueResult(false, null, message, true);

        private CreateIssueResult(bool success, IssueDto issue, string errorMessage, bool sessionExpired)
        {
            Success = success;
            Issue = issue;
            ErrorMessage = errorMessage;
            SessionExpired = sessionExpired;
        }
    }

    /// <summary>
    /// 创建接口的 HTTP 客户端：UnityWebRequest POST /api/issues，10 秒超时按失败处理不自动重试；
    /// 已登录时以 Authorization: Bearer 携带会话令牌（计划 006）。
    /// 响应判定抽成纯函数 <see cref="ParseCreateResponse"/>，供 EditMode 测试与真机共用同一套契约。
    /// 网络日志受「完整诊断」开关门控：默认只输出阶段与状态码摘要，不输出完整请求体。
    /// </summary>
    public class IssueApiClient : MonoBehaviour
    {
        public const int TimeoutSeconds = 10;

        /// <summary>发起创建请求，完成后一次性回调（主线程）。</summary>
        public IEnumerator CreateIssue(CreateIssueRequest body, Action<CreateIssueResult> onDone)
        {
            var baseUrl = BackendConfig.GetBaseUrl();
            if (baseUrl.Length == 0)
            {
                onDone?.Invoke(CreateIssueResult.Fail("未配置后端地址，请先在设置中填写"));
                yield break;
            }

            var url = baseUrl.TrimEnd('/') + "/api/issues";
            var json = JsonUtility.ToJson(body);
            // 完整诊断关闭：不输出完整请求体（业务数据），只留阶段摘要（计划 006 4.8）
            if (AR.DiagnosticsConfig.FullEnabled)
            {
                Debug.Log($"[IssueApiClient] POST {url} body={json}");
            }
            else
            {
                Debug.Log($"[IssueApiClient] POST {url}");
            }

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.timeout = TimeoutSeconds;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (AuthSession.HasToken)
                {
                    request.SetRequestHeader("Authorization", "Bearer " + AuthSession.Token);
                }
                yield return request.SendWebRequest();

                // 网络层失败（含超时）：无响应体可解析，直接走网络提示；
                // 协议层失败（4xx/5xx）：后端有 JSON 错误体，交给统一解析读中文 message。
                var transportError = request.result == UnityWebRequest.Result.ConnectionError
                    ? request.error
                    : null;
                var result = ParseCreateResponse(request.responseCode, request.downloadHandler?.text, transportError);
                // 成功响应详情（含 id）只在完整诊断开启时输出；失败摘要始终保留且不含凭据
                if (AR.DiagnosticsConfig.FullEnabled)
                {
                    Debug.Log($"[IssueApiClient] 响应 code={request.responseCode} result={(result.Success ? "成功 " + result.Issue.id : result.ErrorMessage)}");
                }
                else
                {
                    Debug.Log($"[IssueApiClient] 响应 code={request.responseCode} result={(result.Success ? "成功" : result.ErrorMessage)}");
                }
                onDone?.Invoke(result);
            }
        }

        /// <summary>
        /// 统一响应判定（创建接口）：
        /// 成功 = HTTP 201 且 data 非空、id 非空、status == open，否则一律按失败提示，不伪造成功；
        /// 401 = 会话过期（保留草稿引导重新登录）；其余失败优先展示后端 error.message（中文）；
        /// 无响应体/网络层错误给可理解的连接提示。
        /// </summary>
        public static CreateIssueResult ParseCreateResponse(long statusCode, string bodyText, string transportError)
        {
            if (!string.IsNullOrEmpty(transportError))
            {
                return CreateIssueResult.Fail("无法连接后端，请检查地址与网络");
            }

            if (statusCode == 401)
            {
                return CreateIssueResult.Expired("登录已过期，请重新登录");
            }

            IssueApiEnvelope envelope = null;
            var hasBody = !string.IsNullOrEmpty(bodyText);
            if (hasBody)
            {
                try
                {
                    envelope = JsonUtility.FromJson<IssueApiEnvelope>(bodyText);
                }
                catch (ArgumentException)
                {
                    // JsonUtility 对非法 JSON 抛 ArgumentException：按格式错误处理，不影响崩溃与后续流程
                    envelope = null;
                }
            }

            if (statusCode == 201)
            {
                var data = envelope?.data;
                if (data != null
                    && !string.IsNullOrEmpty(data.id)
                    && data.status == IssueContract.StatusOpen)
                {
                    return CreateIssueResult.Ok(data);
                }
                // 201 但结构异常：按格式错误处理，不让用户误以为已上报
                return CreateIssueResult.Fail("后端响应格式异常，未能确认上报结果，请稍后重试");
            }

            if (envelope?.error != null && !string.IsNullOrEmpty(envelope.error.message))
            {
                return CreateIssueResult.Fail(envelope.error.message);
            }

            if (hasBody && envelope == null)
            {
                return CreateIssueResult.Fail($"上报失败（HTTP {statusCode}），响应格式无法解析");
            }
            return CreateIssueResult.Fail($"上报失败（HTTP {statusCode}），请稍后重试");
        }
    }
}
