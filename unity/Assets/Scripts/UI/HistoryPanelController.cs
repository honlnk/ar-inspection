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
    /// 「刷新」手动更新（无轮询无 WebSocket）。请求期间保留旧列表并显示加载状态；
    /// 首次失败显示错误（刷新按钮即重试）；刷新失败保留旧数据并提示可能过期；
    /// 空列表显示「暂无历史上报」；会话过期保留面板与未提交草稿并跳转登录。
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

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>打开面板：显示当前账号；未加载过则拉取一次，已有数据先展示旧列表。</summary>
        public void Open()
        {
            panelRoot.SetActive(true);
            accountText.text = "当前账号：" + (string.IsNullOrEmpty(AuthSession.DisplayName) ? "inspector" : AuthSession.DisplayName);
            if (!hasLoadedOnce)
            {
                Fetch(initial: true);
            }
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
            AuthSession.Clear();
            hasLoadedOnce = false; // 下次登录后重新拉取（账号可能不同）
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
            refreshButton.interactable = false;
            if (refreshLabel != null)
            {
                refreshLabel.text = "加载中…";
            }
            // 首次加载旧列表为空；刷新时旧列表保留，仅状态行提示（旧数据不错误清空）
            statusText.text = initial ? "加载中…" : "刷新中…";
            StartCoroutine(apiClient.FetchHistory(result => OnFetchFinished(initial, result)));
        }

        private void OnFetchFinished(bool initial, HistoryResult result)
        {
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
            RebuildList(result.Issues);
            statusText.text = result.Issues.Length == 0 ? "暂无历史上报" : string.Empty;
        }

        /// <summary>按模板重建列表（旧条目实例销毁重建，模板本身保持隐藏）。</summary>
        private void RebuildList(IssueDto[] issues)
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
