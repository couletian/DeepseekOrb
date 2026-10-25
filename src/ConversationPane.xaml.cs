using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekOrb
{
    /// <summary>窗口内的一个对话面板（纵向平铺的一行）。</summary>
    public partial class ConversationPane : UserControl
    {
        internal const string HomeUrl = "https://chat.deepseek.com/";

        private bool _ready;
        private bool _loadedOnce;
        private bool _suppressZoomEvent;
        private double _initialZoom = 1.0;

        internal event Action<ConversationPane> Activated;
        internal event Action<ConversationPane, double> ZoomChanged;
        internal event Action<ConversationPane> CloseRequested;

        internal bool IsReady => _ready;
        internal bool IsLoadedOnce => _loadedOnce;

        public ConversationPane()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Web.GotFocus += (s, e) => Activated?.Invoke(this);
            Web.PreviewMouseDown += (s, e) => Activated?.Invoke(this);
        }

        internal void SetIndex(int index)
        {
            IndexText.Text = "对话 " + index;
        }

        internal void SetInitialZoom(double zoom)
        {
            if (zoom > 0.2) _initialZoom = zoom;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Activated?.Invoke(this);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this);
        }

        // ───────────────────────── WebView2 ─────────────────────────

        internal void ApplyZoom(double zoom)
        {
            if (!_ready || zoom < 0.2) return;

            _suppressZoomEvent = true;
            try { Web.ZoomFactor = zoom; }
            finally { _suppressZoomEvent = false; }
        }

        /// <summary>主题切换时更新网页底色，避免加载瞬间闪白。</summary>
        internal void RefreshTheme()
        {
            if (!_ready) return;

            Web.DefaultBackgroundColor = Theme.IsDark
                ? System.Drawing.Color.FromArgb(255, 0x1B, 0x1D, 0x21)
                : System.Drawing.Color.FromArgb(255, 0xF7, 0xF7, 0xF8);
        }

        internal void GoBack()
        {
            if (_ready && Web.CoreWebView2.CanGoBack) Web.CoreWebView2.GoBack();
        }

        internal void Reload()
        {
            if (!_ready) return;

            Splash.Visibility = Visibility.Visible;
            SplashText.Text = "正在重新加载…";
            Web.CoreWebView2.Reload();
        }

        internal void Dispose()
        {
            try { Web.Dispose(); } catch { /* 已释放 */ }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_ready) return;

            try
            {
                // 所有面板共用同一个 Environment：同一浏览器进程、同一份登录态
                CoreWebView2Environment env = await WebHost.GetEnvironmentAsync();
                await Web.EnsureCoreWebView2Async(env);

                CoreWebView2 core = Web.CoreWebView2;

                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsSwipeNavigationEnabled = false;
                core.Settings.IsPinchZoomEnabled = true;
                core.Settings.IsZoomControlEnabled = true;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;

                core.NewWindowRequested += OnNewWindowRequested;
                core.PermissionRequested += OnPermissionRequested;
                core.ProcessFailed += OnProcessFailed;
                core.NavigationCompleted += OnNavigationCompleted;

                Web.DefaultBackgroundColor = Theme.IsDark
                    ? System.Drawing.Color.FromArgb(255, 0x1B, 0x1D, 0x21)
                    : System.Drawing.Color.FromArgb(255, 0xF7, 0xF7, 0xF8);

                _ready = true;

                ApplyZoom(_initialZoom);
                HookZoomWatch();

                core.Navigate(HomeUrl);
            }
            catch (Exception ex)
            {
                SplashText.Text = "DeepSeek 组件初始化失败\n" + ex.Message;
            }
        }

        private void HookZoomWatch()
        {
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                Microsoft.Web.WebView2.Wpf.WebView2.ZoomFactorProperty,
                typeof(Microsoft.Web.WebView2.Wpf.WebView2));
            descriptor?.AddValueChanged(Web, OnZoomValueChanged);
        }

        private void OnZoomValueChanged(object sender, EventArgs e)
        {
            if (_suppressZoomEvent || !_ready) return;
            ZoomChanged?.Invoke(this, Web.ZoomFactor);
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                Splash.Visibility = Visibility.Collapsed;
                _loadedOnce = true;
            }
            else
            {
                Splash.Visibility = Visibility.Visible;
                SplashText.Text = "加载失败（" + e.WebErrorStatus + "）\n点击刷新按钮重试";
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri)
                && uri.Host.EndsWith("deepseek.com", StringComparison.OrdinalIgnoreCase))
            {
                Web.CoreWebView2.Navigate(e.Uri);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
            catch
            {
                // 无默认浏览器时忽略
            }
        }

        private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            switch (e.PermissionKind)
            {
                case CoreWebView2PermissionKind.ClipboardRead:
                case CoreWebView2PermissionKind.Microphone:
                case CoreWebView2PermissionKind.Camera:
                    e.State = CoreWebView2PermissionState.Allow;
                    break;
                default:
                    e.State = CoreWebView2PermissionState.Default;
                    break;
            }
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
                || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                Splash.Visibility = Visibility.Visible;
                SplashText.Text = "页面已中断，正在重新加载…";
                Web.Reload();
            }
        }
    }
}
