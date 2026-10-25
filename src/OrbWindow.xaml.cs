using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeepSeekOrb
{
    public partial class OrbWindow : Window
    {
        private const double BallSize = 52;
        private const double Pad = 2;
        private const double ScreenMargin = 22;

        private readonly DispatcherTimer _hoverDelay;
        private readonly DispatcherTimer _hoverPoll;
        private bool _hovering;

        /// <summary>收起后短暂忽略悬停，避免鼠标停在球上造成“展开—收起”抖动。</summary>
        private DateTime _suppressUntil = DateTime.MinValue;

        internal OrbWindow()
        {
            InitializeComponent();

            _hoverDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
            _hoverDelay.Tick += HoverDelay_Tick;

            // 分层窗口的 MouseEnter/Leave 不可靠（重绘时会误报离开），
            // 统一用光标坐标轮询，与主窗口的收起判定保持同一套逻辑
            _hoverPoll = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _hoverPoll.Tick += HoverPoll_Tick;
            _hoverPoll.Start();

            Place();
        }

        internal void ShowOrb()
        {
            Place();

            // 抓屏取背景时窗口自身必须不可见，否则会拍到残影
            if (IsVisible) Hide();
            CaptureBackdrop();

            Show();
            _suppressUntil = DateTime.UtcNow.AddMilliseconds(450);
        }

        private void Place()
        {
            Rect wa = SystemParameters.WorkArea;
            Width = BallSize + Pad * 2;
            Height = BallSize + Pad * 2;
            Left = wa.Right - ScreenMargin - BallSize - Pad;
            Top = wa.Bottom - ScreenMargin - BallSize - Pad;
        }

        /// <summary>截取悬浮球背后的桌面并模糊，作为玻璃底。</summary>
        private void CaptureBackdrop()
        {
            try
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                int x = (int)Math.Round(Left * dpi.DpiScaleX);
                int y = (int)Math.Round(Top * dpi.DpiScaleY);
                int w = (int)Math.Round(Width * dpi.DpiScaleX);
                int h = (int)Math.Round(Height * dpi.DpiScaleY);
                if (w < 8 || h < 8) return;

                BitmapSource blurred = ScreenCapture.CaptureBlurred(
                    x, y, w, h, dpi.DpiScaleX, dpi.DpiScaleY, 4);
                if (blurred != null) Backdrop.Source = blurred;
            }
            catch
            {
                // 抓屏不可用（远程桌面、受保护内容）时保留半透明雾面
            }
        }

        private void HoverPoll_Tick(object sender, EventArgs e)
        {
            if (!IsVisible)
            {
                _hovering = false;
                return;
            }

            if (!Native.GetCursorPos(out Native.POINT p)) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (!Native.GetWindowRect(hwnd, out Native.RECT r)) return;

            bool inside = p.X >= r.Left && p.X <= r.Right
                       && p.Y >= r.Top && p.Y <= r.Bottom;

            if (inside == _hovering) return;
            _hovering = inside;

            if (inside)
            {
                if (DateTime.UtcNow < _suppressUntil) return;

                // 先启动展开定时器，保证悬停主路径不被动画异常影响
                _hoverDelay.Start();

                try { AnimateHover(true); }
                catch (Exception) { /* 动画失败不影响展开 */ }
            }
            else
            {
                _hoverDelay.Stop();

                try { AnimateHover(false); }
                catch (Exception) { /* 动画失败不影响收起 */ }
            }
        }

        private void HoverDelay_Tick(object sender, EventArgs e)
        {
            _hoverDelay.Stop();
            if (DateTime.UtcNow < _suppressUntil) return;

            Hide();
            App.Shell.Expand();
        }

        private void AnimateHover(bool on)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(on ? 170 : 220);
            double scale = on ? 1.06 : 1.0;

            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(scale, duration) { EasingFunction = ease });
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(scale, duration) { EasingFunction = ease });

            Color tint = on
                ? Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);

            var brush = Tint.Background as SolidColorBrush;
            brush?.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(tint, duration) { EasingFunction = ease });
        }

        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            App.Shell.Expand();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
