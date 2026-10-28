using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Inspection.API;

namespace Inspection.UI
{
    /// <summary>
    /// 登录面板（计划 006 4.6）：独立全屏面板，未登录不能进入 AR 上报主界面
    /// （面板全屏不透明遮挡 + PlacementController 冻结放置双保险）。
    /// 启动先恢复有效会话（GET /api/auth/me），失败再要求登录；
    /// 管理员账号登录成功但被明确拒绝进入（提示用巡检员账号，并撤销该会话）；
    /// 密码只进请求体，本地只存会话令牌（AuthSession），绝不保存密码。
    /// </summary>
    public class LoginPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private AuthApiClient apiClient;
        [SerializeField] private InputField usernameInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Text loginLabel;
        [SerializeField] private Text noticeText;   // 状态行：恢复中/登录已过期/错误提示
        [SerializeField] private ToastView toast;

        public const string RoleInspector = "inspector";
        public const string RoleAdmin = "admin";

        private bool requesting;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Start()
        {
            // 面板在场景里默认激活（阻止未登录进入主界面）；启动即尝试恢复会话
            Show(null);
            if (BackendConfig.GetBaseUrl().Length > 0 && AuthSession.HasToken)
            {
                SetBusy("正在恢复会话…");
                StartCoroutine(apiClient.RestoreSession(OnRestoreFinished));
            }
            else
            {
                AuthSession.Clear();
                SetNotice("请登录巡检员账号");
            }
        }

        /// <summary>显示登录面板；notice 为可选提示（如「登录已过期，请重新登录」）。</summary>
        public void Show(string notice)
        {
            panelRoot.SetActive(true);
            passwordInput.text = string.Empty;
            requesting = false;
            SetNotice(notice ?? (AuthSession.HasToken ? "请登录巡检员账号" : "请登录巡检员账号"));
            RefreshLoginState();
        }

        /// <summary>会话过期入口（表单/历史面板 401 时调用）：保留面板与草稿，跳转登录。</summary>
        public void ShowForSessionExpired()
        {
            AuthSession.Clear();
            Show("登录已过期，请重新登录");
            if (toast != null)
            {
                toast.Show("登录已过期，请重新登录");
            }
        }

        /// <summary>登录按钮（编辑器绑定）：本地校验非空后发起登录；期间防连点。</summary>
        public void OnLoginClicked()
        {
            if (requesting)
            {
                return;
            }
            var username = usernameInput.text.Trim();
            var password = passwordInput.text;
            if (username.Length == 0 || password.Length == 0)
            {
                SetNotice("请输入用户名和密码");
                return;
            }
            if (BackendConfig.GetBaseUrl().Length == 0)
            {
                SetNotice("未配置后端地址，请点击下方「设置」先填写");
                return;
            }

            requesting = true;
            RefreshLoginState();
            SetNotice("登录中…");
            StartCoroutine(apiClient.Login(username, password, OnLoginFinished));
        }

        private void OnLoginFinished(LoginResult result)
        {
            requesting = false;
            RefreshLoginState();
            if (!result.Success)
            {
                // 凭据错误/网络失败：展示后端中文提示（用户名或密码错误），留在登录页
                SetNotice(result.ErrorMessage);
                return;
            }
            if (result.User.role == RoleAdmin)
            {
                // 管理员账号属于网页管理端：明确拒绝进入巡检端并撤销刚创建的会话
                StartCoroutine(apiClient.Logout(null));
                SetNotice("管理员账号请在网页管理端使用，Unity 巡检端请用巡检员账号登录");
                return;
            }
            if (result.User.role != RoleInspector)
            {
                StartCoroutine(apiClient.Logout(null));
                SetNotice("该账号角色无权使用巡检端");
                return;
            }

            AuthSession.Save(result.Token, result.User.username, result.User.displayName, result.User.role);
            EnterMain();
        }

        private void OnRestoreFinished(MeResult result)
        {
            if (result.Valid && result.User.role == RoleInspector)
            {
                // 恢复成功：同步用户名/显示名（令牌原样保留，服务端会话仍在有效期内）
                AuthSession.Save(AuthSession.Token, result.User.username, result.User.displayName, result.User.role);
                EnterMain();
                return;
            }
            AuthSession.Clear();
            SetNotice(result.SessionExpired
                ? "登录已过期，请重新登录"
                : (result.Valid ? "该账号角色无权使用巡检端，请用巡检员账号登录" : "请登录巡检员账号"));
            RefreshLoginState();
        }

        private void EnterMain()
        {
            panelRoot.SetActive(false);
            if (toast != null)
            {
                toast.Show($"已登录：{AuthSession.DisplayName}（巡检员）", 2.5f);
            }
        }

        private void SetBusy(string message)
        {
            SetNotice(message);
        }

        private void SetNotice(string message)
        {
            if (noticeText != null)
            {
                noticeText.text = message ?? string.Empty;
            }
        }

        private void RefreshLoginState()
        {
            if (loginButton == null)
            {
                return;
            }
            loginButton.interactable = !requesting;
            if (loginLabel != null)
            {
                loginLabel.text = requesting ? "登录中…" : "登录";
            }
        }
    }
}
