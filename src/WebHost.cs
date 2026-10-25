using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekOrb
{
    /// <summary>
    /// 全局共享的 WebView2 环境。
    ///
    /// 多开必须走这一条路：WebView2 对 userDataFolder 是独占的，若为每个窗口
    /// 起独立进程，第二个实例要么打不开，要么必须换目录——后者会导致每个窗口
    /// 都要单独登录。共享同一个 Environment 则共用一个浏览器进程与同一份
    /// 登录态，每个窗口只多出自己的渲染进程。
    /// </summary>
    internal static class WebHost
    {
        private static readonly string UserDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeekOrb", "WebView2");

        private static Task<CoreWebView2Environment> _envTask;

        internal static Task<CoreWebView2Environment> GetEnvironmentAsync()
        {
            return _envTask ??= CreateAsync();
        }

        private static async Task<CoreWebView2Environment> CreateAsync()
        {
            Directory.CreateDirectory(UserDataDir);

            var options = new CoreWebView2EnvironmentOptions
            {
                // 限制渲染进程数量与磁盘缓存上限，控制常驻资源与存储增长
                AdditionalBrowserArguments =
                    "--renderer-process-limit=2 --disk-cache-size=52428800 " +
                    "--disable-features=msPdfOOUI,msWebOOUI"
            };

            return await CoreWebView2Environment.CreateAsync(null, UserDataDir, options);
        }
    }
}
