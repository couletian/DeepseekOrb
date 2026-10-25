using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepSeekOrb
{
    /// <summary>
    /// 屏幕抓取 + 模糊。走 GDI BitBlt 与自写盒式模糊，不依赖 System.Drawing.Common，
    /// 保证发布体积不增加额外程序集。
    /// </summary>
    internal static class ScreenCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }

        private const int SRCCOPY = 0x00CC0020;
        private const uint DIB_RGB_COLORS = 0;
        private const int BI_RGB = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
                                          IntPtr hdcSrc, int sx, int sy, int rop);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines,
                                            byte[] bits, ref BITMAPINFO bi, uint usage);

        /// <summary>抓取屏幕矩形（物理像素），返回已模糊的位图；失败返回 null。</summary>
        internal static BitmapSource CaptureBlurred(int x, int y, int w, int h,
                                                    double dpiX, double dpiY, int radius)
        {
            if (w <= 0 || h <= 0) return null;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;

            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hbm = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;

            try
            {
                hbm = CreateCompatibleBitmap(screenDc, w, h);
                if (hbm == IntPtr.Zero) return null;

                old = SelectObject(memDc, hbm);
                if (!BitBlt(memDc, 0, 0, w, h, screenDc, x, y, SRCCOPY)) return null;

                int stride = w * 4;
                var pixels = new byte[stride * h];

                var info = new BITMAPINFO();
                info.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                info.bmiHeader.biWidth = w;
                info.bmiHeader.biHeight = -h;   // 负值表示自上而下
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                info.bmiHeader.biCompression = BI_RGB;

                if (GetDIBits(memDc, hbm, 0, (uint)h, pixels, ref info, DIB_RGB_COLORS) == 0)
                    return null;

                BoxBlur(pixels, w, h, radius, 2);

                BitmapSource src = BitmapSource.Create(
                    w, h, dpiX * 96, dpiY * 96,
                    PixelFormats.Bgra32, null, pixels, stride);
                src.Freeze();
                return src;
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(memDc, old);
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>滑动窗口盒式模糊，两趟近似高斯；仅处理 BGR 三通道。</summary>
        private static void BoxBlur(byte[] px, int w, int h, int radius, int passes)
        {
            if (radius < 1) return;

            int stride = w * 4;
            int window = radius * 2 + 1;
            var tmp = new byte[px.Length];

            for (int pass = 0; pass < passes; pass++)
            {
                // 横向：px -> tmp
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int c = 0; c < 3; c++)
                    {
                        int sum = 0;
                        for (int i = -radius; i <= radius; i++)
                        {
                            int sx = i < 0 ? 0 : (i >= w ? w - 1 : i);
                            sum += px[row + sx * 4 + c];
                        }
                        for (int x = 0; x < w; x++)
                        {
                            tmp[row + x * 4 + c] = (byte)(sum / window);
                            int addX = x + radius + 1; if (addX >= w) addX = w - 1;
                            int subX = x - radius; if (subX < 0) subX = 0;
                            sum += px[row + addX * 4 + c] - px[row + subX * 4 + c];
                        }
                    }
                }

                // 纵向：tmp -> px
                for (int x = 0; x < w; x++)
                {
                    int col = x * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        int sum = 0;
                        for (int i = -radius; i <= radius; i++)
                        {
                            int sy = i < 0 ? 0 : (i >= h ? h - 1 : i);
                            sum += tmp[sy * stride + col + c];
                        }
                        for (int y = 0; y < h; y++)
                        {
                            px[y * stride + col + c] = (byte)(sum / window);
                            int addY = y + radius + 1; if (addY >= h) addY = h - 1;
                            int subY = y - radius; if (subY < 0) subY = 0;
                            sum += tmp[addY * stride + col + c] - tmp[subY * stride + col + c];
                        }
                    }
                }
            }

            // 抓屏结果不含有效 alpha，统一置为不透明
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        }
    }
}
