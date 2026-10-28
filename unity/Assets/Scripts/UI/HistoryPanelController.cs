using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Inspection.API;

namespace Inspection.UI
{
    /// <summary>
    /// 历史问题列表面板（计划 006 4.6）：只读、按创建时间倒序、进入面板拉取一次 +
    /// 「刷新」手动更新（无轮询无 WebSocket）。列表由服务端按登录账号过滤，只包含当前
    /// 巡检员本人的上报；共享设备换账号或退出登录时丢弃旧账号的列表与在途请求。
    /// 请求期间保留旧列表并显示加载状态；首次失败显示错误（刷新按钮即重试）；
    /// 刷新失败保留旧数据并提示可能过期；空列表显示「暂无我的上报」；
    /// 会话过期保留面板与未提交草稿并跳转登录。
    /// 面板打开期间 PlacementController 冻结 AR 放置。
    /// </summary>
    public class HistoryPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private HistoryApiClient apiClient;
        [SerializeField] private AuthApiClient authClient;
        [SerializeField] private LoginPanelController loginPanel;
        [SerializeField] private Text accountText;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Text refreshLabel;
        [SerializeField] private Text statusText;
        [SerializeField] private RectTransform listContent;
        [SerializeField] private GameObject itemTemplate;

        private bool loading;
        private bool hasLoadedOnce;
        private string loadedUsername = string.Empty;
        private int fetchGeneration;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>打开面板：显示当前账号；账号与上次加载不同则先清空旧列表；未加载过则拉取一次。</summary>
        public void Open()
        {
            panelRoot.SetActive(true);
            if (IsAccountChanged(loadedUsername, AuthSession.Username))
            {
                ClearListForAccountChange();
            }
            accountText.text = AccountLine(AuthSession.DisplayName, AuthSession.Username);
            if (!hasLoadedOnce)
            {
                Fetch(initial: true);
            }
        }

        /// <summary>
        /// 面板保持打开时检测账号变化：会话过期期间不处理（保留旧列表，计划 006 4.6）；
        /// 重新登录为其他巡检员后立即清空旧账号数据并按新账号拉取。
        /// </summary>
        private void Update()
        {
            if (!IsOpen || !AuthSession.HasToken)
            {
                return;
            }
            if (!IsAccountChanged(loadedUsername, AuthSession.Username))
            {
                return;
            }
            ClearListForAccountChange();
            accountText.text = AccountLine(AuthSession.DisplayName, AuthSession.Username);
            Fetch(initial: true);
        }

        /// <summary>纯函数：已加载列表所属账号与当前账号不一致（共享设备换账号）。</summary>
        public static bool IsAccountChanged(string loadedUsername, string currentUsername)
        {
            return !string.IsNullOrEmpty(loadedUsername) &&
                   !string.Equals(loadedUsername, currentUsername, StringComparison.Ordinal);
        }

        /// <summary>纯函数：账号行文案，明确本页只显示当前巡检员本人的上报。</summary>
        public static string AccountLine(string displayName, string username)
        {
            var name = !string.IsNullOrEmpty(displayName)
                ? displayName
                : (!string.IsNullOrEmpty(username) ? username : "巡检员");
            return "当前账号：" + name + "（仅显示本人上报）";
        }

        /// <summary>关闭按钮（编辑器绑定）：收起面板（AR 放置恢复）。</summary>
        public void OnCloseClicked()
        {
            panelRoot.SetActive(false);
        }

        /// <summary>刷新按钮（编辑器绑定）：手动更新列表；首次失败时兼任重试。</summary>
        public void OnRefreshClicked()
        {
            if (!loading)
            {
                Fetch(initial: !hasLoadedOnce);
            }
        }

        /// <summary>退出登录（编辑器绑定）：尽力通知服务端撤销会话，本地清除后回到登录面板；草稿保留。</summary>
        public void OnLogoutClicked()
        {
            StartCoroutine(authClient.Logout(OnLogoutFinished));
        }

        private void OnLogoutFinished()
        {
            // 共享设备：退出即丢弃本账号列表并作废在途请求，避免下一个账号登录后看到残留数据
            ClearListForAccountChange();
            AuthSession.Clear();
            panelRoot.SetActive(false);
            var settings = FindAnyObjectByType<SettingsPanelController>(FindObjectsInactive.Include);
            if (settings != null)
            {
                settings.OnCloseClicked();
            }
            loginPanel.Show("已退出登录");
        }

        private void Fetch(bool initial)
        {
            loading = true;
            var generation = ++fetchGeneration;
            var requestedUsername = AuthSession.Username;
            refreshButton.interactable = false;
            if (refreshLabel != null)
            {
                refreshLabel.text = "加载中…";
            }
            // 首次加载旧列表为空；刷新时旧列表保留，仅状态行提示（旧数据不错误清空）
            statusText.text = initial ? "加载中…" : "刷新中…";
            StartCoroutine(apiClient.FetchHistory(result =>
                OnFetchFinished(generation, requestedUsername, initial, result)));
        }

        private void OnFetchFinished(int generation, string requestedUsername, bool initial, HistoryResult result)
        {
            // 账号已切换或已退出登录：丢弃旧账号的迟到响应，绝不在新账号名下展示旧数据
            if (generation != fetchGeneration ||
                !string.Equals(requestedUsername, AuthSession.Username, StringComparison.Ordinal))
            {
                return;
            }
            loading = false;
            refreshButton.interactable = true;
            if (refreshLabel != null)
            {
                refreshLabel.text = "刷新";
            }

            if (result.SessionExpired)
            {
                // 会话过期：保留面板与既有列表，跳转登录，不显示成空列表
                statusText.text = "登录已过期，请重新登录";
                loginPanel.ShowForSessionExpired();
                return;
            }
            if (!result.Success)
            {
                statusText.text = initial && !hasLoadedOnce
                    ? result.ErrorMessage
                    : result.ErrorMessage + "（当前列表可能不是最新）";
                return;
            }

            hasLoadedOnce = true;
            loadedUsername = AuthSession.Username;
            RebuildList(result.Issues);
            statusText.text = result.Issues.Length == 0 ? "暂无我的上报" : string.Empty;
        }

        /// <summary>
        /// 账号变化/退出登录：作废在途请求、清空列表并恢复可刷新状态，
        /// 保证界面不会在另一个巡检员名下继续展示上一个账号的数据。
        /// </summary>
        private void ClearListForAccountChange()
        {
            fetchGeneration++;
            loading = false;
            hasLoadedOnce = false;
            loadedUsername = string.Empty;
            ClearItems();
            if (refreshButton != null)
            {
                refreshButton.interactable = true;
            }
            if (refreshLabel != null)
            {
                refreshLabel.text = "刷新";
            }
            if (statusText != null)
            {
                statusText.text = string.Empty;
            }
        }

        /// <summary>销毁全部列表条目（模板本身保持隐藏）。</summary>
        private void ClearItems()
        {
            for (var i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i);
                if (child.gameObject == itemTemplate)
                {
                    continue;
                }
                Destroy(child.gameObject);
            }
        }

        /// <summary>按模板重建列表（旧条目实例销毁重建，模板本身保持隐藏）。</summary>
        private void RebuildList(IssueDto[] issues)
        {
            ClearItems();

            foreach (var issue in issues)
            {
                var item = Instantiate(itemTemplate, listContent);
                item.name = "HistoryItem_" + issue.id;
                FillItem(item.transform, issue);
                item.SetActive(true);
            }
        }

        private static void FillItem(Transform item, IssueDto issue)
        {
            SetText(item, "TitleText", issue.title);
            SetText(item, "PriorityText", "优先级 " + HistoryApiClient.PriorityLabel(issue.priority));
            SetText(item, "StatusText", HistoryApiClient.StatusLabel(issue.status));
            SetText(item, "DescText", string.IsNullOrEmpty(issue.description) ? "（无描述）" : issue.description);
            SetText(item, "PositionText", HistoryApiClient.FormatPosition(issue.position));
            SetText(item, "TimeText",
                $"创建 {HistoryApiClient.FormatBeijingTime(issue.createdAt)} · 更新 {HistoryApiClient.FormatBeijingTime(issue.updatedAt)}");
        }

        private static void SetText(Transform item, string childName, string value)
        {
            var text = item.Find(childName)?.GetComponent<Text>();
            if (text == null)
            {
                text = item.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.gameObject.name == childName);
            }
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}
