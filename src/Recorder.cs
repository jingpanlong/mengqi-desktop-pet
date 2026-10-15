using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Mengqi
{
    /// <summary>把屏幕的一块区域按帧捕获，编码成 H.264 / MP4 视频（不再是 GIF）。
    /// 有 ffmpeg 就用 ffmpeg，没有就用 Windows 自带的 Media Foundation；区域超过 1920px 时等比缩小以兼顾清晰度与体积；支持暂停/恢复。</summary>
    internal sealed class ScreenRecorder : IDisposable
    {
        private readonly Rectangle region;
        private readonly IVideoSink encoder;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly int scale;
        private long lastFrame;
        private int frames;
        private bool stopped;
        private bool paused;

        private ScreenRecorder(Rectangle region, string path, int fps)
        {
            this.region = region;
            const int MaxDim = 1920;
            int maxDim = Math.Max(region.Width, region.Height);
            scale = maxDim > MaxDim ? (int)Math.Ceiling(maxDim / (double)MaxDim) : 1;
            int width = region.Width / scale;
            int height = region.Height / scale;
            if ((width & 1) == 1) width++;
            if ((height & 1) == 1) height++;
            encoder = VideoSinkFactory.Start(width, height, fps, path);
            timer = new System.Windows.Forms.Timer();
            timer.Interval = Math.Max(60, 1000 / Math.Max(1, fps));
            timer.Tick += Capture;
            lastFrame = watch.ElapsedMilliseconds;
        }

        internal static ScreenRecorder Start(Rectangle region, string path, int fps)
        {
            ScreenRecorder recorder = new ScreenRecorder(region, path, fps);
            recorder.timer.Start();
            return recorder;
        }

        internal int Frames { get { return frames; } }

        /// <summary>暂停/恢复抓取；暂停期间计时与写帧都停，恢复后自动接续，不会漏帧也不会把停顿录进去。</summary>
        internal bool Paused
        {
            get { return paused; }
            set { paused = value; if (!value) lastFrame = watch.ElapsedMilliseconds; }
        }

        private void Capture(object sender, EventArgs e)
        {
            if (stopped || paused) return;
            using (Bitmap frame = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
                }
                encoder.AddFrame(frame);
            }
            frames++;
        }

        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            timer.Stop(); timer.Dispose();
            encoder.Dispose();       // 写入 moov / 收尾，生成完整 MP4
        }

        public void Dispose() { Stop(); }
    }

    /// <summary>半透明遮罩上拖一个框；框内完全透明，能看到真实画面。
    /// 高级版：暗化遮罩 + 三分构图线 + 八向缩放手柄 + 跟随光标的放大镜（带十字与色值）+ 实时尺寸/坐标读数；
    /// 画完后可拖动/缩放微调，Enter 或双击或点「完成」确认，Esc / 右键 / 「取消」放弃。</summary>
    internal sealed class RegionSelectOverlay : Form
    {
        private readonly Action<Rectangle> picked;
        private readonly Action cancelled;
        private Point start;
        private Rectangle selection;
        private Point mouse;
        private bool dragging;
        private int phase;                 // 0=尚未成框，1=已成框可微调
        private string dragMode;           // New / Move / ResizeNW ...
        private readonly int handle = 9;   // 手柄半边长
        private readonly int tolerance = 9;
        private readonly int loupeR = 54;
        private readonly int loupeSrc = 24;
        private readonly Font badgeFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        private readonly Font tipFont = new Font("Microsoft YaHei UI", 9, FontStyle.Regular);
        private readonly Button okBtn, cancelBtn;

        internal RegionSelectOverlay(Action<Rectangle> onPicked, Action onCancelled)
        {
            picked = onPicked; cancelled = onCancelled;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = SystemInformation.VirtualScreen;
            StartPosition = FormStartPosition.Manual;
            TopMost = true; ShowInTaskbar = false; KeyPreview = true; DoubleBuffered = true;
            BackColor = Color.FromArgb(20, 14, 34);
            Opacity = 0.42;
            Cursor = Cursors.Cross;
            Text = "萌栖 · 拖框选择（Esc 取消，Enter 完成）";
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Cancel();
                else if (e.KeyCode == Keys.Enter && phase == 1) Confirm();
            };
            MouseDown += OnDown; MouseMove += OnMove; MouseUp += OnUp; MouseDoubleClick += OnDoubleClick;
            // 底部操作条（落在暗化区，始终可见）
            okBtn = MakeButton("完成 ⏎", Color.FromArgb(124, 92, 196), Color.White, delegate { if (phase == 1) Confirm(); });
            cancelBtn = MakeButton("取消 Esc", Color.White, Color.FromArgb(124, 92, 196), Cancel);
            int barY = Height - 64;
            cancelBtn.Location = new Point(Width - 220, barY);
            okBtn.Location = new Point(Width - 130, barY);
            Controls.Add(cancelBtn); Controls.Add(okBtn);
            mouse = new Point(Width / 2, Height / 2);
        }

        private Button MakeButton(string text, Color back, Color fore, Action action)
        {
            Button b = new Button
            {
                Text = text,
                Size = new Size(94, 38),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
                BackColor = back,
                ForeColor = fore,
                Cursor = Cursors.Default
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += delegate { try { action(); } catch { } };
            return b;
        }

        private void Cancel()
        {
            if (IsDisposed) return;
            Close();
            if (cancelled != null) cancelled();
        }

        private void Confirm()
        {
            if (IsDisposed) return;
            Rectangle chosen = selection.Width >= 16 && selection.Height >= 16 ? RectangleToScreen(selection) : Rectangle.Empty;
            Close();
            if (chosen.Width < 16 || chosen.Height < 16) { if (cancelled != null) cancelled(); return; }
            if (picked != null) picked(chosen);
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Cancel(); return; }
            if (e.Button != MouseButtons.Left) return;
            Point p = PointToClient(Cursor.Position);
            string handleName = phase == 1 ? HitHandle(p) : null;
            if (handleName != null) { dragMode = "Resize" + handleName; dragging = true; Capture = true; return; }
            if (phase == 1 && selection.Contains(p)) { dragMode = "Move"; dragging = true; Capture = true; start = p; return; }
            // 重新画一个框
            dragging = true; dragMode = "New"; Capture = true;
            start = p; selection = Rectangle.Empty; phase = 0;
            UpdateRegion(); Invalidate();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            Point p = PointToClient(Cursor.Position);
            mouse = p;
            if (!dragging) { Invalidate(); return; }
            if (dragMode == "New")
            {
                selection = Normalize(start, p);
            }
            else if (dragMode == "Move")
            {
                int dx = p.X - start.X, dy = p.Y - start.Y;
                selection.Location = ClampPoint(new Point(selection.X + dx, selection.Y + dy), selection.Size);
                start = p;
            }
            else if (dragMode.StartsWith("Resize"))
            {
                ResizeSelection(dragMode.Substring(6), p);
            }
            UpdateRegion(); Invalidate();
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false; Capture = false;
            if (dragMode == "New" && selection.Width >= 16 && selection.Height >= 16) phase = 1;
            else if (dragMode == "New") { selection = Rectangle.Empty; phase = 0; }
            UpdateRegion(); Invalidate();
        }

        private void OnDoubleClick(object sender, MouseEventArgs e)
        {
            if (phase == 1 && selection.Width >= 16 && selection.Height >= 16) Confirm();
        }

        private void ResizeSelection(string handleName, Point p)
        {
            Rectangle r = selection;
            int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            bool n = handleName.Contains("N"), s = handleName.Contains("S"), w = handleName.Contains("W"), e2 = handleName.Contains("E");
            if (n) top = Math.Min(p.Y, bottom - 16);
            if (s) bottom = Math.Max(p.Y, top + 16);
            if (w) left = Math.Min(p.X, right - 16);
            if (e2) right = Math.Max(p.X, left + 16);
            selection = ClampRect(Rectangle.FromLTRB(left, top, right, bottom));
        }

        private string HitHandle(Point p)
        {
            int l = selection.Left, t = selection.Top, r = selection.Right, b = selection.Bottom;
            int cx = (l + r) / 2, cy = (t + b) / 2;
            if (Near(p, l, t)) return "NW";
            if (Near(p, r, t)) return "NE";
            if (Near(p, l, b)) return "SW";
            if (Near(p, r, b)) return "SE";
            if (Near(p, cx, t)) return "N";
            if (Near(p, cx, b)) return "S";
            if (Near(p, l, cy)) return "W";
            if (Near(p, r, cy)) return "E";
            return null;
        }

        private bool Near(Point p, int x, int y)
        {
            return p.X >= x - tolerance && p.X <= x + tolerance && p.Y >= y - tolerance && p.Y <= y + tolerance;
        }

        private Rectangle Normalize(Point a, Point b)
        {
            return ClampRect(Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        }

        private Rectangle ClampRect(Rectangle r)
        {
            Rectangle screen = SystemInformation.VirtualScreen;
            r.X = Math.Max(screen.Left, Math.Min(screen.Right - r.Width, r.X));
            r.Y = Math.Max(screen.Top, Math.Min(screen.Bottom - r.Height, r.Y));
            r.Width = Math.Min(r.Width, screen.Width);
            r.Height = Math.Min(r.Height, screen.Height);
            return r;
        }

        private Point ClampPoint(Point p, Size size)
        {
            Rectangle screen = SystemInformation.VirtualScreen;
            p.X = Math.Max(screen.Left, Math.Min(screen.Right - size.Width, p.X));
            p.Y = Math.Max(screen.Top, Math.Min(screen.Bottom - size.Height, p.Y));
            return p;
        }

        private void UpdateRegion()
        {
            Region region = new Region(new Rectangle(0, 0, Width, Height));
            if (phase == 1 && selection.Width > 2 && selection.Height > 2) region.Exclude(selection);
            else if (dragging && selection.Width > 2 && selection.Height > 2) region.Exclude(selection);
            Region = region;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            if (selection.Width > 0 && selection.Height > 0)
            {
                // 三分构图线
                using (Pen third = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                {
                    int l = selection.Left, t = selection.Top, r = selection.Right, b = selection.Bottom;
                    g.DrawLine(third, l + (r - l) / 3, t, l + (r - l) / 3, b);
                    g.DrawLine(third, l + 2 * (r - l) / 3, t, l + 2 * (r - l) / 3, b);
                    g.DrawLine(third, l, t + (b - t) / 3, r, t + (b - t) / 3);
                    g.DrawLine(third, l, t + 2 * (b - t) / 3, r, t + 2 * (b - t) / 3);
                }
                // 选区描边（白 + 内侧紫，立体感）
                using (Pen pen = new Pen(Color.FromArgb(255, 124, 92, 196), 2f))
                    g.DrawRectangle(pen, selection.X - 1, selection.Y - 1, selection.Width + 1, selection.Height + 1);
                using (Pen pen = new Pen(Color.FromArgb(230, 255, 255, 255), 1.5f))
                    g.DrawRectangle(pen, selection);
                // 八向手柄
                if (phase == 1)
                {
                    int l = selection.Left, t = selection.Top, r = selection.Right, b = selection.Bottom;
                    int cx = (l + r) / 2, cy = (t + b) / 2;
                    Point[] handles = { new Point(l, t), new Point(cx, t), new Point(r, t), new Point(l, cy), new Point(r, cy), new Point(l, b), new Point(cx, b), new Point(r, b) };
                    using (SolidBrush fill = new SolidBrush(Color.White))
                    using (Pen border = new Pen(Color.FromArgb(124, 92, 196), 2f))
                        foreach (Point h in handles)
                        {
                            g.FillRectangle(fill, h.X - handle, h.Y - handle, handle * 2, handle * 2);
                            g.DrawRectangle(border, h.X - handle, h.Y - handle, handle * 2, handle * 2);
                        }
                }
                // 尺寸 / 坐标读数徽标
                string size = selection.Width + " × " + selection.Height;
                string loc = "(" + selection.Left + ", " + selection.Top + ")";
                string badge = size + "   " + loc;
                SizeF sz = g.MeasureString(badge, badgeFont);
                Point spot = new Point(selection.Left, selection.Top - (int)sz.Height - 12);
                if (spot.Y < 0) spot.Y = selection.Bottom + 12;
                if (spot.X + sz.Width + 16 > Width) spot.X = Width - (int)sz.Width - 16;
                DrawBadge(g, spot, sz, badge);
            }
            if (mouse.X >= 0 && mouse.Y >= 0) DrawLoupe(g, mouse);
            // 底部提示
            string hint = phase == 1 ? "拖动框边角可微调 · Enter/双击 完成 · Esc 取消" : "按住左键拖一个框；松手后还能微调";
            SizeF hs = g.MeasureString(hint, tipFont);
            Point hspot = new Point(Width / 2 - (int)hs.Width / 2, Height - 18);
            g.DrawString(hint, tipFont, Brushes.White, hspot);
        }

        private void DrawBadge(Graphics g, Point spot, SizeF sz, string text)
        {
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(220, 20, 14, 34)))
            using (SolidBrush fg = new SolidBrush(Color.FromArgb(255, 255, 255)))
            {
                g.FillRectangle(bg, spot.X - 8, spot.Y - 4, sz.Width + 16, sz.Height + 8);
                g.DrawString(text, badgeFont, fg, spot);
            }
        }

        private void DrawLoupe(Graphics g, Point screenPoint)
        {
            Rectangle screen = SystemInformation.VirtualScreen;
            int half = loupeSrc / 2;
            int sx = Math.Max(screen.Left, Math.Min(screen.Right - loupeSrc, screenPoint.X - half));
            int sy = Math.Max(screen.Top, Math.Min(screen.Bottom - loupeSrc, screenPoint.Y - half));
            int cx = screenPoint.X + loupeR + 22, cy = screenPoint.Y + loupeR + 22;
            if (cx + loupeR > screen.Right) cx = screenPoint.X - loupeR - 22;
            if (cy + loupeR > screen.Bottom) cy = screenPoint.Y - loupeR - 22;
            cx = Math.Max(screen.Left + loupeR, Math.Min(screen.Right - loupeR, cx));
            cy = Math.Max(screen.Top + loupeR, Math.Min(screen.Bottom - loupeR, cy));
            using (Bitmap cap = new Bitmap(loupeSrc, loupeSrc, PixelFormat.Format32bppArgb))
            {
                using (Graphics gc = Graphics.FromImage(cap))
                    gc.CopyFromScreen(sx, sy, 0, 0, new Size(loupeSrc, loupeSrc), CopyPixelOperation.SourceCopy);
                using (GraphicsPath clip = new GraphicsPath())
                {
                    clip.AddEllipse(cx - loupeR, cy - loupeR, loupeR * 2, loupeR * 2);
                    g.SetClip(clip);
                    g.DrawImage(cap, cx - loupeR, cy - loupeR, loupeR * 2, loupeR * 2);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(70, 255, 255, 255)), cx - loupeR, cy - loupeR, loupeR * 2, loupeR * 2);  // 抵消暗化遮罩
                    g.ResetClip();
                }
                // 白圈 + 十字准星
                using (Pen ring = new Pen(Color.White, 3f)) g.DrawEllipse(ring, cx - loupeR, cy - loupeR, loupeR * 2, loupeR * 2);
                using (Pen cross = new Pen(Color.FromArgb(180, 255, 255, 255), 1f))
                {
                    g.DrawLine(cross, cx - 8, cy, cx + 8, cy);
                    g.DrawLine(cross, cx, cy - 8, cx, cy + 8);
                }
                // 中心像素色值（按遮罩亮度补偿 1/0.58）
                Color raw = cap.GetPixel(loupeSrc / 2, loupeSrc / 2);
                int rr = Math.Min(255, (int)(raw.R / 0.58)), gg = Math.Min(255, (int)(raw.G / 0.58)), bb = Math.Min(255, (int)(raw.B / 0.58));
                string hex = "#" + rr.ToString("X2") + gg.ToString("X2") + bb.ToString("X2");
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(220, 20, 14, 34)))
                using (SolidBrush fg = new SolidBrush(Color.White))
                {
                    SizeF hs = g.MeasureString(hex, tipFont);
                    g.FillRectangle(bg, cx - hs.Width / 2 - 6, cy + loupeR + 4, hs.Width + 12, hs.Height + 4);
                    g.DrawString(hex, tipFont, fg, cx - hs.Width / 2, cy + loupeR + 6);
                }
            }
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { badgeFont.Dispose(); tipFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>录制前的 3-2-1 倒数，给用户一点准备时间；结束后回调开始真正录制。</summary>
    internal sealed class CountdownOverlay : RoundedForm
    {
        private readonly Action onDone;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 800 };
        private readonly Label number = new Label();
        private int count = 3;
        private bool done;

        internal CountdownOverlay(Rectangle region, Action onDone)
        {
            this.onDone = onDone;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 14, 34);
            CornerRadius = 28;
            Size = new Size(170, 170);
            number.Dock = DockStyle.Fill;
            number.TextAlign = ContentAlignment.MiddleCenter;
            number.Font = new Font("Microsoft YaHei UI", 70, FontStyle.Bold);
            number.ForeColor = Color.FromArgb(255, 214, 96);
            number.Text = "3";
            Controls.Add(number);
            Rectangle area = Screen.FromRectangle(region).WorkingArea;
            Location = new Point(
                Math.Max(area.Left, Math.Min(area.Right - Width, region.Left + region.Width / 2 - Width / 2)),
                Math.Max(area.Top, Math.Min(area.Bottom - Height, region.Top + region.Height / 2 - Height / 2)));
            timer.Tick += Tick;
        }

        internal void Begin() { timer.Start(); Show(); Activate(); }

        private void Tick(object sender, EventArgs e)
        {
            count--;
            if (count > 0) { number.Text = count.ToString(); Pop(); }
            else if (count == 0) { number.Text = "●"; number.ForeColor = Color.FromArgb(232, 64, 72); Pop(); }
            else
            {
                timer.Stop(); timer.Dispose();
                if (!done) { done = true; Close(); if (onDone != null) onDone(); }
            }
        }

        private void Pop()
        {
            // 简单弹一下：放大再回弹，靠两次重绘制造动感
            number.Font = new Font("Microsoft YaHei UI", 84, FontStyle.Bold);
            System.Windows.Forms.Timer settle = new System.Windows.Forms.Timer { Interval = 120 };
            settle.Tick += delegate { settle.Stop(); settle.Dispose(); number.Font = new Font("Microsoft YaHei UI", 70, FontStyle.Bold); };
            settle.Start();
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>录制时挂在选区上方的小条：脉冲红点 + 计时 + 暂停/恢复 + 停止。</summary>
    internal sealed class RecordBar : RoundedForm
    {
        private readonly Action onStop;
        private readonly Action<bool> onPause;
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 120 };
        private readonly Label time = new Label();
        private readonly Label dot = new Label();
        private readonly Button pauseBtn;
        private readonly Button stopBtn;
        private bool finished;
        private bool paused;
        private long pausedAccum;
        private long pauseStart;

        internal RecordBar(Rectangle region, Action onStop, Action<bool> onPause)
        {
            this.onStop = onStop;
            this.onPause = onPause;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(250, 247, 255);
            CornerRadius = 16;
            Size = new Size(252, 56);
            dot.Bounds = new Rectangle(16, 18, 20, 20);
            dot.BackColor = Color.FromArgb(232, 64, 72);
            dot.Text = "";
            time.Bounds = new Rectangle(44, 14, 96, 28);
            time.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
            time.ForeColor = Color.FromArgb(73, 54, 103);
            time.Text = "00:00";
            pauseBtn = Ui.Button("暂停", delegate { TogglePause(); });
            pauseBtn.MinimumSize = new Size(46, 34);
            pauseBtn.Location = new Point(146, 9);
            pauseBtn.Size = new Size(48, 38);
            stopBtn = Ui.Button("停止", delegate { Finish(); });
            stopBtn.MinimumSize = new Size(46, 34);
            stopBtn.Location = new Point(198, 9);
            stopBtn.Size = new Size(48, 38);
            Controls.Add(dot); Controls.Add(time); Controls.Add(pauseBtn); Controls.Add(stopBtn);
            timer.Tick += delegate
            {
                long visible = watch.ElapsedMilliseconds - pausedAccum;
                TimeSpan span = TimeSpan.FromMilliseconds(visible);
                time.Text = string.Format("{0:00}:{1:00}", (int)span.TotalMinutes, span.Seconds);
                // 红点脉冲
                int phase = (int)(watch.ElapsedMilliseconds / 500) % 2;
                int size = paused ? 14 : (phase == 0 ? 20 : 14);
                dot.Size = new Size(size, size);
                dot.Location = new Point(16 + (20 - size) / 2, 18 + (20 - size) / 2);
            };
            timer.Start();
            Rectangle area = Screen.FromRectangle(region).WorkingArea;
            int x = region.Left, y = region.Top - Height - 8;
            if (y < area.Top) { x = region.Right + 8; y = region.Top; }
            if (x + Width > area.Right) { x = area.Left + 12; y = area.Top + 12; }
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
        }

        private void TogglePause()
        {
            if (finished) return;
            paused = !paused;
            if (paused) { pauseStart = watch.ElapsedMilliseconds; pauseBtn.Text = "继续"; }
            else { pausedAccum += watch.ElapsedMilliseconds - pauseStart; pauseBtn.Text = "暂停"; }
            if (onPause != null) onPause(paused);
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            Close();
            if (onStop != null) onStop();
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
