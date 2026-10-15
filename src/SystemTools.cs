using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Mengqi
{
    /// <summary>
    /// 右键菜单里的系统小工具：清空回收站、整理桌面、截图、取色、状态播报、锁屏、打开常用文件夹。
    /// 全部为本地调用，不联网。
    /// </summary>
    internal static class SystemTools
    {
        // ================= 清空回收站 =================
        internal struct BinInfo { internal bool Ok; internal long Items; internal long Bytes; }

        internal static BinInfo RecycleBinInfo()
        {
            BinInfo info = new BinInfo();
            try
            {
                SHQUERYRBINFO raw = new SHQUERYRBINFO();
                raw.cbSize = (uint)Marshal.SizeOf(typeof(SHQUERYRBINFO));
                if (SHQueryRecycleBin(null, ref raw) == 0) { info.Ok = true; info.Items = raw.i64NumItems; info.Bytes = raw.i64Size; }
            }
            catch { }
            return info;
        }

        internal static void EmptyRecycleBin()
        {
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
            int result = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            if (result != 0) throw new IOException("系统没能清空回收站，错误码 " + result + "。");
        }

        // ================= 一键整理桌面 =================
        private static readonly string[][] Categories = {
            new string[] {"图片", ".jpg",".jpeg",".png",".gif",".bmp",".webp",".heic",".tif",".tiff",".svg",".ico",".raw",".psd"},
            new string[] {"文档", ".doc",".docx",".pdf",".txt",".md",".rtf",".wps",".odt",".pages"},
            new string[] {"表格", ".xls",".xlsx",".csv",".et",".ods",".numbers"},
            new string[] {"演示", ".ppt",".pptx",".dps",".odp",".key"},
            new string[] {"音频", ".mp3",".wav",".flac",".aac",".m4a",".ogg",".wma",".ape"},
            new string[] {"视频", ".mp4",".mkv",".avi",".mov",".wmv",".flv",".webm",".rmvb",".m4v"},
            new string[] {"压缩包", ".zip",".rar",".7z",".tar",".gz",".bz2",".xz",".iso",".cab"},
            new string[] {"程序", ".exe",".msi",".bat",".cmd",".ps1",".apk",".dmg",".appx",".jar"}
        };
        internal const string OtherCategory = "其他";

        internal static string CategoryOf(string extension)
        {
            if (!String.IsNullOrEmpty(extension))
            {
                string ext = extension.ToLowerInvariant();
                foreach (string[] group in Categories)
                    for (int i = 1; i < group.Length; i++)
                        if (group[i] == ext) return group[0];
            }
            return OtherCategory;
        }

        /// <summary>列出可以整理的散落文件（跳过文件夹、快捷方式、隐藏与系统文件）。</summary>
        internal static Dictionary<string, List<string>> PlanTidy(string folder, out int total)
        {
            Dictionary<string, List<string>> plan = new Dictionary<string, List<string>>();
            total = 0;
            string[] files;
            try { files = Directory.GetFiles(folder); } catch { return plan; }
            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    FileAttributes attributes = File.GetAttributes(file);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                }
                catch { continue; }
                string category = CategoryOf(ext);
                if (!plan.ContainsKey(category)) plan[category] = new List<string>();
                plan[category].Add(file);
                total++;
            }
            return plan;
        }

        /// <summary>把文件搬进同名分类文件夹，返回"图片 5 个、文档 2 个"这样的摘要。</summary>
        internal static string TidyDesktop(Dictionary<string, List<string>> plan, string folder)
        {
            StringBuilder summary = new StringBuilder();
            foreach (KeyValuePair<string, List<string>> entry in plan)
            {
                string target = Path.Combine(folder, entry.Key);
                if (File.Exists(target)) continue;                 // 桌面已有同名文件，这一类跳过
                try { if (!Directory.Exists(target)) Directory.CreateDirectory(target); }
                catch { continue; }
                int count = 0;
                foreach (string file in entry.Value)
                {
                    try { File.Move(file, UniquePath(target, Path.GetFileName(file))); count++; }
                    catch { }
                }
                if (count > 0) { if (summary.Length > 0) summary.Append("、"); summary.Append(entry.Key).Append(" ").Append(count).Append(" 个"); }
            }
            return summary.Length > 0 ? summary.ToString() : "什么都没动";
        }

        private static string UniquePath(string folder, string name)
        {
            string candidate = Path.Combine(folder, name);
            if (!File.Exists(candidate)) return candidate;
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int n = 2; n < 10000; n++)
            {
                candidate = Path.Combine(folder, stem + "-" + n + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(folder, Guid.NewGuid().ToString("N") + ext);
        }

        // ================= 截图 =================
        internal static string CaptureScreen(string folder)
        {
            return CaptureRegion(SystemInformation.VirtualScreen, folder);
        }

        internal static string CaptureRegion(Rectangle region, string folder)
        {
            region = Rectangle.Intersect(region, SystemInformation.VirtualScreen);
            if (region.Width < 8 || region.Height < 8) throw new ArgumentException("选区太小啦。");
            string path = UniquePath(folder, "截图 " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
            using (Bitmap shot = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
                }
                shot.Save(path, ImageFormat.Png);
            }
            return path;
        }

        internal static string NewRecordingPath()
        {
            return UniquePath(DesktopFolder, "屏幕录制 " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".mp4");
        }

        internal static Color ColorAt(Point point)
        {
            using (Bitmap pixel = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(pixel))
                {
                    g.CopyFromScreen(point, Point.Empty, new Size(1, 1), CopyPixelOperation.SourceCopy);
                }
                return pixel.GetPixel(0, 0);
            }
        }

        internal static string Hex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        // ================= 系统状态播报 =================
        internal static string StatusReport()
        {
            List<string> parts = new List<string>();
            MEMORYSTATUSEX memory = new MEMORYSTATUSEX();
            memory.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memory))
                parts.Add("内存用了 " + memory.dwMemoryLoad + "%，还空着 " + Giga(memory.ullAvailPhys) + "G");
            try
            {
                DriveInfo system = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if (system.IsReady)
                    parts.Add(system.Name.TrimEnd('\\') + " 盘还剩 " + Giga((ulong)system.AvailableFreeSpace) + "G（共 " + Giga((ulong)system.TotalSize) + "G）");
            }
            catch { }
            try
            {
                PowerStatus power = SystemInformation.PowerStatus;
                if (power.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery &&
                    power.BatteryChargeStatus != BatteryChargeStatus.Unknown && power.BatteryLifePercent >= 0)
                    parts.Add("电量 " + (int)(power.BatteryLifePercent * 100) + "%" + (power.PowerLineStatus == PowerLineStatus.Online ? "，正在充电" : "，没插电"));
            }
            catch { }
            if (parts.Count == 0) return "这次没能读到系统状态，稍后再试试。";
            return "我看了看：" + String.Join("；", parts.ToArray()) + "。";
        }

        private static string Giga(ulong bytes) { return (bytes / 1073741824.0).ToString("0.0"); }
        internal static string Mega(long bytes) { return (bytes / 1048576.0).ToString("0.#") + " MB"; }

        // ================= 锁屏 / 打开文件夹 =================
        internal static void LockScreen() { LockWorkStation(); }

        internal static void OpenFolder(string path)
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("这个文件夹不存在。");
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\""));
        }

        internal static string DesktopFolder { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        internal static string DownloadsFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); } }
        internal static string DocumentsFolder { get { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } }
        internal static string PicturesFolder { get { return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); } }

        // ================= 互操作 =================
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO { public uint cbSize; public long i64Size; public long i64NumItems; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile;
            public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr window, string root, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();
    }

    /// <summary>全屏取色：只显示十字光标，点哪取哪。</summary>
    internal sealed class PickColorOverlay : Form
    {
        private readonly PetWindow pet;
        private readonly System.Windows.Forms.Timer delay = new System.Windows.Forms.Timer { Interval = 150 };
        private Point picked;

        internal PickColorOverlay(PetWindow pet)
        {
            this.pet = pet;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = SystemInformation.VirtualScreen;
            StartPosition = FormStartPosition.Manual;
            TopMost = true; ShowInTaskbar = false; KeyPreview = true;
            BackColor = Color.FromArgb(30, 20, 50);
            Opacity = 0.12;                       // 尽量淡，取色前还会先隐藏自己
            Cursor = Cursors.Cross;
            Text = "萌栖屏幕取色（Esc 取消）";
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Cancel(); };
            MouseClick += OnClick;
            delay.Tick += delegate
            {
                delay.Stop();
                Color color;
                try { color = SystemTools.ColorAt(picked); }
                catch { Close(); return; }
                Close();
                pet.ReportColor(color);
            };
        }

        private void Cancel()
        {
            Close();
            pet.PlayAction(3, 2200);
            pet.Speak("不取色了，回来陪你。", 3000);
        }

        private void OnClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            picked = Cursor.Position;
            Hide();                               // 先把自己藏掉，否则会把遮罩颜色一起取进去
            Cursor = Cursors.Default;
            delay.Start();
        }

        protected override bool ShowWithoutActivation { get { return false; } }
        protected override void Dispose(bool disposing)
        {
            if (disposing) delay.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>新建文件/文件夹时向用户要个名字的小气泡；后缀可下拉选也可以自己填。</summary>
    internal sealed class CreateInputDialog : RoundedForm
    {
        private static readonly string[] CommonSuffixes = { ".txt", ".md", ".docx", ".xlsx", ".pptx", ".pdf", ".csv", ".json", ".html", ".png", ".jpg", ".gif", ".mp4", ".zip", ".py", ".cs", ".bat", ".ini" };
        private readonly TextBox input = new TextBox();
        private readonly ComboBox suffix = new ComboBox();
        private readonly Action<string> accept;
        private readonly Action cancel;
        private readonly bool folderMode;
        private bool answered;

        internal CreateInputDialog(string title, string hint, string suggestion, bool isFolder, Action<string> onAccept, Action onCancel)
        {
            accept = onAccept; cancel = onCancel; folderMode = isFolder;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(250, 247, 255);
            Font = new Font("Microsoft YaHei UI", 10);
            CornerRadius = 18;
            bool pickSuffix = !isFolder;                          // 文件夹没有后缀，别把这一栏摆出来
            Size = new Size(340, pickSuffix ? 180 : 158);
            Label head = new Label { Text = title, Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(73, 54, 103), Bounds = new Rectangle(18, 12, 304, 30) };
            input.Bounds = new Rectangle(18, 46, 304, 28);
            input.Font = new Font("Microsoft YaHei UI", 10);
            input.BorderStyle = BorderStyle.FixedSingle;
            input.BackColor = Color.White;
            input.Text = suggestion;
            int noteY, buttonY;
            if (pickSuffix)
            {
                Label suffixLabel = new Label { Text = "后缀", ForeColor = Color.FromArgb(73, 54, 103), Bounds = new Rectangle(18, 82, 44, 26) };
                suffix.Bounds = new Rectangle(66, 79, 256, 28);
                suffix.DropDownStyle = ComboBoxStyle.DropDown;      // 可下拉选，也可以自己敲
                suffix.Items.AddRange(CommonSuffixes);
                suffix.Text = "";
                Controls.Add(suffixLabel); Controls.Add(suffix);
                noteY = 112; buttonY = 140;
            }
            else { noteY = 84; buttonY = 112; }
            Label note = new Label { Text = hint, Font = new Font("Microsoft YaHei UI", 9), ForeColor = Color.FromArgb(146, 124, 176), Bounds = new Rectangle(18, noteY, 304, 22) };
            Button create = Ui.Button("创建", delegate { Accept(); });
            Button keep = Ui.Button("取消", delegate { Cancel(); });
            create.Location = new Point(138, buttonY); keep.Location = new Point(242, buttonY);
            create.BackColor = Color.FromArgb(255, 214, 96); create.ForeColor = Color.FromArgb(73, 54, 103);
            input.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; e.Handled = true; Accept(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; e.Handled = true; Cancel(); }
            };
            Controls.Add(head); Controls.Add(input); Controls.Add(note); Controls.Add(create); Controls.Add(keep);
        }

        internal void PositionAbove(PetWindow pet)
        {
            Rectangle area = Screen.FromControl(pet).WorkingArea;
            int x = pet.Left + pet.Width / 2 - Width / 2;
            int y = pet.Top - Height - 10;
            if (y < area.Top) y = Math.Min(area.Bottom - Height, pet.Bottom + 10);
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), y);
        }

        private void Accept()
        {
            if (answered) return;
            answered = true;
            string value = SystemOps.ComposeName(input.Text, folderMode, suffix.Text);
            Close();
            if (accept != null) accept(value);
        }

        private void Cancel()
        {
            if (answered) return;
            answered = true;
            Close();
            if (cancel != null) cancel();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.Focus();
            input.SelectAll();
        }
    }

    /// <summary>截屏时那一记"咔嚓"的白光。</summary>
    internal sealed class FlashLayer : Form
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 20 };
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private const int TotalMs = 320;

        private FlashLayer()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true; ShowInTaskbar = false;
            BackColor = Color.White;
            Opacity = 0.62;
            timer.Tick += delegate
            {
                double t = watch.ElapsedMilliseconds / (double)TotalMs;
                if (t >= 1) { timer.Stop(); Close(); return; }
                Opacity = 0.62 * (1 - t);
            };
        }

        internal static void Launch()
        {
            FlashLayer layer = new FlashLayer();
            layer.Show();
            layer.timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00000080 | 0x08000000 | 0x00000020; return cp; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
