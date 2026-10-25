using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DeepSeekOrb
{
    /// <summary>用户偏好。尺寸单位为 DIP，跨 DPI 变化仍然有效。</summary>
    internal sealed class Settings
    {
        /// <summary>窗口宽度，0 表示未设置。</summary>
        public double Width { get; set; }

        /// <summary>窗口高度，0 表示未设置。</summary>
        public double Height { get; set; }

        /// <summary>网页缩放倍数，0 表示未设置（默认 1.0）。</summary>
        public double Zoom { get; set; }

        /// <summary>
        /// 纵向平铺时每行的高度比例（星号值），列表长度即面板数量。
        /// 空表示均分。
        /// </summary>
        public List<double> RowRatios { get; set; } = new List<double>();
    }

    internal static class SettingsStore
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeekOrb");

        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        internal static Settings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Settings();
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath))
                       ?? new Settings();
            }
            catch
            {
                return new Settings();
            }
        }

        internal static void Save(Settings settings)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
            }
            catch
            {
                // 磁盘不可写时静默忽略，不影响正常使用
            }
        }
    }
}
