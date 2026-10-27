using System;
using UnityEngine;

namespace Inspection.API
{
    /// <summary>
    /// 后端地址的存取与格式化：App 唯一持久化配置（PlayerPrefs），无内置默认地址。
    /// 保存时去除首尾空白；保存即生效，无需重启。
    /// </summary>
    public static class BackendConfig
    {
        private const string BaseUrlKey = "inspection.backend.base_url";
        private const string FirstRunGuidedKey = "inspection.backend.first_run_guided";

        /// <summary>是否已保存过后端地址（含曾经保存过空串的情况）。</summary>
        public static bool HasSaved => PlayerPrefs.HasKey(BaseUrlKey);

        /// <summary>取后端地址（已去首尾空白）；从未保存返回空串。</summary>
        public static string GetBaseUrl()
        {
            return HasSaved ? (PlayerPrefs.GetString(BaseUrlKey) ?? string.Empty).Trim() : string.Empty;
        }

        /// <summary>保存后端地址（原样去首尾空白后持久化，不强制联网验证）。</summary>
        public static void Save(string rawUrl)
        {
            PlayerPrefs.SetString(BaseUrlKey, (rawUrl ?? string.Empty).Trim());
            PlayerPrefs.Save();
        }

        /// <summary>首次启动引导是否已展示过（引导只在从未配置地址时弹一次，之后不再打扰）。</summary>
        public static bool FirstRunGuided
        {
            get => PlayerPrefs.GetInt(FirstRunGuidedKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(FirstRunGuidedKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// 顶部栏展示用缩略：去协议头前缀显示，超过上限截中间加省略号。
        /// </summary>
        public static string ShortenForDisplay(string url)
        {
            var v = (url ?? string.Empty).Trim();
            if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                v = v.Substring("http://".Length);
            }
            else if (v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                v = v.Substring("https://".Length);
            }
            const int max = 24;
            if (v.Length <= max)
            {
                return v;
            }
            return v.Substring(0, 10) + "…" + v.Substring(v.Length - (max - 11));
        }
    }
}
