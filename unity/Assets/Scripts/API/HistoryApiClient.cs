using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

namespace Inspection.API
{
    /// <summary>历史问题列表信封：成功 {"data":[…]}，失败 {"error":{…}}。</summary>
    [Serializable]
    public class IssueListEnvelope
    {
        public IssueDto[] data;
        public ApiErrorBody error;
    }

    /// <summary>
    /// 历史列表请求结果：成功携带按创建时间倒序的问题数组；SessionExpired=true 表示 401
    /// （保留面板与未提交草稿，引导重新登录，不显示成空列表——计划 006 4.6）。
    /// </summary>
    public readonly struct HistoryResult
    {
        public readonly bool Success;
        public readonly IssueDto[] Issues;
        public readonly string ErrorMessage;
        public readonly bool SessionExpired;

        public static HistoryResult Ok(IssueDto[] issues) => new HistoryResult(true, issues, null, false);
        public static HistoryResult Fail(string message) => new HistoryResult(false, null, message, false);
        public static HistoryResult Expired(string message) => new HistoryResult(false, null, message, true);

        private HistoryResult(bool success, IssueDto[] issues, string errorMessage, bool sessionExpired)
        {
            Success = success;
            Issues = issues;
            ErrorMessage = errorMessage;
            SessionExpired = sessionExpired;
        }
    }

    /// <summary>
    /// 历史问题列表客户端（计划 006 4.6）：GET /api/issues（巡检员仅返回本人上报），
    /// REST 拉取、无轮询无 WebSocket。解析/排序/时间格式化均为纯函数供 EditMode 测试。
    /// </summary>
    public class HistoryApiClient : MonoBehaviour
    {
        public const int TimeoutSeconds = 10;

        /// <summary>拉取历史列表，完成后一次性回调（主线程）。</summary>
        public IEnumerator FetchHistory(Action<HistoryResult> onDone)
        {
            var baseUrl = BackendConfig.GetBaseUrl();
            if (baseUrl.Length == 0)
            {
                onDone?.Invoke(HistoryResult.Fail("未配置后端地址，请先在设置中填写"));
                yield break;
            }
            if (!AuthSession.HasToken)
            {
                onDone?.Invoke(HistoryResult.Expired("登录已过期，请重新登录"));
                yield break;
            }

            var url = baseUrl.TrimEnd('/') + "/api/issues";
            Debug.Log($"[HistoryApiClient] GET {url}");

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.timeout = TimeoutSeconds;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + AuthSession.Token);
                yield return request.SendWebRequest();

                var transportError = request.result == UnityWebRequest.Result.ConnectionError
                    ? request.error
                    : null;
                var result = ParseListResponse(request.responseCode, request.downloadHandler?.text, transportError);
                // 只输出条数与状态码摘要，不输出业务数据详情（不含凭据）
                Debug.Log($"[HistoryApiClient] 响应 code={request.responseCode} count={(result.Success ? result.Issues.Length.ToString() : "-")} result={(result.Success ? "成功" : result.ErrorMessage)}");
                onDone?.Invoke(result);
            }
        }

        /// <summary>
        /// 列表响应判定（纯函数）：成功 = 200 且 data 为数组（过滤 id 为空的坏条目，可为空数组）；
        /// 401 = 会话过期；其余按失败处理并优先展示后端中文 message。
        /// </summary>
        public static HistoryResult ParseListResponse(long statusCode, string bodyText, string transportError)
        {
            if (!string.IsNullOrEmpty(transportError))
            {
                return HistoryResult.Fail("无法连接后端，请检查地址与网络");
            }
            if (statusCode == 401)
            {
                return HistoryResult.Expired("登录已过期，请重新登录");
            }

            IssueListEnvelope envelope = null;
            var hasBody = !string.IsNullOrEmpty(bodyText);
            if (hasBody)
            {
                try
                {
                    envelope = JsonUtility.FromJson<IssueListEnvelope>(bodyText);
                }
                catch (ArgumentException)
                {
                    envelope = null;
                }
            }

            if (statusCode == 200)
            {
                if (envelope?.data == null)
                {
                    return HistoryResult.Fail("后端响应格式异常，请稍后重试");
                }
                return HistoryResult.Ok(SortByCreatedAtDescending(FilterValid(envelope.data)));
            }

            if (envelope?.error != null && !string.IsNullOrEmpty(envelope.error.message))
            {
                return HistoryResult.Fail(envelope.error.message);
            }
            return HistoryResult.Fail($"加载失败（HTTP {statusCode}），请稍后重试");
        }

        /// <summary>过滤无效条目（id 为空视为坏数据，不进入列表）。</summary>
        public static IssueDto[] FilterValid(IssueDto[] source)
        {
            if (source == null || source.Length == 0)
            {
                return new IssueDto[0];
            }
            var count = 0;
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] != null && !string.IsNullOrEmpty(source[i].id))
                {
                    count++;
                }
            }
            var result = new IssueDto[count];
            var index = 0;
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] != null && !string.IsNullOrEmpty(source[i].id))
                {
                    result[index++] = source[i];
                }
            }
            return result;
        }

        /// <summary>
        /// 按创建时间倒序（新的在前）。后端时间为统一的 +08:00 RFC 3339 字符串，
        /// 同偏移下字典序即时间序；解析失败的条目按字符串比较仍保持确定性排序。
        /// </summary>
        public static IssueDto[] SortByCreatedAtDescending(IssueDto[] issues)
        {
            if (issues == null)
            {
                return new IssueDto[0];
            }
            var sorted = (IssueDto[])issues.Clone();
            Array.Sort(sorted, (a, b) => string.CompareOrdinal(b.createdAt, a.createdAt));
            return sorted;
        }

        /// <summary>
        /// 北京时间展示格式化（纯函数）："2026-10-28T07:15:24+08:00" → "2026/10/28 07:15:24"；
        /// 解析失败原样返回，不抛异常。
        /// </summary>
        public static string FormatBeijingTime(string rfc3339)
        {
            if (string.IsNullOrEmpty(rfc3339))
            {
                return rfc3339 ?? string.Empty;
            }
            if (DateTimeOffset.TryParse(rfc3339, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            return rfc3339;
        }

        /// <summary>优先级中文（列表展示用，与颜色语义并存——不只靠颜色传递含义）。</summary>
        public static string PriorityLabel(string priority)
        {
            switch (priority)
            {
                case IssueContract.PriorityLow: return "低";
                case IssueContract.PriorityMedium: return "中";
                case IssueContract.PriorityHigh: return "高";
                default: return string.IsNullOrEmpty(priority) ? "-" : priority;
            }
        }

        /// <summary>状态中文（列表展示用）。</summary>
        public static string StatusLabel(string status)
        {
            switch (status)
            {
                case IssueContract.StatusOpen: return "待处理";
                case IssueContract.StatusInProgress: return "处理中";
                case IssueContract.StatusResolved: return "已解决";
                default: return string.IsNullOrEmpty(status) ? "-" : status;
            }
        }

        /// <summary>三维坐标展示（与表单位置行同一 3 位小数约定）。</summary>
        public static string FormatPosition(IssuePosition position)
        {
            if (position == null)
            {
                return "-";
            }
            return $"x {position.x:F3}   y {position.y:F3}   z {position.z:F3}";
        }
    }
}
