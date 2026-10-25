using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DeepSeekOrb
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        internal static MainWindow Shell { get; private set; }
        internal static OrbWindow Orb { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, @"Local\DeepSeekOrb.SingleInstance", out bool isNew);
            if (!isNew)
            {
                Shutdown();
                return;
            }

            Theme.Apply();

            base.OnStartup(e);

            Shell = new MainWindow();
            Orb = new OrbWindow();

            Orb.Show();
            Shell.BeginPrewarm();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* 未持有则忽略 */ }
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }

    /// <summary>跟随 Windows 应用主题（浅色 / 深色）切换界面画刷。</summary>
    internal static class Theme
    {
        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        internal static bool IsDark { get; private set; } = true;

        internal static bool ReadIsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    object v = key?.GetValue("AppsUseLightTheme");
                    if (v is int i) return i == 0;
                }
            }
            catch
            {
                // 注册表不可读时按深色处理
            }
            return true;
        }

        internal static void Apply()
        {
            IsDark = ReadIsDark();
            ResourceDictionary r = Application.Current.Resources;

            if (IsDark)
            {
                r["TextPrimary"] = Brush(0xF2, 0xF2, 0xF5);
                r["TextSecondary"] = Brush(0xC2, 0xC2, 0xC8);
                r["BtnHover"] = Brush(0xFF, 0xFF, 0xFF, 0x1F);
                r["BtnHoverClose"] = Brush(0xE8, 0x11, 0x23);
                r["BarTint"] = Brush(0x00, 0x00, 0x00, 0x99);
                r["PageBg"] = Brush(0x1B, 0x1D, 0x21);
                r["Stroke"] = Brush(0xFF, 0xFF, 0xFF, 0x1F);
            }
            else
            {
                r["TextPrimary"] = Brush(0x1A, 0x1A, 0x1C);
                r["TextSecondary"] = Brush(0x4A, 0x4A, 0x52);
                r["BtnHover"] = Brush(0x00, 0x00, 0x00, 0x12);
                r["BtnHoverClose"] = Brush(0xC4, 0x2B, 0x1C);
                r["BarTint"] = Brush(0xFF, 0xFF, 0xFF, 0xB8);
                r["PageBg"] = Brush(0xF7, 0xF7, 0xF8);
                r["Stroke"] = Brush(0x00, 0x00, 0x00, 0x1F);
            }
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 0xFF)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
