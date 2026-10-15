using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Mengqi
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // DPI 感知改为在 app.manifest 中声明（PerMonitorV2 + dpiAware），
            // 不再调用 SetProcessDPIAware——后者会把进程降为 System DPI Aware，
            // 覆盖清单的逐显示器 V2 感知，导致在高分屏下界面被系统位图拉伸、文字发虚或显示不全。
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--self-test")
                return SelfTest(args[1]);
            if (args.Length == 3 && args[0] == "--record-test")
                return RecordTest(args[1], args[2]);
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\MengqiDesktopPet_v1", out first))
            {
                if (!first)
                {
                    MessageBox.Show("萌栖已经在运行。可以在任务栏通知区域找到萌栖图标。", "萌栖");
                    return 0;
                }
                try { Application.Run(new PetWindow()); }
                catch (Exception error)
                {
                    MessageBox.Show("萌栖未能启动：\n" + error.Message, "萌栖", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }

        /// <summary>隐藏的录屏冒烟测试：走真实的「抓屏 → 编码 → MP4」链路，结束后把结论写到 &lt;输出&gt;.report.json。
        /// 用法：萌栖.exe --record-test 输出.mp4 秒数</summary>
        private static int RecordTest(string outPath, string seconds)
        {
            string report = Path.ChangeExtension(outPath, ".report.json");
            try
            {
                double secs = 2.0;
                double.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs);
                if (secs < 0.5) secs = 0.5;
                System.Drawing.Rectangle region = System.Windows.Forms.SystemInformation.VirtualScreen;
                string backend = VideoSinkFactory.BackendName;
                int frames = 0; string error = null;
                try
                {
                    using (ScreenRecorder rec = ScreenRecorder.Start(region, outPath, 10))
                    {
                        long until = Environment.TickCount + (long)(secs * 1000);
                        while (Environment.TickCount < until)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        }
                        frames = rec.Frames;
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                long size = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
                string json = "{\"backend\":\"" + backend + "\",\"region\":\"" + region.Width + "x" + region.Height +
                    "\",\"frames\":" + frames + ",\"bytes\":" + size + ",\"ok\":" + (error == null && size > 500 ? "true" : "false") +
                    ",\"error\":\"" + (error ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ") + "\"}";
                File.WriteAllText(report, json, Encoding.UTF8);
                return (error == null && size > 500) ? 0 : 1;
            }
            catch (Exception fatal)
            {
                File.WriteAllText(report, "{\"ok\":false,\"error\":\"" + fatal.Message.Replace("\"", "'") + "\"}", Encoding.UTF8);
                return 1;
            }
        }

        private static int SelfTest(string report)
        {
            try
            {
                Environment.SetEnvironmentVariable("MENGQI_DATA_DIR", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "_work", "test-profile"));
                Environment.SetEnvironmentVariable("MENGQI_SELF_TEST","1");
                using (PetWindow pet = new PetWindow())
                {
                    int count = 0;
                    foreach (int scale in new int[] { 75, 100, 125, 150 })
                    {
                        pet.ApplyScale(scale);
                        for (int row = 0; row < PetWindow.Durations.Length; row++)
                            for (int frame = 0; frame < PetWindow.Durations[row].Length; frame++)
                            {
                                pet.RenderForTest(row, frame);
                                count++;
                            }
                    }
                    pet.VerifyInteractions();
                    FeatureTests.Run(pet, Path.GetDirectoryName(Path.GetFullPath(report)));
                    string json = "{\"ok\":true,\"states\":15,\"frames\":81,\"nativeRenders\":" + count +
                        ",\"scales\":[75,100,125,150],\"transparentLayeredWindow\":true,\"interactionChecks\":true,\"atlasWidth\":1536,\"atlasHeight\":1872}";
                    File.WriteAllText(report, json, Encoding.UTF8);
                }
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(report, "{\"ok\":false,\"error\":\"" + error.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"}", Encoding.UTF8);
                return 1;
            }
        }
    }

    internal sealed partial class PetWindow : Form
    {
        internal static readonly int[][] Durations = {
            new int[] {280,110,110,140,140,320},
            new int[] {120,120,120,120,120,120,120,220},
            new int[] {120,120,120,120,120,120,120,220},
            new int[] {140,140,140,280},
            new int[] {140,140,140,140,280},
            new int[] {140,140,140,140,140,140,140,240},
            new int[] {150,150,150,150,150,260},
            new int[] {120,120,120,120,120,220},
            new int[] {150,150,150,150,150,280},
            new int[] {420,420,420,420}, new int[] {220,220,220,320}, new int[] {240,240,240,320}, new int[] {200,200,240,360}, new int[] {220,220,220,320}, new int[] {180,180,180,320}
        };
        private static readonly string[] Labels = {"待机眨眼", "向右跑动", "向左跑动", "挥爪打招呼", "跳跃", "失落趴下", "等待回应", "专注揉爪", "观察思考", "睡觉", "伸懒腰", "舔毛", "打哈欠", "吃饭", "玩球"};
        // Chouchou uses calmer timing: fewer, longer poses so the motion reads smooth instead of busy.
        internal static readonly int[][] ChouchouDurations = {
            new int[] {420,130,170,170,420,420},
            new int[] {150,150,150,150,150,150,150,200},
            new int[] {150,150,150,150,150,150,150,200},
            new int[] {220,220,220,320},
            new int[] {200,200,220,200,320},
            new int[] {200,200,200,200,200,200,200,260},
            new int[] {220,220,220,220,220,300},
            new int[] {200,200,200,200,200,260},
            new int[] {220,220,220,220,220,300},
            new int[] {520,520,520,520}, new int[] {300,300,300,380}, new int[] {300,300,300,380}, new int[] {240,260,300,420}, new int[] {280,280,280,380}, new int[] {240,240,240,380}
        };
        private int[][] ActiveDurations { get { return currentPreset == "chouchou" ? ChouchouDurations : Durations; } }
        private Bitmap atlas;
        private Bitmap extraAtlas;
        private string currentPreset = "";
        private readonly Dictionary<int, Bitmap[]> cache = new Dictionary<int, Bitmap[]>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem automaticItem = new ToolStripMenuItem("自由活动");
        private readonly ToolStripMenuItem topmostItem = new ToolStripMenuItem("保持在最上层");
        private Icon catIcon;
        private int scalePercent = 100;
        private int state;
        private int paintedState = -1;
        private int paintedFrame = -1;
        private int paintedSmooth = -1;
        private bool hiddenForCapture;
        private long stateStarted;
        private long stateUntil;
        private long nextActivity;
        private long lastTick;
        private bool automatic = true;
        private bool dragging;
        private bool moved;
        private Point mouseStart;
        private Point windowStart;
        private int walkingDirection;
        private double preciseX;
        // ---- 多桌宠同屏：同一进程里最多再带四只陪伴桌宠，功能（提醒/专注/聊天）只在主桌宠上跑 ----
        internal static readonly List<PetWindow> Companions = new List<PetWindow>();
        private bool companion;
        private AppData companionData;
        private string companionPetId = "";

        internal PetWindow() { InitializeWindow(); }

        /// <summary>陪伴桌宠：共享一份只读数据克隆，按指定形象出场，不注册热键、不跑提醒。</summary>
        internal PetWindow(AppData sharedData,string petId)
        {
            companion = true;companionData = sharedData;companionPetId = petId ?? "";
            Companions.Add(this);
            InitializeWindow();
        }

        private void InitializeWindow()
        {
            LoadPreset("xiaohei");
            Text = "萌栖";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(192, 208);
            DoubleBuffered = false;
            BuildMenu();
            ContextMenuStrip = menu;
            BuildIcon();
            tray.Icon = catIcon;
            tray.Text = "萌栖 · 右键打开菜单";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenHub("chat"); };
            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;
            MouseCaptureChanged += delegate { if (dragging && !Capture) FinishDrag(); };
            MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { if (companion) Play(3, 2200, 0); else OpenHub("chat"); } };
            timer.Interval = 30;
            timer.Tick += TickAnimation;
            nextActivity = 12000;
            InitializeFeatures();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private void LoadPreset(string preset)
        {
            if (currentPreset == preset) return;
            string baseResource = preset == "chouchou" ? "chouchou.atlas.png" : "xiaohei.atlas.png";
            string extraResource = preset == "chouchou" ? "chouchou.extra.png" : "mengqi.extra.png";
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(baseResource))
            {
                if (source == null) throw new InvalidDataException("内置动作图集缺失。");
                using (Bitmap loaded = new Bitmap(source))
                {
                    Bitmap next = new Bitmap(loaded);
                    if (atlas != null) atlas.Dispose();
                    atlas = next;
                }
            }
            using (Stream extras = Assembly.GetExecutingAssembly().GetManifestResourceStream(extraResource))
            {
                Bitmap next = null;
                if (extras != null) using (Bitmap b = new Bitmap(extras)) next = new Bitmap(b);
                if (extraAtlas != null) extraAtlas.Dispose();
                extraAtlas = next;
            }
            if (atlas.Width != 1536 || atlas.Height != 1872) throw new InvalidDataException("动作图集尺寸不正确。");
            currentPreset = preset;
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; return cp; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if(Environment.GetEnvironmentVariable("MENGQI_SELF_TEST")=="1") return;
            ResetPosition();
            PaintFrame(0, 0);
            tray.Visible = true;
            lastTick = clock.ElapsedMilliseconds;
            timer.Start();
            FeaturesShown();
        }

        private void BuildMenu()
        {
            ToolStripMenuItem title = new ToolStripMenuItem("萌栖") { Enabled = false };
            menu.Items.Add(title);
            ToolStripMenuItem actions = new ToolStripMenuItem("动作");
            for (int i = 0; i < Labels.Length; i++)
            {
                int row = i;
                ToolStripMenuItem item = new ToolStripMenuItem(Labels[i]);
                item.Click += delegate { Play(row, row == 0 ? 0 : 3200, 0); };
                actions.DropDownItems.Add(item);
            }
            menu.Items.Add(actions);
            ToolStripMenuItem size = new ToolStripMenuItem("大小");
            foreach (int value in new int[] {75,100,125,150})
            {
                int percent = value;
                ToolStripMenuItem item = new ToolStripMenuItem(percent + "%") { Checked = percent == 100, Tag = percent };
                item.Click += delegate {
                    ApplyScale(percent);
                    foreach (ToolStripMenuItem other in size.DropDownItems) other.Checked = (int)other.Tag == percent;
                    ConstrainToScreen();
                    PaintFrame(state, CurrentFrame(clock.ElapsedMilliseconds));
                };
                size.DropDownItems.Add(item);
            }
            menu.Items.Add(size);
            automaticItem.Checked = automatic;
            automaticItem.Click += delegate {
                automatic = !automatic;
                automaticItem.Checked = automatic;
                if (!automatic) Play(0, 0, 0);
                ScheduleActivity();
            };
            menu.Items.Add(automaticItem);
            topmostItem.Checked = true;
            topmostItem.Click += delegate { TopMost = !TopMost; topmostItem.Checked = TopMost; };
            menu.Items.Add(topmostItem);
            menu.Items.Add("回到屏幕右下角", null, delegate { ResetPosition(); Play(0,0,0); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出萌栖", null, delegate { Close(); });
        }

        private void BuildIcon()
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("mengqi.icon.ico"))
                using(Icon loaded=new Icon(source,32,32)) {IntPtr handle=loaded.Handle;catIcon=(Icon)loaded.Clone();}
            Icon = catIcon;
        }

        internal void ApplyScale(int percent)
        {
            if (percent != 75 && percent != 100 && percent != 125 && percent != 150) throw new ArgumentOutOfRangeException("percent");
            Point bottom = new Point(Left + Width / 2, Top + Height);
            scalePercent = percent;
            ClientSize = new Size(192 * percent / 100, 208 * percent / 100);
            Location = new Point(bottom.X - Width/2, bottom.Y - Height);
            preciseX = Left;
            paintedFrame = -1;
        }

        private Bitmap[] Frames(int row)
        {
            if (customImage != null) return CustomFrames(row);
            int key = scalePercent * 10 + row;
            Bitmap[] frames;
            if (cache.TryGetValue(key, out frames)) return frames;
            frames = new Bitmap[ActiveDurations[row].Length];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                Bitmap target = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(target))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = scalePercent == 100 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Bitmap sourceAtlas = row < 9 ? atlas : extraAtlas;
                    if (sourceAtlas == null) throw new InvalidDataException("新增动作图集缺失。");
                    g.DrawImage(sourceAtlas, new Rectangle(0,0,Width,Height), new Rectangle(frame*192,(row < 9 ? row : row-9)*208,192,208), GraphicsUnit.Pixel);
                }
                frames[frame] = target;
            }
            cache.Add(key, frames);
            return frames;
        }

        private int CurrentFrame(long time)
        {
            int[] durations = ActiveDurations[state];
            int total = 0;
            foreach (int duration in durations) total += duration;
            long phase = (long)(Math.Max(0, time - stateStarted) * (data==null?1.0:data.MotionSpeed/100.0)) % total;
            for (int i=0; i<durations.Length; i++) { if (phase < durations[i]) return i; phase -= durations[i]; }
            return 0;
        }

        private void Play(int row, int milliseconds, int direction)
        {
            state = row;
            stateStarted = clock.ElapsedMilliseconds;
            stateUntil = milliseconds == 0 ? 0 : stateStarted + milliseconds;
            walkingDirection = direction;
            preciseX = Left;
            paintedFrame = -1;
            paintedSmooth = -1;
            ScheduleActivity();
        }

        private void ScheduleActivity() { nextActivity = clock.ElapsedMilliseconds + (currentPreset == "chouchou" ? random.Next(15000,32000) : random.Next(10000,22000)); }

        private void TickAnimation(object sender, EventArgs e)
        {
            TickFeatures();
            long now = clock.ElapsedMilliseconds;
            double dt = Math.Min(80, Math.Max(0, now - lastTick)) / 1000.0;
            lastTick = now;
            if (menu.Visible) return;
            if (!dragging && stateUntil != 0 && now >= stateUntil) Play(0,0,0);
            if (!dragging && walkingDirection != 0)
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                preciseX += walkingDirection * 46 * scalePercent/100.0 * dt;
                if (preciseX <= area.Left || preciseX + Width >= area.Right)
                {
                    walkingDirection = -walkingDirection;
                    preciseX = Math.Max(area.Left, Math.Min(area.Right-Width, preciseX));
                    state = walkingDirection > 0 ? 1 : 2;
                    stateStarted = now;
                }
                Location = new Point((int)Math.Round(preciseX), Top);
            }
            if (!dragging && automatic && !featureFocusRunning && state == 0 && now >= nextActivity)
            {
                int choice = random.Next(10);
                if (choice < 2) { int direction = choice == 0 ? 1 : -1; Play(direction > 0 ? 1 : 2, currentPreset == "chouchou" ? random.Next(2600,4200) : random.Next(1800,3200), direction); }
                else { int[] actions = {3,4,6,7,8,10,11,12}; Play(actions[choice-2], currentPreset == "chouchou" ? 2800 : 1800, 0); }
            }
            if (hiddenForCapture) return;           // 截图/录屏期间保持隐身，别被拍进去
            int frame = CurrentFrame(now);
            if(data.SmoothMotion) PaintSmooth(now,frame);
            else if (state != paintedState || frame != paintedFrame || paintedSmooth>0) {PaintFrame(state,frame);paintedSmooth=0;}
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true; moved = false;
            mouseStart = Cursor.Position; windowStart = Location;
            walkingDirection = 0;
            Capture = true;
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point cursor = Cursor.Position;
            int dx = cursor.X-mouseStart.X, dy = cursor.Y-mouseStart.Y;
            if (!moved && Math.Abs(dx)+Math.Abs(dy) < 4) return;
            moved = true;
            int direction = cursor.X > Left + (mouseStart.X-windowStart.X) ? 1 : -1;
            Location = new Point(windowStart.X+dx,windowStart.Y+dy);
            int row = direction > 0 ? 1 : 2;
            if (state != row) Play(row,0,0);
            PaintFrame(state,CurrentFrame(clock.ElapsedMilliseconds));
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !dragging) return;
            FinishDrag();
        }

        private void FinishDrag()
        {
            dragging = false;
            Capture = false;
            ConstrainToScreen();
            Play(moved ? 0 : 3,moved ? 0 : 1600,0);
        }

        private void ResetPosition()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left,area.Right-Width-32),Math.Max(area.Top,area.Bottom-Height-16));
            preciseX = Left;
            if (IsHandleCreated) PaintFrame(state,CurrentFrame(clock.ElapsedMilliseconds));
        }

        private void ConstrainToScreen()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Max(area.Left,Math.Min(area.Right-Width,Left)),Math.Max(area.Top,Math.Min(area.Bottom-Height,Top)));
            preciseX = Left;
        }

        private void PaintFrame(int row, int frame)
        {
            Native.PaintLayer(Handle, Frames(row)[frame], Location, data == null ? (byte)255 : (byte)(data.Opacity * 255 / 100));
            PositionBubble();
            paintedState = row; paintedFrame = frame;
        }

        internal void RenderForTest(int row, int frame) { PaintFrame(row,frame); }
        internal ContextMenuStrip MenuForTest { get { return menu; } }

        /// <summary>截屏/录屏前后把桌宠隐掉/恢复（保持窗口存在，只把像素透明度打到 0）。</summary>
        internal void SetVisibleForCapture(bool visible)
        {
            if (!IsHandleCreated || data == null) return;
            if (visible)
            {
                hiddenForCapture = false;
                paintedFrame = -1;
                PaintFrame(state, CurrentFrame(clock.ElapsedMilliseconds));
                return;
            }
            hiddenForCapture = true;      // 必须挡住动画循环的重绘，否则下一帧就把桌宠画回来了
            Native.PaintLayer(Handle, Frames(state)[CurrentFrame(clock.ElapsedMilliseconds)], Location, 0);
            paintedFrame = -1;
        }
        internal void VerifyInteractions()
        {
            Play(3,1600,0);
            if (CurrentFrame(stateStarted) != 0 || CurrentFrame(stateStarted+141) != 1) throw new InvalidOperationException("Frame duration check failed");
            Play(1,2000,1);
            if (state != 1 || walkingDirection != 1) throw new InvalidOperationException("Direction check failed");
            Play(0,0,0);
            if (walkingDirection != 0 || stateUntil != 0) throw new InvalidOperationException("Idle return check failed");
            ResetPosition();
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            if (!area.Contains(Bounds)) throw new InvalidOperationException("Screen bounds check failed");
            if (menu.Items.Count < 8) throw new InvalidOperationException("Context menu check failed");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ShutdownFeatures();
                timer.Stop(); timer.Dispose();
                tray.Visible = false; tray.Dispose();
                foreach (Bitmap[] frames in cache.Values) foreach (Bitmap frame in frames) frame.Dispose();
                cache.Clear();
                atlas.Dispose();
                if (extraAtlas != null) extraAtlas.Dispose();
                menu.Dispose();
                if (catIcon != null) catIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X,Y; internal Point(int x,int y) { X=x;Y=y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int Width,Height; internal Size(int w,int h) { Width=w;Height=h; } }
        [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Blend { internal byte Op,Flags,Alpha,Format; }
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
        [DllImport("user32.dll",SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr window,IntPtr destDc,ref Point position,ref Size size,IntPtr sourceDc,ref Point origin,uint key,ref Blend blend,uint flags);

        internal static void PaintLayer(IntPtr window,Bitmap image,System.Drawing.Point location,byte opacity)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                bitmap = image.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory,bitmap);
                Point position = new Point(location.X,location.Y), origin = new Point(0,0);
                Size size = new Size(image.Width,image.Height);
                Blend blend = new Blend {Op=0,Flags=0,Alpha=opacity,Format=1};
                if (!UpdateLayeredWindow(window,screen,ref position,ref size,memory,ref origin,0,ref blend,2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory,previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero,screen);
            }
        }

        // UpdateLayeredWindow expects premultiplied alpha; particle/animation layers draw with per-pixel alpha.
        internal static Bitmap Premultiply(Bitmap source)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            Rectangle rect = new Rectangle(System.Drawing.Point.Empty, source.Size);
            BitmapData sd = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dd = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int length = sd.Stride * source.Height;
                byte[] buffer = new byte[length];
                Marshal.Copy(sd.Scan0, buffer, 0, length);
                for (int i = 0; i < length; i += 4)
                {
                    byte a = buffer[i + 3];
                    if (a < 255)
                    {
                        buffer[i] = (byte)(buffer[i] * a / 255);
                        buffer[i + 1] = (byte)(buffer[i + 1] * a / 255);
                        buffer[i + 2] = (byte)(buffer[i + 2] * a / 255);
                    }
                }
                Marshal.Copy(buffer, 0, dd.Scan0, length);
            }
            finally { source.UnlockBits(sd); result.UnlockBits(dd); }
            return result;
        }
    }
}

