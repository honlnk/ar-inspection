using System;

namespace Inspection.API
{
    /// <summary>
    /// 三端统一的问题坐标契约（与 backend / web 的 position 字段严格一致）。
    /// 用 double 并在赋值前四舍五入 3 位：float 直接序列化会产出 0.4199999… 的精度噪音
    /// 污染三端数据，与表单展示（3 位小数）也对齐。JsonUtility 按字段原名序列化，字段名即契约，不要改名。
    /// </summary>
    [Serializable]
    public class IssuePosition
    {
        public double x;
        public double y;
        public double z;
    }

    /// <summary>
    /// 三端统一的问题数据契约（与 backend / web 字段严格一致）：
    /// id / title / description / priority / status / position / createdAt / updatedAt。
    /// priority、status 以字符串传递，取值见 <see cref="IssueContract"/>。
    /// </summary>
    [Serializable]
    public class IssueDto
    {
        public string id;
        public string title;
        public string description;
        public string priority;
        public string status;
        public IssuePosition position;
        public string createdAt;
        public string updatedAt;
    }

    /// <summary>
    /// 创建问题请求体：只含客户端可填的 4 个字段（title / description / priority / position{x,y,z}），
    /// 与后端 POST /api/issues 契约一致；id、status、时间一律由后端生成，客户端不传。
    /// 注意：JsonUtility 不支持字段省略，description 为空时也会序列化为 ""（后端对缺省与空串同等处理）。
    /// </summary>
    [Serializable]
    public class CreateIssueRequest
    {
        public string title;
        public string description;
        public string priority;
        public IssuePosition position;
    }

    /// <summary>后端统一错误体：{"error":{"code":…,"message":…}}，message 为中文提示。</summary>
    [Serializable]
    public class ApiErrorBody
    {
        public string code;
        public string message;
    }

    /// <summary>
    /// 后端统一响应信封：成功 {"data":…}，失败 {"error":…}。
    /// JsonUtility 对缺失字段解析为 null，一个类型可同时覆盖两种形态。
    /// </summary>
    [Serializable]
    public class IssueApiEnvelope
    {
        public IssueDto data;
        public ApiErrorBody error;
    }

    /// <summary>priority 与 status 的合法取值（与后端校验规则一致）。</summary>
    public static class IssueContract
    {
        public const string PriorityLow = "low";
        public const string PriorityMedium = "medium";
        public const string PriorityHigh = "high";

        public const string StatusOpen = "open";
        public const string StatusInProgress = "in_progress";
        public const string StatusResolved = "resolved";
    }
}
