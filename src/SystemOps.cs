using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Windows.Automation;

namespace Mengqi
{
    internal sealed class FileTarget
    {
        internal string Name = "";
        internal string Path;
        internal Rectangle Rect;
    }

    internal static class SystemOps
    {
        // ---- intent recognition ----
        internal static bool IsDeleteIntent(string text)
        {
            if (text.Contains("提醒") || text.Contains("待办") || text.Contains("备份")) return false;
            return text.Contains("删除文件") || text.Contains("删掉文件") || text.Contains("删除这个") ||
                   text.Contains("删掉这个") || (text.Contains("删") && text.Contains("文件")) ||
                   (text.Contains("删") && text.Contains("桌面"));
        }

        internal static bool TryParseCreate(string text, out string name, out bool folder)
        {
            name = null; folder = false;
            Match m = Regex.Match(text, "(新建|创建|建)(个|一个|新建一个)?(文件夹|目录|文件)");
            if (!m.Success) return false;
            folder = m.Groups[3].Value != "文件";
            string rest = text.Substring(m.Index + m.Length);
            rest = Regex.Replace(rest, "^(个|一个|叫|叫做|叫作|名为|名字是|名字为|：|:)+", "").Trim();
            rest = rest.Trim(new char[] {' ', '\u3000', '。', '，', '！', '！', '？', '?', '"', '"', '\'', '\'', '、'});
            if (rest.Length == 0) name = folder ? "新建文件夹" : "新建文件";
            else name = rest;
            if (!folder && !Path.HasExtension(name)) name = name + ".txt";
            name = CleanName(name);
            return name.Length > 0;
        }

        internal static string CleanName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            // Windows 不接受以点或空格结尾的名字，直接建会抛异常
            return name.TrimEnd('.', ' ');
        }

        /// <summary>把用户填的名字和所选后缀合成最终文件名；后缀留空且名字没带后缀时补 .txt。</summary>
        internal static string ComposeName(string name, bool folder, string suffix)
        {
            name = (name ?? "").Trim();
            if (folder) return name.Length == 0 ? "新建文件夹" : name;
            suffix = (suffix ?? "").Trim();
            if (suffix.Length > 0)
            {
                if (suffix[0] != '.') suffix = "." + suffix;
                if (name.Length == 0) return "新建文件" + suffix;
                string stem = Path.GetFileNameWithoutExtension(name);
                return (stem.Length > 0 ? stem : name) + suffix;
            }
            if (name.Length == 0) return "新建文件.txt";
            return Path.HasExtension(name) ? name : name + ".txt";
        }

        // ---- actions ----
        internal static string CreateItem(string name, bool folder, string root)
        {
            string dir = root ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string safe = CleanName(name);
            if (safe.Length == 0) throw new ArgumentException("名字里没有能用的字符。");
            string path = Path.Combine(dir, safe);
            if (folder)
            {
                // 文件夹也要重名自动加序号：Directory.CreateDirectory 遇到已存在的目录会静默成功，
                // 用户看到的就是"点了几次都没建出来"。
                int k = 1; string candidate = path;
                while (Directory.Exists(candidate) || File.Exists(candidate)) { k++; candidate = Path.Combine(dir, safe + "-" + k); }
                Directory.CreateDirectory(candidate);
                NotifyShellChanged(candidate);
                return candidate;
            }
            string ext = Path.GetExtension(safe);
            string stem = safe.Substring(0, safe.Length - ext.Length);
            int n = 1; string file = path;
            while (File.Exists(file) || Directory.Exists(file)) { n++; file = Path.Combine(dir, stem + "-" + n + ext); }
            File.WriteAllText(file, "");
            NotifyShellChanged(file);
            return file;
        }

        /// <summary>真删除：不进回收站，直接从磁盘上抹掉。
        /// 系统删除失败时会摘掉只读/隐藏/系统属性再试，仍不行就走文件系统强制删；被其他程序占用的文件删不掉，会把原因讲清楚。</summary>
        public static void DeleteForever(string path)
        {
            if (String.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                throw new FileNotFoundException("没找到这个文件。", path);
            int ret = ShDelete(path);
            if (ret != 0 || File.Exists(path) || Directory.Exists(path))
            {
                ClearBlockingAttributes(path);                       // 只读/隐藏/系统属性经常是删不掉的原因
                ret = ShDelete(path);
            }
            if (ret != 0 || File.Exists(path) || Directory.Exists(path))
            {
                try { ForceDelete(path); }                           // 最后一招：直接走文件系统
                catch (Exception error) { throw new IOException(Explain(ret) + "强制删除也没成功：" + error.Message); }
            }
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException(Explain(ret));
            NotifyShellChanged(path);
        }

        private static int ShDelete(string path)
        {
            SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
            op.hwnd = IntPtr.Zero; op.wFunc = 3; op.pFrom = path + "\0";
            // 没有 FOF_ALLOWUNDO → 不进回收站；NOCONFIRMATION+SILENT+NOERRORUI → 不弹系统对话框
            op.fFlags = (ushort)(0x10 | 0x4 | 0x400);
            int ret = SHFileOperation(ref op);
            return ret == 0 && (File.Exists(path) || Directory.Exists(path)) ? -1 : ret;   // -1：系统说成功但东西还在
        }

        /// <summary>把文件或整个文件夹里所有项的只读/隐藏/系统属性摘掉，这些属性会让删除被拒。</summary>
        private static void ClearBlockingAttributes(string path)
        {
            try
            {
                if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); return; }
                if (!Directory.Exists(path)) return;
                File.SetAttributes(path, FileAttributes.Directory);
                foreach (string item in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
                    try { if (File.Exists(item)) File.SetAttributes(item, FileAttributes.Normal); else File.SetAttributes(item, FileAttributes.Directory); }
                    catch { }                                        // 个别项摘不掉就留给最后一步去试
            }
            catch { }
        }

        private static void ForceDelete(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else File.Delete(path);
        }

        /// <summary>把删除失败的错误码翻译成用户能懂的话。</summary>
        private static string Explain(int code)
        {
            if (code == 0) return "文件还在原处，可能被别的软件重新创建或占用。";
            switch (code)
            {
                case -1: return "系统说删好了，但文件还在原处。";
                case 2: case 3: return "文件不存在或路径不对。";
                case 5: case 0x78: return "没有删除权限，可能需要用管理员身份运行，或文件正被占用。";
                case 32: return "文件正被其他程序占用，请先关掉正在使用它的软件再试。";
                case 0x71: return "路径无效。";
                case 0x77: return "删除路径太长，试试先把外层文件夹改名变短。";
                case 0x7A: return "出错了：不能把文件夹删进它自己里面。";
                default: return "系统删除失败，错误码 " + code + "。";
            }
        }

        /// <summary>让资源管理器立刻刷新所在目录（删除后图标马上消失，而不是等下次刷新）。</summary>
        private static void NotifyShellChanged(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (String.IsNullOrEmpty(folder)) return;
                IntPtr pointer = Marshal.StringToHGlobalUni(folder);
                try { SHChangeNotify(0x00001000 /*SHCNE_UPDATEDIR*/, 0x0005u | 0x1000u /*SHCNF_PATHW|SHCNF_FLUSH*/, pointer, IntPtr.Zero); }
                finally { Marshal.FreeHGlobal(pointer); }
            }
            catch { }
        }

        /// <summary>取文件的壳图标（文件夹也能取），失败时手绘一张占位纸页。</summary>
        internal static Bitmap IconImageFor(string path)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                SHFILEINFO info = new SHFILEINFO();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x100 /*SHGFI_ICON*/) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    handle = info.hIcon;
                    using (Icon icon = Icon.FromHandle(handle)) return icon.ToBitmap();
                }
            }
            catch { }
            finally { if (handle != IntPtr.Zero) DestroyIcon(handle); }
            try { using (Icon icon = Icon.ExtractAssociatedIcon(path)) return icon.ToBitmap(); } catch { }
            Bitmap page = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(page))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(Brushes.White, 6, 2, 20, 28);
                g.DrawRectangle(Pens.Silver, 6, 2, 20, 28);
                using (Pen pen = new Pen(Color.LightGray, 2f)) { g.DrawLine(pen, 10, 10, 22, 10); g.DrawLine(pen, 10, 16, 22, 16); g.DrawLine(pen, 10, 22, 18, 22); }
            }
            return page;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string displayName; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo;
            public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
        }

        // ---- aiming: enumerate file icons on desktop and explorer windows ----
        internal static List<FileTarget> EnumerateFileTargets()
        {
            List<FileTarget> result = new List<FileTarget>();
            Dictionary<string, Dictionary<string, string>> index =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
                {
                    string cls;
                    try { cls = (string)window.GetCurrentPropertyValue(AutomationElement.ClassNameProperty); } catch { continue; }
                    if (cls != "Progman" && cls != "WorkerW" && cls != "CabinetWClass" && cls != "ExploreWClass") continue;
                    string folder;
                    if (cls == "Progman" || cls == "WorkerW")
                        folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    else
                        folder = ExplorerPath(window);
                    AutomationElement list = BestList(window);
                    if (list == null) continue;
                    // Descendants (not Children): Windows 11 nests items inside date "Group" nodes in
                    // grouped folders (Downloads / Documents / Pictures), where Children returns 0.
                    foreach (AutomationElement item in list.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)))
                    {
                        try
                        {
                            string itemText = (string)item.GetCurrentPropertyValue(AutomationElement.NameProperty);
                            Rectangle rect = BoundsOf(item);
                            if (String.IsNullOrWhiteSpace(itemText) || rect.Width <= 0 || rect.Height <= 0) continue;
                            FileTarget target = new FileTarget();
                            target.Name = itemText;
                            target.Path = (folder != null && !folder.StartsWith("::")) ? ResolvePath(folder, itemText, index) : null;
                            target.Rect = rect;
                            result.Add(target);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>Pick the real file view, not the tab strip or nav pane.</summary>
        private static AutomationElement BestList(AutomationElement window)
        {
            AutomationElement best = null; double bestScore = -1;
            foreach (AutomationElement candidate in window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List)))
            {
                string cls = "";
                try { cls = (string)candidate.GetCurrentPropertyValue(AutomationElement.ClassNameProperty); } catch { }
                Rectangle rect = BoundsOf(candidate);
                double area = (double)rect.Width * rect.Height;
                bool fileView = cls == "UIItemsView" || cls == "SysListView32" || cls == "DUIListView";
                double score = (fileView ? 1e12 : 0) + area;
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }

        private static Rectangle BoundsOf(AutomationElement element)
        {
            object bounds = element.GetCurrentPropertyValue(AutomationElement.BoundingRectangleProperty);
            Type type = bounds.GetType();
            double x = Convert.ToDouble(type.InvokeMember("X", BindingFlags.GetProperty, null, bounds, null));
            double y = Convert.ToDouble(type.InvokeMember("Y", BindingFlags.GetProperty, null, bounds, null));
            double w = Convert.ToDouble(type.InvokeMember("Width", BindingFlags.GetProperty, null, bounds, null));
            double h = Convert.ToDouble(type.InvokeMember("Height", BindingFlags.GetProperty, null, bounds, null));
            return Rectangle.Round(new RectangleF((float)x, (float)y, (float)w, (float)h));
        }

        /// <summary>Map a shell display name back to a real path (handles hidden extensions and shortcuts).</summary>
        internal static string ResolvePath(string folder, string displayName)
        {
            return ResolvePath(folder, displayName,
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));
        }

        private static string ResolvePath(string folder, string displayName, Dictionary<string, Dictionary<string, string>> index)
        {
            string direct = Path.Combine(folder, displayName);
            if (File.Exists(direct) || Directory.Exists(direct)) return direct;
            if (File.Exists(direct + ".lnk")) return direct + ".lnk";
            if (File.Exists(direct + ".url")) return direct + ".url";
            Dictionary<string, string> map;
            if (!index.TryGetValue(folder, out map))
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string entry in Directory.GetFileSystemEntries(folder))
                    {
                        string file = Path.GetFileName(entry);
                        map[file] = entry;
                        string stem = Path.GetFileNameWithoutExtension(entry);
                        if (stem.Length > 0 && !map.ContainsKey(stem)) map[stem] = entry;
                    }
                }
                catch { }
                index[folder] = map;
            }
            string found;
            return map.TryGetValue(displayName, out found) ? found : null;
        }

        private static string ExplorerPath(AutomationElement window)
        {
            try
            {
                long handle = window.Current.NativeWindowHandle;
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                Type shellType = shell.GetType();
                object windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
                foreach (object win in (System.Collections.IEnumerable)windows)
                {
                    Type winType = win.GetType();
                    object hwnd = winType.InvokeMember("HWND", BindingFlags.GetProperty, null, win, null);
                    // COM hands HWND back as Int64; casting straight to int threw InvalidCastException.
                    if (hwnd == null || Convert.ToInt64(hwnd) != handle) continue;
                    object document = winType.InvokeMember("Document", BindingFlags.GetProperty, null, win, null);
                    object folder = document.GetType().InvokeMember("Folder", BindingFlags.GetProperty, null, document, null);
                    object self = folder.GetType().InvokeMember("Self", BindingFlags.GetProperty, null, folder, null);
                    object path = self.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, self, null);
                    string value = path as string;
                    if (!String.IsNullOrEmpty(value) && Directory.Exists(value)) return value;
                    return null;
                }
            }
            catch { }
            return null;
        }

        internal static FileTarget PickTarget(List<FileTarget> targets, Point point)
        {
            foreach (FileTarget target in targets)
                if (target.Rect.Contains(point)) return target;
            FileTarget best = null; double bestDist = 20.0 * 20.0;
            foreach (FileTarget target in targets)
            {
                double cx = target.Rect.Left + target.Rect.Width / 2.0, cy = target.Rect.Top + target.Rect.Height / 2.0;
                double d = (cx - point.X) * (cx - point.X) + (cy - point.Y) * (cy - point.Y);
                if (d < bestDist) { bestDist = d; best = target; }
            }
            return best;
        }

        internal static string KindOf(FileTarget target)
        {
            if (target.Path != null && Directory.Exists(target.Path)) return "文件夹";
            return "文件";
        }
    }

    // ---- fullscreen aiming overlay with crosshair cursor ----
    internal sealed class TargetOverlay : Form
    {
        private readonly PetWindow pet;
        private bool deciding;
        private readonly System.Windows.Forms.Timer guard = new System.Windows.Forms.Timer { Interval = 60000 };

        internal TargetOverlay(PetWindow pet)
        {
            this.pet = pet;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = SystemInformation.VirtualScreen;
            StartPosition = FormStartPosition.Manual;
            TopMost = true; ShowInTaskbar = false; KeyPreview = true;
            BackColor = Color.FromArgb(24, 16, 40);
            Opacity = 0.22;
            Cursor = Cursors.Cross;
            Text = "萌栖瞄准模式（Esc 取消）";
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Cancel(); };
            MouseClick += OnClick;
            guard.Tick += delegate { Cancel(); };   // 兜底：一分钟后无论如何都把桌面还给你
            guard.Start();
        }

        private void Cancel()
        {
            guard.Stop();
            Close();
            pet.PlayAction(3, 2200);
            pet.Speak("已取消瞄准，桌面还给你。", 3000);
        }

        private void OnClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || deciding) return;
            deciding = true;
            Cursor = Cursors.WaitCursor;
            Point point = Cursor.Position;
            List<FileTarget> targets = SystemOps.EnumerateFileTargets();
            FileTarget hit = SystemOps.PickTarget(targets, point);
            Cursor = Cursors.Default;
            Close();                       // 无论哪种结果都先关掉遮罩，把桌面还给你
            if (hit == null)
            {
                pet.PlayAction(6, 2400);
                pet.Speak("这里没有文件喵。把十字对准文件图标再点一次，或者重新发“删除文件”。", 6000);
                return;
            }
            if (hit.Path == null)
            {
                pet.PlayAction(6, 2400);
                pet.Speak("「" + hit.Name + "」是系统图标，背后没有真实文件，我删不了喵。", 5000);
                return;
            }
            pet.WalkToAndAsk(hit);
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) guard.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---- particle effects drawn on a click-through layered window ----
    internal sealed class EffectLayer : Form
    {
        private sealed class Particle { public float X, Y, VX, VY, Radius, Life, Spin; public Color Color; public bool Star; }

        private readonly List<Particle> particles = new List<Particle>();
        private readonly Timer timer = new Timer { Interval = 33 };
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly long total;
        private readonly Point origin;
        private const int LayerSize = 300;

        private EffectLayer(Point center, string kind)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            origin = new Point(center.X - LayerSize / 2, center.Y - LayerSize / 2);
            Bounds = new Rectangle(origin, new Size(LayerSize, LayerSize));
            Random random = new Random();
            if (kind == "poof")
            {
                total = 620;
                for (int i = 0; i < 14; i++)
                {
                    double angle = i * Math.PI * 2 / 14 + random.NextDouble() * 0.4;
                    float speed = 1.6f + (float)random.NextDouble() * 2.2f;
                    Particle p = new Particle();
                    p.X = LayerSize / 2; p.Y = LayerSize / 2;
                    p.VX = (float)Math.Cos(angle) * speed; p.VY = (float)Math.Sin(angle) * speed - 0.5f;
                    p.Radius = 5 + (float)random.NextDouble() * 7;
                    p.Life = 1f;
                    p.Color = Color.FromArgb(255, 176 + random.Next(40), 168 + random.Next(40), 158 + random.Next(40));
                    particles.Add(p);
                }
            }
            else
            {
                total = 780;
                for (int i = 0; i < 12; i++)
                {
                    Particle p = new Particle();
                    p.X = LayerSize / 2 + (float)(random.NextDouble() - 0.5) * 90;
                    p.Y = LayerSize / 2 + (float)(random.NextDouble() - 0.5) * 60;
                    p.VX = (float)(random.NextDouble() - 0.5) * 1.2f; p.VY = -0.9f - (float)random.NextDouble() * 1.1f;
                    p.Radius = 3 + (float)random.NextDouble() * 5;
                    p.Life = 1f; p.Star = true; p.Spin = (float)random.NextDouble() * 6f;
                    p.Color = Color.FromArgb(255, 255, 214 + random.Next(40), 96 + random.Next(60));
                    particles.Add(p);
                }
            }
            timer.Tick += delegate { TickFrame(); };
        }

        internal static void Launch(Point center, string kind)
        {
            EffectLayer layer = new EffectLayer(center, kind);
            layer.Show();
            layer.timer.Start();
        }

        private void TickFrame()
        {
            float t = watch.ElapsedMilliseconds / (float)total;
            if (t >= 1f) { timer.Stop(); Close(); return; }
            using (Bitmap canvas = Render(t))
            using (Bitmap premultiplied = Native.Premultiply(canvas))
                Native.PaintLayer(Handle, premultiplied, origin, 255);
        }

        private Bitmap Render(float t)
        {
            Bitmap canvas = new Bitmap(LayerSize, LayerSize, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (Particle p in particles)
                {
                    p.X += p.VX; p.Y += p.VY;
                    int alpha = (int)(255 * (1 - t) * p.Life);
                    if (p.Star)
                    {
                        float r = p.Radius * (0.6f + 0.4f * (float)Math.Sin(t * 12 + p.Spin));
                        using (Pen pen = new Pen(Color.FromArgb(alpha, p.Color), 2f))
                        {
                            g.DrawLine(pen, p.X - r, p.Y, p.X + r, p.Y);
                            g.DrawLine(pen, p.X, p.Y - r, p.X, p.Y + r);
                            float d = r * 0.5f;
                            g.DrawLine(pen, p.X - d, p.Y - d, p.X + d, p.Y + d);
                            g.DrawLine(pen, p.X - d, p.Y + d, p.X + d, p.Y - d);
                        }
                    }
                    else
                    {
                        float r = p.Radius * (0.5f + t * 1.8f);
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(alpha * 0.55), p.Color)))
                            g.FillEllipse(brush, p.X - r, p.Y - r, r * 2, r * 2);
                    }
                }
            }
            return canvas;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020; return cp; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---- the file icon gets sucked into the pet's mouth ----
    internal sealed class SwallowLayer : Form
    {
        private readonly Point startCenter;
        private readonly Point mouth;
        private readonly int startSize;
        private readonly Bitmap sprite;
        private readonly Action done;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly Point origin;
        private const int TotalMs = 640;
        private const int Pad = 30;
        private const int EndSize = 10;

        private SwallowLayer(Rectangle iconRect, Point mouthPoint, Bitmap image, Action callback)
        {
            startCenter = new Point(iconRect.Left + iconRect.Width / 2, iconRect.Top + iconRect.Height / 2);
            mouth = mouthPoint;
            sprite = image;
            done = callback;
            startSize = Math.Max(34, Math.Min(iconRect.Width, 58));
            Rectangle box = Rectangle.Round(RectangleF.Union(
                new RectangleF(startCenter.X - startSize / 2f - Pad, startCenter.Y - startSize / 2f - Pad, startSize + Pad * 2, startSize + Pad * 2),
                new RectangleF(mouth.X - EndSize - Pad, mouth.Y - EndSize - Pad, EndSize * 2 + Pad * 2, EndSize * 2 + Pad * 2)));
            origin = box.Location;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            Bounds = box;
            timer.Tick += delegate { TickFrame(); };
        }

        internal static void Launch(Rectangle iconRect, Point mouthPoint, Bitmap image, Action callback)
        {
            SwallowLayer layer = new SwallowLayer(iconRect, mouthPoint, image, callback);
            layer.Show();
            layer.timer.Start();
        }

        private RectangleF Lerp(double t)
        {
            double size = startSize + (EndSize - startSize) * t;
            double x = startCenter.X + (mouth.X - startCenter.X) * t - size / 2;
            double y = startCenter.Y + (mouth.Y - startCenter.Y) * t - size / 2;
            return new RectangleF((float)(x - origin.X), (float)(y - origin.Y), (float)size, (float)size);
        }

        private void TickFrame()
        {
            double t = watch.ElapsedMilliseconds / (double)TotalMs;
            if (t >= 1f) { timer.Stop(); Close(); return; }
            double ease = t * t;                       // 先慢后快，像被吸进去
            using (Bitmap canvas = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    for (int k = 4; k >= 1; k--)        // 拖尾残影
                    {
                        double back = t - k * 0.05;
                        if (back <= 0) continue;
                        RectangleF ghost = Lerp(back * back);
                        int alpha = (int)(70 * (1 - k / 5.0) * (1 - t));
                        using (ImageAttributes attributes = new ImageAttributes())
                        {
                            ColorMatrix matrix = new ColorMatrix { Matrix33 = alpha / 255f };
                            attributes.SetColorMatrix(matrix);
                            g.DrawImage(sprite, Rectangle.Round(ghost), 0f, 0f, sprite.Width, sprite.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    g.DrawImage(sprite, Lerp(ease));
                }
                using (Bitmap premultiplied = Native.Premultiply(canvas))
                    Native.PaintLayer(Handle, premultiplied, origin, 255);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (sprite != null) sprite.Dispose();
            if (done != null) done();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020; return cp; }
        }
    }

    // ---- the pet asks face to face, next to the file ----
    internal sealed class DeleteConfirm : RoundedForm
    {
        private readonly Action yes;
        private readonly Action no;
        private bool answered;
        private readonly System.Windows.Forms.Timer timeout = new System.Windows.Forms.Timer();

        internal DeleteConfirm(string title, string detail, Action onYes, Action onNo)
            : this(title, detail, "吃掉它", "先留着", onYes, onNo) { }

        internal DeleteConfirm(string title, string detail, string yesLabel, string noLabel, Action onYes, Action onNo)
        {
            yes = onYes; no = onNo;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(250, 247, 255);
            Font = new Font("Microsoft YaHei UI", 10);
            CornerRadius = 18;
            Size = new Size(330, 122);
            Label head = new Label { Text = title, Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(73, 54, 103), Bounds = new Rectangle(18, 12, 294, 30) };
            Label note = new Label { Text = detail, Font = new Font("Microsoft YaHei UI", 9), ForeColor = Color.FromArgb(146, 124, 176), Bounds = new Rectangle(18, 44, 294, 26) };
            Button eat = Ui.Button(yesLabel, delegate { Answer(true); });
            Button keep = Ui.Button(noLabel, delegate { Answer(false); });
            eat.Location = new Point(128, 74); keep.Location = new Point(232, 74);
            eat.BackColor = Color.FromArgb(255, 214, 96); eat.ForeColor = Color.FromArgb(73, 54, 103);
            Controls.Add(head); Controls.Add(note); Controls.Add(eat); Controls.Add(keep);
            timeout.Interval = 20000;
            timeout.Tick += delegate { Answer(false); };
        }

        internal void PositionAbove(PetWindow pet)
        {
            Rectangle area = Screen.FromControl(pet).WorkingArea;
            int x = pet.Left + pet.Width / 2 - Width / 2;
            int y = pet.Top - Height - 10;
            if (y < area.Top) y = Math.Min(area.Bottom - Height, pet.Bottom + 10);
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), y);
        }

        private void Answer(bool accepted)
        {
            if (answered) return;
            answered = true;
            timeout.Stop();
            Close();
            if (accepted && yes != null) yes();
            if (!accepted && no != null) no();
        }

        protected override bool ProcessDialogKey(Keys key)
        {
            if (key == Keys.Escape) { Answer(false); return true; }
            return base.ProcessDialogKey(key);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timeout.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timeout.Dispose();
            base.Dispose(disposing);
        }
    }
}
