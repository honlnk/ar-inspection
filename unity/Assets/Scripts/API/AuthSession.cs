using UnityEngine;

namespace Inspection.API
{
    /// <summary>
    /// 登录会话的本地存储（计划 006）：只保存随机会话令牌与非敏感账号信息
    /// （用户名/显示名/角色），绝不保存密码。令牌放应用私有偏好（PlayerPrefs），
    /// 请求时以 Authorization: Bearer 头携带（计划第 200 行预留方案）。
    /// 用户名用于在共享设备上区分巡检员，避免历史列表残留上一个账号的数据。
    /// </summary>
    public static class AuthSession
    {
        private const string TokenKey = "inspection.auth.token";
        private const string UsernameKey = "inspection.auth.username";
        private const string DisplayNameKey = "inspection.auth.display_name";
        private const string RoleKey = "inspection.auth.role";

        /// <summary>本地是否存在会话令牌（不代表仍有效，有效性以 /api/auth/me 为准）。</summary>
        public static bool HasToken => PlayerPrefs.HasKey(TokenKey) && !string.IsNullOrEmpty(Token);

        /// <summary>会话令牌；未登录为空串。</summary>
        public static string Token => PlayerPrefs.GetString(TokenKey, string.Empty);

        /// <summary>登录账号用户名（如「inspector」「zhangsan」）；未登录为空串。</summary>
        public static string Username => PlayerPrefs.GetString(UsernameKey, string.Empty);

        /// <summary>登录账号显示名（如「巡检员」）；未登录为空串。</summary>
        public static string DisplayName => PlayerPrefs.GetString(DisplayNameKey, string.Empty);

        /// <summary>登录账号角色（inspector/admin）；未登录为空串。</summary>
        public static string Role => PlayerPrefs.GetString(RoleKey, string.Empty);

        /// <summary>已登录且账号是巡检员（Unity 主界面的准入条件）。</summary>
        public static bool IsInspector => Role == "inspector" && HasToken;

        /// <summary>保存会话（登录成功时调用）：令牌 + 非敏感账号信息。</summary>
        public static void Save(string token, string username, string displayName, string role)
        {
            PlayerPrefs.SetString(TokenKey, token ?? string.Empty);
            PlayerPrefs.SetString(UsernameKey, username ?? string.Empty);
            PlayerPrefs.SetString(DisplayNameKey, displayName ?? string.Empty);
            PlayerPrefs.SetString(RoleKey, role ?? string.Empty);
            PlayerPrefs.Save();
        }

        /// <summary>清除本地会话（退出登录、会话过期、恢复失败时调用）。</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.DeleteKey(UsernameKey);
            PlayerPrefs.DeleteKey(DisplayNameKey);
            PlayerPrefs.DeleteKey(RoleKey);
            PlayerPrefs.Save();
        }
    }
}
