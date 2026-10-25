using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeepSeekOrb
{
    public partial class MainWindow : Window
    {
        private const double BarHeight = 44;
        private const int MaxPanes = 4;
        private const double RowGap = 2;

        private readonly DispatcherTimer _hoverWatch;
        private readonly DispatcherTimer _saveTimer;
        private readonly Settings _settings;
        private readonly List<ConversationPane> _panes = new List<ConversationPane>();

        private ConversationPane _activePane;
        private bool _expanded;
        private bool _pinned;
        private DateTime? _outsideSince;

        internal bool IsExpanded => _expanded;

        public MainWindow()
        {
            InitializeComponent();

            _settings = SettingsStore.Load();

            _hoverWatch = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _hoverWatch.Tick += HoverWatch_Tick;

            // 用户拖动边框或分隔条后延迟落盘，避免拖拽过程中频繁写文件
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                SettingsStore.Save(_settings);
            };

            SizeChanged += MainWindow_SizeChanged;
            Loaded += MainWindow_Loaded;

            RestorePanes();
            RebuildLayout();
        }

        private void RestorePanes()
        {
            int count = _settings.RowRatios == null || _settings.RowRatios.Count == 0
                ? 1
                : Math.Min(_settings.RowRatios.Count, MaxPanes);

            for (int i = 0; i < count; i++) _panes.Add(CreatePane());
            _activePane = _panes[0];
        }

        protected override void OnClosed(EventArgs e)
        {
            _hoverWatch.Stop();
            _saveTimer.Stop();
            SettingsStore.Save(_settings);

            foreach (ConversationPane pane in _panes) pane.Dispose();

            base.OnClosed(e);
        }

        // ───────────────────────── 生命周期 ─────────────────────────

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeCompositionTransparent(this);
            Native.ApplyRoundedCorners(hwnd, Theme.IsDark);
            EnableGlass(hwnd);

            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }

        /// <summary>
        /// 无边框窗口不绘制非客户区，DWM 的 SYSTEMBACKDROP_TYPE 不会产生像素，
        /// 因此统一走 accent policy 亚克力路径，才能得到真实的背景模糊。
        /// </summary>
        private static void EnableGlass(IntPtr hwnd)
        {
            Native.EnableAcrylic(hwnd, AcrylicTint());
        }

        /// <summary>磨砂色调：深色为冷灰浓磨砂，浅色为高透明白雾。</summary>
        private static int AcrylicTint()
        {
            // 0xAABBGGRR
            return Theme.IsDark ? unchecked((int)0xB31C1D20) : 0x66F6F6F8;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 位置由 BeginPrewarm / Expand 决定，此处不再调整，否则会把预热窗口拉回屏幕内
            Native.MakeCompositionTransparent(this);
            EnableGlass(new WindowInteropHelper(this).Handle);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_expanded) return;
            if (Math.Abs(_settings.Width - Width) < 0.5
                && Math.Abs(_settings.Height - Height) < 0.5) return;

            _settings.Width = Width;
            _settings.Height = Height;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SETTINGCHANGE = 0x001A;

            if (msg == WM_SETTINGCHANGE && Theme.ReadIsDark() != Theme.IsDark)
            {
                Theme.Apply();
                Native.ApplyRoundedCorners(hwnd, Theme.IsDark);
                EnableGlass(hwnd);

                foreach (ConversationPane pane in _panes) pane.RefreshTheme();
            }
            return IntPtr.Zero;
        }

        // ───────────────────────── 纵向平铺布局 ─────────────────────────

        private ConversationPane CreatePane()
        {
            var pane = new ConversationPane();
            pane.SetInitialZoom(_settings.Zoom);

            pane.Activated += p => _activePane = p;
            pane.ZoomChanged += (p, zoom) => ApplyZoomToAll(zoom);
            pane.CloseRequested += RemovePane;

            return pane;
        }

        private void ApplyZoomToAll(double zoom)
        {
            if (Math.Abs(_settings.Zoom - zoom) < 0.001) return;

            _settings.Zoom = zoom;
            foreach (ConversationPane pane in _panes) pane.ApplyZoom(zoom);

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>
        /// 单列纵向平铺：每行一个面板，行间 2px 分隔条可拖动调整比例。
        /// 星号行随窗口高度自动缩放，因此拖动窗口边框时面板同步变大变小。
        /// </summary>
        private void RebuildLayout()
        {
            PaneHost.Children.Clear();
            PaneHost.RowDefinitions.Clear();

            int n = _panes.Count;
            List<double> ratios = NormalizedRatios(n);

            for (int i = 0; i < n; i++)
            {
                PaneHost.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(ratios[i], GridUnitType.Star)
                });

                if (i > 0)
                {
                    // 间隙行固定 2px，分隔条放在其中；拖动它改变相邻两行的比例
                    PaneHost.RowDefinitions.Insert(PaneHost.RowDefinitions.Count - 1,
                        new RowDefinition { Height = new GridLength(RowGap) });

                    GridSplitter splitter = CreateRowSplitter();
                    Grid.SetRow(splitter, i * 2 - 1);
                    PaneHost.Children.Add(splitter);
                }

                ConversationPane pane = _panes[i];
                pane.SetIndex(i + 1);

                // 每次都显式设行号，避免残留上一次布局的 RowSpan 导致面板重叠
                Grid.SetRow(pane, i * 2);
                Grid.SetColumn(pane, 0);
                Grid.SetRowSpan(pane, 1);
                Grid.SetColumnSpan(pane, 1);

                PaneHost.Children.Add(pane);
            }

            UpdateTitle();
        }

        private GridSplitter CreateRowSplitter()
        {
            var splitter = new GridSplitter
            {
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                ShowsPreview = false,
                Focusable = false
            };

            splitter.DragCompleted += (s, e) => SaveRowRatios();
            return splitter;
        }

        /// <summary>取归一化后的行高比例；没有记录时均分。</summary>
        private List<double> NormalizedRatios(int n)
        {
            var ratios = new List<double>();
            double sum = 0;

            for (int i = 0; i < n; i++)
            {
                double r = (_settings.RowRatios != null && i < _settings.RowRatios.Count)
                    ? _settings.RowRatios[i] : 0;
                if (r <= 0.01) r = 1.0;

                ratios.Add(r);
                sum += r;
            }

            for (int i = 0; i < n; i++) ratios[i] /= sum;
            return ratios;
        }

        private void SaveRowRatios()
        {
            var ratios = new List<double>();
            for (int i = 0; i < PaneHost.RowDefinitions.Count; i += 2)
            {
                GridLength length = PaneHost.RowDefinitions[i].Height;
                ratios.Add(length.IsStar ? length.Value : 1.0);
            }

            _settings.RowRatios = ratios;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveAll(bool immediate)
        {
            SaveRowRatios();
            if (immediate) SettingsStore.Save(_settings);
        }

        private void AddPane()
        {
            if (_panes.Count >= MaxPanes) return;

            ConversationPane pane = CreatePane();
            _panes.Insert(0, pane);   // 新面板出现在最上方，原有面板依次下移
            _activePane = pane;

            // 增加面板时按用户要求均分显示空间
            _settings.RowRatios = new List<double>();

            RebuildLayout();          // 先重建布局，再按新布局落盘
            SaveAll(immediate: true);
        }

        private void RemovePane(ConversationPane pane)
        {
            if (_panes.Count <= 1) return;

            _panes.Remove(pane);
            pane.Dispose();

            if (ReferenceEquals(_activePane, pane)) _activePane = _panes[0];

            _settings.RowRatios = new List<double>();   // 移除后重新均分

            RebuildLayout();
            SaveAll(immediate: true);
        }

        private void RemoveLastPane()
        {
            if (_panes.Count > 1) RemovePane(_panes[_panes.Count - 1]);
        }

        private void UpdateTitle()
        {
            int n = _panes.Count;
            if (n <= 1)
            {
                TitleText.Text = "DeepSeek";
                return;
            }

            int index = _activePane == null ? 1 : _panes.IndexOf(_activePane) + 1;
            TitleText.Text = $"DeepSeek · {index}/{n}";
        }

        // ───────────────────────── 展开 / 收起 ─────────────────────────

        /// <summary>
        /// 启动时把窗口停到屏幕外并加载 WebView2，让 Chromium 合成器保持待命。
        /// 实测 Hide() 会销毁合成器，再次展开需 1.5–2.5 秒才出画面，无法满足"悬停即用"。
        /// </summary>
        internal void BeginPrewarm()
        {
            if (_expanded) return;

            ApplyBounds(ComputeBounds());
            ParkOffscreen();
            Show();
        }

        internal void Expand()
        {
            if (_expanded) return;

            _expanded = true;
            _outsideSince = null;
            App.Orb.Hide();

            Rect target = ComputeBounds();
            CaptureBarBackdrop(target);
            ApplyBounds(target);
            if (!IsVisible) Show();

            // 窗口从屏幕外移回需要一两帧，过早开始探测会读到旧矩形而误判为"已移出"
            if (!_hoverWatch.IsEnabled)
            {
                var warmup = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
                warmup.Tick += (s, e) =>
                {
                    warmup.Stop();
                    if (_expanded) _hoverWatch.Start();
                };
                warmup.Start();
            }
        }

        internal void Collapse()
        {
            if (!_expanded) return;

            _expanded = false;
            _outsideSince = null;
            _hoverWatch.Stop();

            ParkOffscreen();

            // 等窗口移出屏幕后再让悬浮球抓屏，否则会拍到主窗口残影
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            delay.Tick += (s, e) =>
            {
                delay.Stop();
                App.Orb.ShowOrb();
            };
            delay.Start();
        }

        /// <summary>把窗口挪到虚拟桌面之外，同时保持 IsVisible=true，让 WebView2 持续待命。</summary>
        private void ParkOffscreen()
        {
            Left = SystemParameters.VirtualScreenLeft - Width - 200;
            Top = SystemParameters.VirtualScreenTop - Height - 200;
        }

        private Rect ComputeBounds()
        {
            Rect wa = SystemParameters.WorkArea;

            // 优先高窄：默认只占用纵向空间，横向留白；仅用户手动调整才改变宽度
            double w = _settings.Width > 0 ? _settings.Width : 420;
            double h = _settings.Height > 0 ? _settings.Height : 860;

            w = Math.Clamp(w, MinWidth, Math.Max(MinWidth, wa.Width - 40));
            h = Math.Clamp(h, MinHeight, Math.Max(MinHeight, wa.Height - 80));

            return new Rect(wa.Right - w - 24, wa.Bottom - h - 24, w, h);
        }

        private void ApplyBounds(Rect b)
        {
            Width = b.Width;
            Height = b.Height;
            Left = b.X;
            Top = b.Y;
        }

        private void CaptureBarBackdrop(Rect target)
        {
            try
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                int x = (int)Math.Round(target.X * dpi.DpiScaleX);
                int y = (int)Math.Round(target.Y * dpi.DpiScaleY);
                int w = (int)Math.Round(target.Width * dpi.DpiScaleX);
                int h = (int)Math.Round((BarHeight + 2) * dpi.DpiScaleY);
                if (w < 16 || h < 8) return;

                BitmapSource blurred = ScreenCapture.CaptureBlurred(
                    x, y, w, h, dpi.DpiScaleX, dpi.DpiScaleY, 10);

                if (blurred != null) BarBackdrop.Source = blurred;
            }
            catch
            {
                // 抓屏不可用（远程桌面、受保护内容）时保留色调层作为兜底
            }
        }

        private void HoverWatch_Tick(object sender, EventArgs e)
        {
            if (!_expanded || _pinned)
            {
                _outsideSince = null;
                return;
            }

            // 窗口处于激活状态说明用户正在使用（点击过窗口），此时不自动收起，
            // 避免阅读回复时鼠标移开、窗口凭空消失。
            if (IsActive)
            {
                _outsideSince = null;
                return;
            }

            // 还有面板没出画面就先留着，别让用户在等待时窗口自己消失
            if (_panes.Any(p => !p.IsLoadedOnce))
            {
                _outsideSince = null;
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (!Native.GetWindowRect(hwnd, out Native.RECT r)) return;
            if (!Native.GetCursorPos(out Native.POINT p)) return;

            const int pad = 10;
            bool inside = p.X >= r.Left - pad && p.X <= r.Right + pad
                       && p.Y >= r.Top - pad && p.Y <= r.Bottom + pad;

            if (inside)
            {
                _outsideSince = null;
                return;
            }

            if (_outsideSince == null)
            {
                _outsideSince = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - _outsideSince.Value).TotalMilliseconds >= 650)
                Collapse();
        }

        // ───────────────────────── 标题栏按钮 ─────────────────────────

        private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => _activePane?.GoBack();

        private void BtnReload_Click(object sender, RoutedEventArgs e) => _activePane?.Reload();

        private void BtnAddPane_Click(object sender, RoutedEventArgs e) => AddPane();

        private void BtnRemovePane_Click(object sender, RoutedEventArgs e) => RemoveLastPane();

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            _outsideSince = null;

            BtnPin.Content = _pinned ? "\uE77A" : "\uE718";
            BtnPin.Foreground = _pinned
                ? (Brush)FindResource("Accent")
                : (Brush)FindResource("TextSecondary");
            BtnPin.ToolTip = _pinned ? "取消固定" : "固定窗口（不自动收起）";
        }

        private void BtnCollapse_Click(object sender, RoutedEventArgs e) => Collapse();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
