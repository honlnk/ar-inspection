using System;

namespace Inspection.API
{
    /// <summary>
    /// 三端统一的问题坐标契约（与 backend / web 的 position 字段严格一致）。
    /// JsonUtility 按字段原名序列化，字段名即契约，不要改名。
    /// </summary>
    [Serializable]
    public class IssuePosition
    {
        public float x;
        public float y;
        public float z;
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
