using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BubbleScr
{
    public static class Nat
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; public SIZE(int w, int h) { cx = w; cy = h; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
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
        public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public int bmiColors; }

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hDC, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("gdi32.dll", SetLastError = true)] public static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, uint rop);
        [DllImport("msimg32.dll", SetLastError = true)] public static extern bool AlphaBlend(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
                                                                       IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc,
                                                                       BLENDFUNCTION blend);

        /* ---- 空闲检测：托盘模式靠它判断「该开始飘了」 ---- */

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("kernel32.dll")]
        public static extern uint GetTickCount();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>距上一次键鼠输入的毫秒数（GetTickCount 是 32 位，会回绕，用无符号减法自然正确）</summary>
        public static uint IdleMs()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked(GetTickCount() - li.dwTime);
        }
    }

    /// <summary>
    /// 极简运行日志：写到 %TEMP%\BubbleGlass.log。
    /// 全屏分层窗口盖住桌面时看不到任何报错，出了问题只能靠它把运行时真相带出来。
    /// </summary>
    public static class Dbg
    {
        public static readonly string File =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BubbleGlass.log");

        public static void Reset()
        {
            try
            {
                System.IO.File.WriteAllText(File,
                    "BubbleGlass log  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n",
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        public static void W(string s)
        {
            try
            {
                System.IO.File.AppendAllText(File,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n",
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>一枚预乘 alpha 的精灵，直接放在 DIB 里，交给 GDI 做 AlphaBlend</summary>
    public class Sprite
    {
        public int Size;
        public IntPtr Bmp;
    }

    public class Bubble
    {
        // 平移用「方向 + 速度(px/秒)」，与帧率无关，也不会像速度衰减那样越走越慢
        public float X, Y, R, Phase;
        public float Dir, Spd, Turn;
        // 出生时的基准速度：碰撞会让速度在气泡之间转移，慢慢拉回来才不会全体趋同
        public float BaseSpd;
        // 四个亚像素相位：把小数位置烘焙进像素，画的时候只需整数坐标
        public Sprite[] Ph = new Sprite[4];
    }

    public static class Settings
    {
        const string Path = "Software\\BubbleScr";

        public static int Bubbles
        {
            get { return Read("Bubbles", 0); }
            set { Write("Bubbles", value); }
        }

        public static int Fps
        {
            get
            {
                int v = Read("Fps", 60);
                if (v < 12) v = 12;
                if (v > 60) v = 60;
                return v;
            }
            set { Write("Fps", value); }
        }

        /// <summary>漂浮速度，百分比。100 = 基准速度</summary>
        public static int Speed
        {
            get
            {
                int v = Read("Speed", 100);
                if (v < 30) v = 30;
                if (v > 300) v = 300;
                return v;
            }
            set { Write("Speed", value); }
        }

        /// <summary>闲置多少分钟后自动开始（0 = 不自动开始）。托盘模式使用</summary>
        public static int IdleMinutes
        {
            get
            {
                int v = Read("IdleMin", 5);
                if (v < 0) v = 0;
                if (v > 120) v = 120;
                return v;
            }
            set { Write("IdleMin", value); }
        }

        static int Read(string name, int def)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Path))
                {
                    if (k == null) return def;
                    object o = k.GetValue(name);
                    if (o == null) return def;
                    return Convert.ToInt32(o);
                }
            }
            catch { return def; }
        }

        static void Write(string name, int v)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Path))
                {
                    if (k != null) k.SetValue(name, v, RegistryValueKind.DWord);
                }
            }
            catch { }
        }
    }

    public class Overlay : Form
    {
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int ULW_ALPHA = 0x02;
        const int AC_SRC_ALPHA = 0x01;
        const int HUE_N = 720;
        const int PROF_N = 2048;
        const float PROF_MAX = 1.08f;

        static float[] profA;
        static float[] profW;
        static byte[] hueR, hueG, hueB;
        static float[] angLight;

        readonly List<Bubble> list = new List<Bubble>();
        readonly Random rnd = new Random();
        readonly Stopwatch sw = new Stopwatch();
        readonly bool[] keyBase = new bool[256];

        IntPtr screenDc, memDc, srcDc, dib, dibBits, oldDib;
        Nat.BLENDFUNCTION blend;
        int w, h, fps, maxBubbles, speedPct, frameIdx;
        float velScale;
        bool armed, built, quitLoop, useFlush = true;
        Nat.POINT armPt;
        double lastMs;
        float tsec;

        public Overlay(int maxBubbles, int fps)
        {
            try { if (!Nat.SetProcessDpiAwarenessContext(new IntPtr(-4))) Nat.SetProcessDPIAware(); }
            catch { try { Nat.SetProcessDPIAware(); } catch { } }

            this.maxBubbles = maxBubbles;
            this.fps = fps < 12 ? 12 : (fps > 60 ? 60 : fps);
            this.speedPct = Settings.Speed;

            Rectangle b = Screen.PrimaryScreen.Bounds;
            w = b.Width;
            h = b.Height;

            // 速度按屏幕尺寸缩放，2K/4K 上的观感不至于比 1080p 慢一半
            velScale = Math.Min(w, h) / 1080f;
            if (velScale < 0.7f) velScale = 0.7f;
            if (velScale > 2.2f) velScale = 2.2f;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = b;
            BackColor = Color.Black;
            Text = "Bubble Screen";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 分层 + 鼠标穿透 + 不进 Alt+Tab：气泡只是浮在桌面上的一层画
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (built) return;
            built = true;
            try
            {
                Build();
            }
            catch (Exception ex)
            {
                Dbg.W("BUILD FAILED: " + ex);
                throw;
            }
            int ex = Nat.GetWindowLong(Handle, -20);
            Dbg.W("shown  " + w + "x" + h + "  bubbles=" + list.Count
                  + "  layered=" + ((ex & WS_EX_LAYERED) != 0)
                  + "  clickthrough=" + ((ex & WS_EX_TRANSPARENT) != 0));
            BeginInvoke(new MethodInvoker(StartLoop));
        }

        // 分层窗口的内容完全由 UpdateLayeredWindow 提供，这里不能再去擦背景，
        // 否则消息循环里冒出一个 WM_PAINT 就会闪一帧黑色。
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            quitLoop = true;
            base.OnFormClosing(e);
        }

        /// <summary>
        /// 自绘循环。用 DwmFlush 对齐显示器垂直同步，帧间隔由此变得均匀；
        /// 若 DwmFlush 不生效（合成被关掉等），退化为按目标周期睡眠补足。
        /// </summary>
        void StartLoop()
        {
            Nat.timeBeginPeriod(1);
            sw.Start();
            lastMs = 0;

            double period = 1000.0 / fps;
            Dbg.W("loop start  fps=" + fps + "  period=" + period.ToString("F2") + "ms  speed=" + speedPct + "%");

            while (!quitLoop && !IsDisposed)
            {
                try
                {
                    Application.DoEvents();
                    if (quitLoop || IsDisposed) break;

                    double now = sw.Elapsed.TotalMilliseconds;
                    tsec = (float)(now / 1000.0);

                    float dt = (float)(now - lastMs);
                    lastMs = now;
                    if (dt <= 0f) dt = 16f;
                    if (dt > 100f) dt = 100f;

                    if (!Step(dt / 16.667f)) break;
                    PaintScene();
                    Present();

                    // 等一个垂直同步，帧间隔就均匀了；万一这台机器的合成器不配合，就退化成睡眠补时
                    if (useFlush)
                    {
                        double before = sw.Elapsed.TotalMilliseconds;
                        Nat.DwmFlush();
                        double flushMs = sw.Elapsed.TotalMilliseconds - before;
                        if (flushMs > 200.0) { useFlush = false; Dbg.W("DwmFlush too slow (" + flushMs.ToString("F1") + "ms), disabled"); }
                    }

                    double rest = period - (sw.Elapsed.TotalMilliseconds - now);
                    if (rest > 1.2)
                    {
                        int ms = (int)rest - 1;
                        if (ms > 0) System.Threading.Thread.Sleep(ms);
                    }

                    frameIdx++;
                    if (frameIdx % 180 == 0)
                        Dbg.W("alive  frames=" + frameIdx + "  avg=" + (sw.Elapsed.TotalMilliseconds / frameIdx).ToString("F2") + "ms");
                }
                catch (Exception ex)
                {
                    Dbg.W("LOOP FAILED at frame " + frameIdx + ": " + ex);
                    break;
                }
            }
            Dbg.W("loop exit  frames=" + frameIdx);
        }

        void Build()
        {
            if (profA == null) BuildTables();

            screenDc = Nat.GetDC(IntPtr.Zero);
            memDc = Nat.CreateCompatibleDC(screenDc);
            srcDc = Nat.CreateCompatibleDC(screenDc);

            Nat.BITMAPINFO bmi = new Nat.BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(Nat.BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;
            bmi.bmiHeader.biSizeImage = w * h * 4;

            dib = Nat.CreateDIBSection(screenDc, ref bmi, 0, out dibBits, IntPtr.Zero, 0);
            oldDib = Nat.SelectObject(memDc, dib);

            blend.BlendOp = 0;            // AC_SRC_OVER
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = 1;        // AC_SRC_ALPHA

            int n = maxBubbles;
            if (n <= 0)
            {
                n = (int)Math.Round((w * h) / 92000.0);
                if (n < 11) n = 11;
                if (n > 24) n = 24;
            }

            float scale = Math.Min(w, h) / 780f;
            if (scale < 0.62f) scale = 0.62f;
            if (scale > 1.6f) scale = 1.6f;

            for (int i = 0; i < n; i++) list.Add(NewBubble(scale));

            Dbg.W("build ok  " + w + "x" + h + "  bubbles=" + list.Count
                  + "  sprite=" + (list.Count > 0 ? list[0].Ph[0].Size : 0));
        }

        /// <summary>
        /// 不开窗口，只跑一次 Build + 一帧绘制，把 DIB 原样存成 PNG。
        /// 这样"绘制对不对"和"窗口合成对不对"可以分开验证。
        /// </summary>
        public void DumpOneFrame()
        {
            Build();
            tsec = 1.0f;
            Step(1.0f);
            PaintScene();

            int visible = 0;
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 4)
                    if (Marshal.ReadByte(dibBits, (y * w + x) * 4 + 3) > 0) visible++;

            // 透明区域先合成到浅灰上再存：否则看图软件显示成黑白格，看不出边缘真实形状
            int bytes = w * h * 4;
            byte[] px = new byte[bytes];
            Marshal.Copy(dibBits, px, 0, bytes);
            const float CB = 242f;
            for (int i = 0; i < bytes; i += 4)
            {
                float a = px[i + 3] / 255f;
                if (a >= 1f) continue;
                float inv = 1f - a;
                px[i + 0] = (byte)(px[i + 0] + CB * inv);
                px[i + 1] = (byte)(px[i + 1] + CB * inv);
                px[i + 2] = (byte)(px[i + 2] + CB * inv);
                px[i + 3] = 255;
            }

            string p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BubbleGlass-frame.png");
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(px, y * w * 4, IntPtr.Add(bd.Scan0, y * bd.Stride), w * 4);
                }
                finally { bmp.UnlockBits(bd); }
                bmp.Save(p, System.Drawing.Imaging.ImageFormat.Png);
            }
            Dbg.W("dump  visibleSamples=" + visible + "  saved=" + p);
        }

        Bubble NewBubble(float scale)
        {
            Bubble b = new Bubble();
            float z = 0.45f + (float)rnd.NextDouble() * 0.55f;
            b.R = (26f + (float)rnd.NextDouble() * 68f) * scale * (0.55f + z * 0.6f);
            if (b.R < 22f) b.R = 22f;
            if (b.R > 150f) b.R = 150f;

            b.X = (float)(rnd.NextDouble() * Math.Max(1.0, w - 2 * b.R)) + b.R;
            b.Y = (float)(rnd.NextDouble() * Math.Max(1.0, h - 2 * b.R)) + b.R;

            // 别和已有气泡生在同一点，否则一开场就挤成一团、被碰撞解算猛推一下
            for (int attempt = 0; attempt < 48; attempt++)
            {
                bool clash = false;
                for (int k = 0; k < list.Count; k++)
                {
                    float ddx = b.X - list[k].X;
                    float ddy = b.Y - list[k].Y;
                    float need = (b.R + list[k].R) * 0.98f + 8f;
                    if (ddx * ddx + ddy * ddy < need * need) { clash = true; break; }
                }
                if (!clash) break;
                b.X = (float)(rnd.NextDouble() * Math.Max(1.0, w - 2 * b.R)) + b.R;
                b.Y = (float)(rnd.NextDouble() * Math.Max(1.0, h - 2 * b.R)) + b.R;
            }

            // 速度单位是像素/秒，跟帧率解耦；近处的气泡更快更大，形成纵深
            b.Dir = (float)(rnd.NextDouble() * 6.2831853);
            b.Spd = (34f + (float)rnd.NextDouble() * 62f) * (0.55f + z * 0.7f) * velScale;
            b.BaseSpd = b.Spd;
            b.Turn = 0.10f + (float)rnd.NextDouble() * 0.20f;

            b.Phase = (float)rnd.NextDouble() * 6.283f;

            int hueOff = rnd.Next(0, HUE_N);
            for (int p = 0; p < 4; p++)
                b.Ph[p] = MakeSprite(b.R, hueOff, (p & 1) * 0.5f, (p >> 1) * 0.5f);
            return b;
        }

        /* ---------- 查找表 ---------- */

        static void BuildTables()
        {
            profA = new float[PROF_N];
            profW = new float[PROF_N];

            for (int i = 0; i < PROF_N; i++)
            {
                float t = i * PROF_MAX / (PROF_N - 1);

                // 边缘那道细而亮的线（细一点才像玻璃，宽了就成了雾带）
                float line = (float)Math.Exp(-Math.Pow((t - 0.963) / 0.024, 2.0));
                // 紧贴内壁的柔和厚度
                float soft = (float)(0.26 * Math.Exp(-Math.Pow((t - 0.925) / 0.045, 2.0)));
                // 外侧极淡的光晕
                float halo = (float)(0.11 * Math.Exp(-Math.Pow((t - 1.014) / 0.030, 2.0)));

                float a = line + soft + halo;
                if (a > 1f) a = 1f;
                profA[i] = a;

                // 越靠近亮线越接近纯白，玻璃感来源
                float wm = (float)Math.Exp(-Math.Pow((t - 0.963) / 0.015, 2.0));
                profW[i] = wm;
            }

            hueR = new byte[HUE_N];
            hueG = new byte[HUE_N];
            hueB = new byte[HUE_N];
            angLight = new float[HUE_N];

            for (int i = 0; i < HUE_N; i++)
            {
                double ang = i * (2.0 * Math.PI / HUE_N);
                double deg = ang * 180.0 / Math.PI;
                double hue = (deg + 360.0) % 360.0;

                // 饱和度提高了一点：亮线不再被白洗掉，浅色桌面上也还能看出颜色
                Color c = Hsl(hue, 0.74, 0.76, 255);
                hueR[i] = c.R;
                hueG[i] = c.G;
                hueB[i] = c.B;

                // 左上受光、右下偏暗，再叠一层彩虹不均
                double m = 0.54 + 0.26 * Math.Cos(ang + 2.35) + 0.20 * Math.Sin(2.3 * ang + 0.8);
                if (m < 0.14) m = 0.14;
                if (m > 1.0) m = 1.0;
                angLight[i] = (float)m;
            }
        }

        /* ---------- 逐像素生成气泡精灵 ---------- */
        // 环 + 高光一次性烘焙进同一张预乘 alpha 的 DIB，
        // 之后每帧只需要一次 GDI AlphaBlend，不再有重采样。

        Sprite MakeSprite(float radius, int hueOff, float fx, float fy)
        {
            int size = (int)Math.Ceiling(radius * 2f * 1.14f) + 8;
            if (size < 28) size = 28;

            byte[] buf = new byte[size * size * 4];
            float cx = (size - 1) * 0.5f;
            float invR = 1f / radius;

            // 1) 虹彩环 + 紧贴内壁的第二道薄膜干涉
            for (int y = 0; y < size; y++)
            {
                float sy = y + fy - cx;
                int row = y * size * 4;

                for (int x = 0; x < size; x++)
                {
                    float sx = x + fx - cx;
                    float t = (float)Math.Sqrt(sx * sx + sy * sy) * invR;
                    if (t > PROF_MAX) continue;

                    double ang = Math.Atan2(sy, sx);
                    int ai = (int)((ang + Math.PI) * HUE_N / (2.0 * Math.PI));
                    if (ai < 0) ai = 0;
                    if (ai >= HUE_N) ai = HUE_N - 1;

                    int o = row + x * 4;

                    // 1a) 内侧第二道干涉：薄膜厚度不均产生的另一圈淡彩
                    float bd = 0.050f * (float)Math.Exp(-Math.Pow((t - 0.893) / 0.027, 2.0));
                    if (bd >= 0.0012f)
                    {
                        float ba = bd * angLight[ai];
                        if (ba > 1f) ba = 1f;
                        int bi = ai + hueOff + 330;
                        if (bi >= HUE_N) bi -= HUE_N;
                        if (bi >= HUE_N) bi -= HUE_N;
                        Over(buf, o,
                             hueR[bi] * 0.55f + 255f * 0.45f,
                             hueG[bi] * 0.55f + 255f * 0.45f,
                             hueB[bi] * 0.55f + 255f * 0.45f, ba);
                    }

                    // 1b) 主环
                    int ti = (int)(t * (PROF_N - 1) / PROF_MAX);
                    if (ti < 0) ti = 0;
                    if (ti > PROF_N - 1) ti = PROF_N - 1;

                    float ap = profA[ti];
                    if (ap < 0.0025f) continue;

                    float a = ap * angLight[ai];
                    if (a <= 0f) continue;
                    if (a > 1f) a = 1f;

                    int hi = ai + hueOff;
                    if (hi >= HUE_N) hi -= HUE_N;

                    float wm = profW[ti] * 0.70f;
                    Over(buf, o,
                         hueR[hi] + (255 - hueR[hi]) * wm,
                         hueG[hi] + (255 - hueG[hi]) * wm,
                         hueB[hi] + (255 - hueB[hi]) * wm, a);
                }
            }

            // 2) 高光：全是羽化光晕，没有硬边、没有孤立亮点
            //    大范围那层压得很淡，是为了不在深色桌面上留下看得见形状的灰团
            Glow(buf, size, radius, cx, fx, fy, -0.30f, -0.42f, 0.56f, 0.42f, 9f);
            Glow(buf, size, radius, cx, fx, fy, -0.41f, -0.45f, 0.25f, 0.19f, 58f);
            Glow(buf, size, radius, cx, fx, fy, 0.28f, 0.42f, 0.32f, 0.22f, 14f);

            return MakeDibSprite(size, buf);
        }

        /// <summary>
        /// 羽化光晕：alpha 用 (1-d²)²·e^-1.8d² 衰减，边界严格归零，
        /// 所以看不到圆形的硬边——这是替代"小亮点"的关键。
        /// </summary>
        static void Glow(byte[] buf, int size, float radius, float cx, float fx, float fy,
                         float ox, float oy, float rx, float ry, float peak)
        {
            float dcx = cx + ox * radius - fx;
            float dcy = cx + oy * radius - fy;
            float ax = rx * radius;
            float ay = ry * radius;
            if (ax < 0.6f || ay < 0.6f) return;

            int x0 = (int)Math.Floor(dcx - ax), x1 = (int)Math.Ceiling(dcx + ax);
            int y0 = (int)Math.Floor(dcy - ay), y1 = (int)Math.Ceiling(dcy + ay);
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > size - 1) x1 = size - 1;
            if (y1 > size - 1) y1 = size - 1;

            float k = peak / 255f;

            for (int y = y0; y <= y1; y++)
            {
                float ny = (y - dcy) / ay;
                float ny2 = ny * ny;
                if (ny2 >= 1f) continue;
                int row = y * size * 4;

                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x - dcx) / ax;
                    float d2 = nx * nx + ny2;
                    if (d2 >= 1f) continue;

                    float w = 1f - d2;
                    float a = k * w * w * (float)Math.Exp(-1.8 * d2);
                    if (a <= 0.0004f) continue;
                    Over(buf, row + x * 4, 255f, 255f, 255f, a);
                }
            }
        }

        /// <summary>把源色以 alpha 叠加到预乘缓冲上（标准 source-over）</summary>
        static void Over(byte[] b, int o, float r, float g, float bl, float a)
        {
            if (a <= 0f) return;
            if (a > 1f) a = 1f;
            float ia = 1f - a;

            float da = a + b[o + 3] * (1f / 255f) * ia;
            float dr = bl * a + b[o + 0] * ia;
            float dg = g * a + b[o + 1] * ia;
            float db = r * a + b[o + 2] * ia;

            if (da > 1f) da = 1f;
            if (dr > 255f) dr = 255f;
            if (dg > 255f) dg = 255f;
            if (db > 255f) db = 255f;

            b[o + 0] = (byte)(dr + 0.5f);
            b[o + 1] = (byte)(dg + 0.5f);
            b[o + 2] = (byte)(db + 0.5f);
            b[o + 3] = (byte)(da * 255f + 0.5f);
        }

        Sprite MakeDibSprite(int size, byte[] buf)
        {
            Nat.BITMAPINFO bi = new Nat.BITMAPINFO();
            bi.bmiHeader.biSize = Marshal.SizeOf(typeof(Nat.BITMAPINFOHEADER));
            bi.bmiHeader.biWidth = size;
            bi.bmiHeader.biHeight = -size;
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = 0;
            bi.bmiHeader.biSizeImage = size * size * 4;

            IntPtr bits;
            IntPtr hbm = Nat.CreateDIBSection(screenDc, ref bi, 0, out bits, IntPtr.Zero, 0);
            Marshal.Copy(buf, 0, bits, buf.Length);

            Sprite s = new Sprite();
            s.Size = size;
            s.Bmp = hbm;
            return s;
        }

        public static Color Hsl(double h, double s, double l, int a)
        {
            h = (((h % 360.0) + 360.0) % 360.0) / 360.0;
            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            double rd = Hue2(p, q, h + 1.0 / 3.0);
            double gd = Hue2(p, q, h);
            double bd = Hue2(p, q, h - 1.0 / 3.0);
            int r = (int)Math.Round(rd * 255.0);
            int gc = (int)Math.Round(gd * 255.0);
            int bc = (int)Math.Round(bd * 255.0);
            if (r < 0) r = 0; if (r > 255) r = 255;
            if (gc < 0) gc = 0; if (gc > 255) gc = 255;
            if (bc < 0) bc = 0; if (bc > 255) bc = 255;
            return Color.FromArgb(a, r, gc, bc);
        }

        static double Hue2(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        /* ---------- 每帧 ---------- */

        bool Step(float s)
        {
            Nat.POINT cur;
            Nat.GetCursorPos(out cur);

            // 前 0.8 秒不判输入：窗口刚弹出时，用户的手很可能还在键鼠上
            if (!armed)
            {
                if (tsec > 0.8f)
                {
                    armed = true;
                    armPt = cur;
                    for (int i = 1; i < 255; i++)
                        keyBase[i] = (Nat.GetAsyncKeyState(i) & 0x8000) != 0;
                }
            }
            else
            {
                int ddx = cur.X - armPt.X;
                int ddy = cur.Y - armPt.Y;
                if (ddx * ddx + ddy * ddy > 100) { Close(); return false; }   // 鼠标移动超过 10px

                for (int i = 1; i < 255; i++)
                {
                    if (i == 0x5B || i == 0x5C) continue;                     // 忽略 Win 键
                    bool down = (Nat.GetAsyncKeyState(i) & 0x8000) != 0;
                    if (down && !keyBase[i]) { Close(); return false; }
                }
            }

            float sec = s * 0.016667f;              // 本帧真实时长（秒）
            float spd = speedPct * 0.01f;           // 速度倍率

            for (int i = 0; i < list.Count; i++)
            {
                Bubble b = list[i];

                // 方向缓慢游走：对时间做正弦积分，曲线连续，不会有突变
                b.Dir += (float)Math.Sin(tsec * b.Turn + b.Phase) * b.Turn * 0.45f * sec;

                // 速度本身也有极慢的起伏，避免所有气泡像匀速机器
                float sp = b.Spd * (1f + 0.12f * (float)Math.Sin(tsec * 0.7f + b.Phase * 1.7f));

                b.X += (float)Math.Cos(b.Dir) * sp * sec * spd;
                b.Y += (float)Math.Sin(b.Dir) * sp * sec * spd;

                // 碰到边界就镜像方向分量，速度大小不变
                float pad = b.R * 0.55f;
                if (b.X < pad) { b.X = pad; b.Dir = (float)Math.PI - b.Dir; }
                else if (b.X > w - pad) { b.X = w - pad; b.Dir = (float)Math.PI - b.Dir; }
                if (b.Y < pad) { b.Y = pad; b.Dir = -b.Dir; }
                else if (b.Y > h - pad) { b.Y = h - pad; b.Dir = -b.Dir; }

                // 碰撞会让速度在气泡之间转移，极慢地拉回基准值，防止全体趋同
                b.Spd += (b.BaseSpd - b.Spd) * 0.0015f * s;
            }

            ResolveCollisions();

            // 碰撞可能把气泡推出画面，最后统一夹回来
            for (int i = 0; i < list.Count; i++)
            {
                Bubble b = list[i];
                float pad = b.R * 0.55f;
                if (b.X < pad) b.X = pad;
                else if (b.X > w - pad) b.X = w - pad;
                if (b.Y < pad) b.Y = pad;
                else if (b.Y > h - pad) b.Y = h - pad;
            }
            return true;
        }

        /// <summary>
        /// 气泡之间的碰撞：等质量弹性碰撞的推广——按"半径当质量"分配冲量，
        /// 所以小气泡会被大气泡弹开得更明显，不会互相穿过去。
        /// </summary>
        void ResolveCollisions()
        {
            const float REST = 0.98f;      // 弹性系数：几乎不损失能量，否则会重现"越飘越慢"
            const float SKIN = 0.94f;      // 视觉半径边缘是渐隐的，用 0.94 才不显得隔着缝就撞上

            // 两轮：一堆气泡同时挤在一起时，一轮推不干净
            for (int pass = 0; pass < 2; pass++)
            {
                int n = list.Count;
                for (int i = 0; i < n; i++)
                {
                    Bubble a = list[i];
                    for (int j = i + 1; j < n; j++)
                    {
                        Bubble b = list[j];

                        float dx = b.X - a.X;
                        float dy = b.Y - a.Y;
                        float reach = (a.R + b.R) * SKIN;
                        float d2 = dx * dx + dy * dy;
                        if (d2 >= reach * reach) continue;

                        float nx, ny, d;
                        if (d2 < 0.0001f) { d = 0.01f; nx = 1f; ny = 0f; }
                        else { d = (float)Math.Sqrt(d2); nx = dx / d; ny = dy / d; }

                        float ma = a.R, mb = b.R;
                        float mt = ma + mb;

                        // 1) 先把重叠推开：大的推得少、小的推得多
                        float over = reach - d + 0.05f;
                        float wa = mb / mt;
                        float wb = ma / mt;
                        a.X -= nx * over * wa; a.Y -= ny * over * wa;
                        b.X += nx * over * wb; b.Y += ny * over * wb;

                        // 2) 法向速度交换
                        float avx = (float)Math.Cos(a.Dir) * a.Spd;
                        float avy = (float)Math.Sin(a.Dir) * a.Spd;
                        float bvx = (float)Math.Cos(b.Dir) * b.Spd;
                        float bvy = (float)Math.Sin(b.Dir) * b.Spd;

                        float rel = (bvx - avx) * nx + (bvy - avy) * ny;
                        if (rel >= 0f) continue;          // 已经在分开了，不再加冲量

                        float jimp = -(1f + REST) * rel / (1f / ma + 1f / mb);
                        avx -= (jimp / ma) * nx; avy -= (jimp / ma) * ny;
                        bvx += (jimp / mb) * nx; bvy += (jimp / mb) * ny;

                        a.Spd = (float)Math.Sqrt(avx * avx + avy * avy);
                        a.Dir = (float)Math.Atan2(avy, avx);
                        b.Spd = (float)Math.Sqrt(bvx * bvx + bvy * bvy);
                        b.Dir = (float)Math.Atan2(bvy, bvx);

                        // 围着各自的基准速度波动，不设绝对值上下限，避免大量碰撞后整体变慢
                        if (a.Spd < a.BaseSpd * 0.55f) a.Spd = a.BaseSpd * 0.55f;
                        else if (a.Spd > a.BaseSpd * 2.2f) a.Spd = a.BaseSpd * 2.2f;
                        if (b.Spd < b.BaseSpd * 0.55f) b.Spd = b.BaseSpd * 0.55f;
                        else if (b.Spd > b.BaseSpd * 2.2f) b.Spd = b.BaseSpd * 2.2f;
                    }
                }
            }
        }

        void PaintScene()
        {
            // 一帧开始时把画布清成全透明（BLACKNESS 会把 alpha 也置 0）
            Nat.PatBlt(memDc, 0, 0, w, h, 0x00000042);

            for (int i = 0; i < list.Count; i++)
            {
                Bubble b = list[i];

                // 小数位置决定用哪个亚像素相位，画的时候只给整数坐标
                float cx0 = (b.Ph[0].Size - 1) * 0.5f;
                float ux = b.X - cx0;
                float uy = b.Y - cx0;
                float frx = ux - (float)Math.Floor(ux);
                float fry = uy - (float)Math.Floor(uy);

                int px = (frx >= 0.25f && frx < 0.75f) ? 1 : 0;
                int py = (fry >= 0.25f && fry < 0.75f) ? 1 : 0;

                Sprite sp = b.Ph[(py << 1) | px];
                int dx = (int)Math.Round(ux + px * 0.5f);
                int dy = (int)Math.Round(uy + py * 0.5f);

                Nat.SelectObject(srcDc, sp.Bmp);
                Nat.AlphaBlend(memDc, dx, dy, sp.Size, sp.Size,
                               srcDc, 0, 0, sp.Size, sp.Size, blend);
            }
        }

        void Present()
        {
            // 画面已经在分层窗口自己的 DIB 里了，这里只需要提交一次
            Nat.POINT dst = new Nat.POINT(Left, Top);
            Nat.SIZE sz = new Nat.SIZE(w, h);
            Nat.POINT src = new Nat.POINT(0, 0);
            Nat.BLENDFUNCTION bf = new Nat.BLENDFUNCTION();
            bf.BlendOp = 0;
            bf.BlendFlags = 0;
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = AC_SRC_ALPHA;

            bool ok = Nat.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, ULW_ALPHA);
            if (frameIdx < 3)
                Dbg.W("ULW frame" + frameIdx + " ok=" + ok + " err=" + Marshal.GetLastWin32Error()
                      + " dst=" + dst.X + "," + dst.Y + " size=" + sz.cx + "x" + sz.cy
                      + " blend=" + bf.BlendOp + "/" + bf.BlendFlags + "/" + bf.SourceConstantAlpha + "/" + bf.AlphaFormat
                      + " flags=" + ULW_ALPHA);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                quitLoop = true;
                for (int i = 0; i < list.Count; i++)
                {
                    Sprite[] ph = list[i].Ph;
                    for (int p = 0; p < ph.Length; p++)
                    {
                        if (ph[p] != null && ph[p].Bmp != IntPtr.Zero) Nat.DeleteObject(ph[p].Bmp);
                    }
                }
                if (oldDib != IntPtr.Zero) Nat.SelectObject(memDc, oldDib);
                if (dib != IntPtr.Zero) Nat.DeleteObject(dib);
                if (srcDc != IntPtr.Zero) Nat.DeleteDC(srcDc);
                if (memDc != IntPtr.Zero) Nat.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) Nat.ReleaseDC(IntPtr.Zero, screenDc);
                Nat.timeEndPeriod(1);
            }
            catch { }
            base.OnFormClosed(e);
        }
    }

    public class SettingsForm : Form
    {
        NumericUpDown nb, nf, ns, nidle;

        public SettingsForm()
        {
            Text = "Bubble Screen - Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(336, 256);
            try { Font = new Font("Microsoft YaHei", 9f); } catch { }

            Label l1 = new Label();
            l1.Text = "气泡数量（0 = 按屏幕自动）";
            l1.SetBounds(24, 24, 200, 20);
            Controls.Add(l1);

            nb = new NumericUpDown();
            nb.Minimum = 0;
            nb.Maximum = 60;
            int cur = Settings.Bubbles;
            if (cur < 0) cur = 0;
            if (cur > 60) cur = 60;
            nb.Value = cur;
            nb.SetBounds(236, 22, 72, 24);
            Controls.Add(nb);

            Label l2 = new Label();
            l2.Text = "帧率（越高越顺，越费电）";
            l2.SetBounds(24, 62, 200, 20);
            Controls.Add(l2);

            nf = new NumericUpDown();
            nf.Minimum = 12;
            nf.Maximum = 60;
            nf.Value = Settings.Fps;
            nf.SetBounds(236, 60, 72, 24);
            Controls.Add(nf);

            Label l4 = new Label();
            l4.Text = "漂浮速度（%，100 为基准）";
            l4.SetBounds(24, 100, 200, 20);
            Controls.Add(l4);

            ns = new NumericUpDown();
            ns.Minimum = 30;
            ns.Maximum = 300;
            ns.Increment = 10;
            ns.Value = Settings.Speed;
            ns.SetBounds(236, 98, 72, 24);
            Controls.Add(ns);

            Label l3 = new Label();
            l3.Text = "托盘图标右键「立即显示气泡」即可查看效果。";
            l3.ForeColor = Color.Gray;
            l3.SetBounds(24, 172, 292, 20);
            Controls.Add(l3);

            Label l5 = new Label();
            l5.Text = "闲置多久后自动显示（分钟，0 = 关）";
            l5.SetBounds(24, 138, 210, 20);
            Controls.Add(l5);

            nidle = new NumericUpDown();
            nidle.Minimum = 0;
            nidle.Maximum = 120;
            nidle.Value = Settings.IdleMinutes;
            nidle.SetBounds(236, 136, 72, 24);
            Controls.Add(nidle);

            Button ok = new Button();
            ok.Text = "保存";
            ok.SetBounds(140, 208, 80, 28);
            ok.Click += OnOk;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(232, 208, 80, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        void OnOk(object sender, EventArgs e)
        {
            Settings.Bubbles = (int)nb.Value;
            Settings.Fps = (int)nf.Value;
            Settings.Speed = (int)ns.Value;
            Settings.IdleMinutes = (int)nidle.Value;
            Close();
        }
    }

    /// <summary>
    /// 常驻托盘 —— 气泡浮在当前界面上、背后是实时桌面的唯一正确姿势。
    ///
    /// 不走屏保机制：被系统屏保拉起时，Windows 不再把桌面内容合成给你，
    /// 分层窗口的透明区域会退化成一整片黑（Bubbles.scr 从 Win8 起黑底同一原因）。
    /// 托盘模式是普通窗口，DWM 正常合成，透明区域就是实时桌面。
    /// </summary>
    public class TrayHost : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "BubbleGlass";

        NotifyIcon ni;
        System.Windows.Forms.Timer watch;
        Overlay ov;
        ToolStripMenuItem miAuto, miStartup;
        DateTime lastStart = DateTime.MinValue;
        // 退出后必须先见到一次真实输入，才允许再次自动开始，
        // 否则「闲置时间还很长」会立刻又触发一遍，变成死循环
        bool needInput;
        bool quitting;

        public TrayHost()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            try { menu.Font = new Font("Microsoft YaHei", 9f); } catch { }

            ToolStripMenuItem miNow = new ToolStripMenuItem("立即显示气泡");
            try { miNow.Font = new Font(menu.Font, FontStyle.Bold); } catch { }
            miNow.Click += delegate { StartOverlay(); };
            menu.Items.Add(miNow);

            ToolStripMenuItem miCfg = new ToolStripMenuItem("设置…");
            miCfg.Click += delegate
            {
                using (SettingsForm f = new SettingsForm()) { f.ShowDialog(); }
                RefreshMenuText();
            };
            menu.Items.Add(miCfg);

            menu.Items.Add(new ToolStripSeparator());

            miAuto = new ToolStripMenuItem("闲置后自动显示");
            miAuto.Click += delegate
            {
                Settings.IdleMinutes = Settings.IdleMinutes > 0 ? 0 : 5;
                RefreshMenuText();
            };
            menu.Items.Add(miAuto);

            miStartup = new ToolStripMenuItem("开机自动启动");
            miStartup.Click += delegate
            {
                SetStartup(!IsStartup());
                RefreshMenuText();
            };
            menu.Items.Add(miStartup);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { Quit(); };
            menu.Items.Add(miExit);

            ni = new NotifyIcon();
            ni.Icon = MakeIcon();
            ni.Text = "七彩气泡 · 浮在桌面上，可透出当前界面";
            ni.ContextMenuStrip = menu;
            ni.Visible = true;
            ni.DoubleClick += delegate { if (ov == null) StartOverlay(); };

            RefreshMenuText();

            watch = new System.Windows.Forms.Timer();
            watch.Interval = 1000;
            watch.Tick += OnWatch;
            watch.Start();

            Dbg.W("tray start  idleMin=" + Settings.IdleMinutes + "  startup=" + IsStartup());

            // 消息循环起来之后再提示，否则气球提示可能来不及显示
            System.Windows.Forms.Timer hello = new System.Windows.Forms.Timer();
            hello.Interval = 700;
            hello.Tick += delegate
            {
                hello.Stop();
                hello.Dispose();
                try
                {
                    ni.ShowBalloonTip(4000, "七彩气泡已就绪",
                        "双击托盘图标看效果：气泡浮在桌面上，能透出当前界面。鼠标一动就退出。",
                        ToolTipIcon.None);
                }
                catch { }
            };
            hello.Start();
        }

        /* ---------- 托盘图标 ---------- */

        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    for (int i = 0; i < 12; i++)
                    {
                        double a0 = i * 30.0;
                        Color c = Overlay.Hsl(a0, 0.78, 0.62, 255);
                        using (Pen p = new Pen(c, 3.2f))
                        {
                            p.StartCap = LineCap.Round;
                            p.EndCap = LineCap.Round;
                            g.DrawArc(p, 6f, 6f, 20f, 20f, (float)a0, 34f);
                        }
                    }
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(238, 255, 255, 255)))
                        g.FillEllipse(b, 9.5f, 7.5f, 5.0f, 4.2f);
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { Nat.DestroyIcon(h); }
            }
        }

        /* ---------- 菜单状态 ---------- */

        void RefreshMenuText()
        {
            int m = Settings.IdleMinutes;
            miAuto.Checked = m > 0;
            miAuto.Text = m > 0 ? ("闲置 " + m + " 分钟后自动显示") : "闲置后自动显示（关闭）";
            miStartup.Checked = IsStartup();
        }

        static bool IsStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return k != null && k.GetValue(RunName) != null;
                }
            }
            catch { return false; }
        }

        static void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(RunName, false);
                }
                Dbg.W("startup=" + on);
            }
            catch (Exception ex) { Dbg.W("startup set failed: " + ex.Message); }
        }

        /* ---------- 闲置监视 ---------- */

        void OnWatch(object sender, EventArgs e)
        {
            if (ov != null) return;
            int m = Settings.IdleMinutes;
            if (m <= 0) return;

            uint idle = Nat.IdleMs();

            if (needInput)
            {
                if (idle < 2000u) needInput = false;
                return;
            }

            if ((DateTime.Now - lastStart).TotalSeconds < 3) return;

            if (idle >= (uint)m * 60000u)
            {
                Dbg.W("idle " + idle + "ms >= " + m + "min -> start");
                StartOverlay();
            }
        }

        void StartOverlay()
        {
            if (ov != null) return;
            try { watch.Stop(); } catch { }
            try
            {
                ov = new Overlay(Settings.Bubbles, Settings.Fps);
                ov.FormClosed += delegate
                {
                    ov = null;
                    lastStart = DateTime.Now;
                    needInput = true;
                    if (!quitting) { try { watch.Start(); } catch { } }
                    Dbg.W("overlay closed -> back to tray");
                };
                ov.Show();
            }
            catch (Exception ex)
            {
                Dbg.W("start overlay failed: " + ex);
                ov = null;
                try { watch.Start(); } catch { }
            }
        }

        void Quit()
        {
            quitting = true;
            try { if (watch != null) watch.Stop(); } catch { }
            try
            {
                if (ni != null) { ni.Visible = false; ni.Dispose(); ni = null; }
            }
            catch { }
            if (ov != null) { try { ov.Close(); } catch { } ov = null; }
            Dbg.W("tray quit");
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { if (watch != null) { watch.Stop(); watch.Dispose(); } } catch { }
                try { if (ni != null) { ni.Visible = false; ni.Dispose(); } } catch { }
            }
            base.Dispose(disposing);
        }
    }

    static class Program
    {
        /// <summary>
        /// 无参数 → 常驻托盘，气泡浮在桌面上。
        /// /x     → 不开窗口，只渲染一帧存成 PNG，用来离线检查绘制结果。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Dbg.Reset();
            string a0 = args != null && args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
            Dbg.W("start  args=[" + a0 + "]");

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (a0 == "/x" || a0 == "-x" || a0 == "--dump")
                {
                    using (Overlay ov = new Overlay(Settings.Bubbles, Settings.Fps))
                        ov.DumpOneFrame();
                }
                else
                {
                    Application.Run(new TrayHost());
                }
            }
            catch (Exception ex)
            {
                Dbg.W("FATAL: " + ex);
            }
            Dbg.W("exit");
        }
    }
}
