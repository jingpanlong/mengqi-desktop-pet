using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mengqi
{
    /// <summary>
    /// 在常见的几个位置找本机的 ffmpeg.exe：程序自带目录 → PATH → 常见安装位置 → 各磁盘的 software\ffmpeg*。
    /// 找到就缓存，避免重复扫描。找不到返回 null，录屏会自动退回系统自带的 Media Foundation。
    /// </summary>
    internal static class FfmpegLocator
    {
        private static bool probed;
        private static string cached;

        internal static string Path
        {
            get
            {
                if (!probed) { probed = true; cached = Locate(); }
                return cached;
            }
        }

        private static string Locate()
        {
            // 1) 程序目录（含随程序一起放置的绿色版）
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string rel in new[]
            {
                "ffmpeg.exe",
                @"bin\ffmpeg.exe",
                @"ffmpeg\ffmpeg.exe",
                @"ffmpeg\bin\ffmpeg.exe",
                @"tools\ffmpeg.exe",
                @"tools\ffmpeg\bin\ffmpeg.exe"
            })
            {
                string candidate = SafeCombine(baseDir, rel);
                if (candidate != null && File.Exists(candidate)) return candidate;
            }
            // 2) PATH 环境变量
            string envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (string dir in envPath.Split(';'))
                {
                    string trimmed = dir.Trim();
                    if (trimmed.Length == 0) continue;
                    string candidate = SafeCombine(trimmed, "ffmpeg.exe");
                    if (candidate != null && File.Exists(candidate)) return candidate;
                }
            }
            // 3) 常见安装位置
            foreach (string candidate in CommonLocations())
                if (candidate != null && File.Exists(candidate)) return candidate;
            return null;
        }

        private static IEnumerable<string> CommonLocations()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            yield return SafeCombine(pf, @"ffmpeg\bin\ffmpeg.exe");
            yield return SafeCombine(pf86, @"ffmpeg\bin\ffmpeg.exe");
            yield return SafeCombine(local, @"Microsoft\WinGet\Links\ffmpeg.exe");
            yield return SafeCombine(userProfile, @"scoop\shims\ffmpeg.exe");
            yield return SafeCombine(userProfile, @"scoop\apps\ffmpeg\current\bin\ffmpeg.exe");
            yield return SafeCombine(programData, @"chocolatey\bin\ffmpeg.exe");
            yield return SafeCombine(roaming, @"ffmpeg\bin\ffmpeg.exe");

            // 各磁盘根目录 / software 目录下的绿色版（例如 E:\software\ffmpeg-8.0.1-full_build\bin\ffmpeg.exe）
            foreach (string root in FixedDriveRoots())
            {
                yield return SafeCombine(root, @"ffmpeg\bin\ffmpeg.exe");
                foreach (string found in GlobFfmpeg(root)) yield return found;
            }
        }

        private static IEnumerable<string> FixedDriveRoots()
        {
            List<string> roots = new List<string>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) roots.Add(drive.RootDirectory.FullName); }
                    catch { }
                }
            }
            catch { }
            return roots;
        }

        private static IEnumerable<string> GlobFfmpeg(string root)
        {
            foreach (string parent in new[] { "software", "tools", "apps", "software\\bin" })
            {
                string dir = SafeCombine(root, parent);
                if (dir == null || !Directory.Exists(dir)) continue;
                string[] children;
                try { children = Directory.GetDirectories(dir, "*ffmpeg*"); }
                catch { continue; }
                foreach (string child in children)
                {
                    string candidate = SafeCombine(child, @"bin\ffmpeg.exe");
                    if (candidate != null) yield return candidate;
                    string candidate2 = SafeCombine(child, "ffmpeg.exe");
                    if (candidate2 != null) yield return candidate2;
                }
            }
        }

        private static string SafeCombine(string dir, string rel)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try { return System.IO.Path.Combine(dir, rel); }
            catch { return null; }
        }
    }

    /// <summary>
    /// 用 ffmpeg 把一序列位图编码成 H.264 / MP4。帧以 BGRA 原始像素通过管道喂给 ffmpeg，
    /// 不需要落任何临时图片，启动快、体积小、画质好，微信 / 文档里都能直接发。
    /// </summary>
    internal sealed class FfmpegEncoder : IVideoSink
    {
        private readonly Process process;
        private readonly Stream stdin;
        private readonly int width;
        private readonly int height;
        private readonly byte[] pixels;          // 一帧 BGRA
        private readonly StringBuilder stderr = new StringBuilder();
        private int frames;
        private bool finished;
        private bool broken;

        private FfmpegEncoder(Process process, int width, int height)
        {
            this.process = process;
            this.width = width;
            this.height = height;
            stdin = process.StandardInput.BaseStream;
            pixels = new byte[width * height * 4];
            try
            {
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (stderr)
                    {
                        stderr.AppendLine(e.Data);
                        if (stderr.Length > 4000) stderr.Remove(0, 2000);
                    }
                };
                process.BeginErrorReadLine();
            }
            catch { }
        }

        /// <summary>启动 ffmpeg；失败会抛异常，交给上层回退到 Media Foundation。</summary>
        internal static FfmpegEncoder Start(int width, int height, int fps, string path)
        {
            string exe = FfmpegLocator.Path;
            if (exe == null) throw new InvalidOperationException("本机没有找到 ffmpeg。");
            if ((width & 1) == 1) width++;         // yuv420p 要求偶数边长
            if ((height & 1) == 1) height++;
            if (width < 2 || height < 2) throw new ArgumentException("录制区域太小。");

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = BuildArguments(width, height, fps, path),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            Process started = Process.Start(info);
            if (started == null) throw new InvalidOperationException("没能启动 ffmpeg 进程。");
            try { started.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            return new FfmpegEncoder(started, width, height);
        }

        private static string BuildArguments(int width, int height, int fps, string path)
        {
            // 原始 BGRA 帧从标准输入进来；libx264 恒定质量，yuv420p 保证到处都能播；faststart 方便微信直接预览。
            return string.Format(
                "-y -hide_banner -loglevel error " +
                "-f rawvideo -pixel_format bgra -video_size {0}x{1} -framerate {2} -i - " +
                "-an -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{3}\"",
                width, height, Math.Max(1, fps), path);
        }

        internal int Frames { get { return frames; } }

        internal void AddFrame(Bitmap frame)
        {
            if (finished) throw new ObjectDisposedException("FfmpegEncoder");
            if (broken) return;
            if (frame.Width == width && frame.Height == height) Pack(frame);
            else using (Bitmap scaled = Scale(frame, width, height)) Pack(scaled);
            try
            {
                stdin.Write(pixels, 0, pixels.Length);
                frames++;
            }
            catch (Exception)
            {
                broken = true;   // ffmpeg 提前退出了（参数不对/磁盘满等），后续帧直接丢弃
            }
        }

        int IVideoSink.Frames { get { return frames; } }
        void IVideoSink.AddFrame(Bitmap frame) { AddFrame(frame); }

        /// <summary>把位图读成 BGRA 字节（GDI 的 32bppArgb 内存里就是 B,G,R,A，正好对上 ffmpeg 的 bgra）。</summary>
        private void Pack(Bitmap source)
        {
            BitmapData data = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                if (stride == width * 4)
                {
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                else
                {
                    // 极少见：行末有填充，逐行搬
                    IntPtr row = data.Scan0;
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(row, pixels, y * width * 4, width * 4);
                        row = new IntPtr(row.ToInt64() + stride);
                    }
                }
            }
            finally { source.UnlockBits(data); }
        }

        private static Bitmap Scale(Bitmap source, int width, int height)
        {
            Bitmap target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, width, height);
            }
            return target;
        }

        /// <summary>关闭管道并等 ffmpeg 写完索引（moov），这样视频才是完整可播放的。</summary>
        public void Dispose()
        {
            if (finished) return;
            finished = true;
            try { stdin.Flush(); } catch { }
            try { stdin.Close(); } catch { }
            try
            {
                if (!process.WaitForExit(20000))
                {
                    try { process.Kill(); } catch { }
                }
            }
            catch { }
            try { process.Dispose(); } catch { }
        }

        /// <summary>收尾后如果 ffmpeg 报过错，取回最近的错误输出（用于提示用户）。</summary>
        internal string LastError
        {
            get { lock (stderr) { return stderr.ToString().Trim(); } }
        }
    }
}
