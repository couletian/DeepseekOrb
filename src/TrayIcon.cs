using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinForms = System.Windows.Forms;

namespace DeepSeekOrb
{
    /// <summary>
    /// 托盘图标：点标题栏 ✕ 后程序缩到这里，双击唤回悬浮球，右键可唤起或彻底退出。
    /// 用 WinForms 的 NotifyIcon（WPF 没有原生托盘支持），菜单仍用 WPF 的 ContextMenu 保持风格一致。
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;

        internal event Action OpenRequested;
        internal event Action ExitRequested;

        internal TrayIcon()
        {
            _icon = new WinForms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "DeepSeek 悬浮窗",
                Visible = false
            };

            _icon.DoubleClick += (s, e) => OpenRequested?.Invoke();
            _icon.MouseUp += OnMouseUp;
        }

        internal bool Visible
        {
            get => _icon.Visible;
            set
            {
                _icon.Visible = value;

                if (value)
                {
                    // Win11 会把新托盘图标折叠进「隐藏图标」面板，提示一次免得用户找不到
                    _icon.ShowBalloonTip(4000, "DeepSeek 悬浮窗",
                        "已最小化到系统托盘。点任务栏的 ^ 可找到本图标，双击即可重新打开。",
                        WinForms.ToolTipIcon.Info);
                }
            }
        }

        private void OnMouseUp(object sender, WinForms.MouseEventArgs e)
        {
            if (e.Button != WinForms.MouseButtons.Right) return;

            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            menu.Items.Add(CreateItem("打开悬浮窗", () => OpenRequested?.Invoke()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateItem("退出", () => ExitRequested?.Invoke()));
            menu.IsOpen = true;
        }

        private static MenuItem CreateItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }

        /// <summary>直接用 exe 内嵌的应用图标，避免额外的资源加载路径。</summary>
        private static System.Drawing.Icon LoadIcon()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (icon != null) return icon;
                }
            }
            catch
            {
                // 提取失败时退回系统默认图标
            }
            return System.Drawing.SystemIcons.Application;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
