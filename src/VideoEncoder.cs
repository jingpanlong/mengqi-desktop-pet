using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Mengqi
{
    /// <summary>
    /// 用 Windows Media Foundation 把一序列位图编码成 H.264 / MP4 视频。
    /// 不依赖任何外部程序，Windows 8+（含 Win10 / 11）自带；比 GIF 清晰、体积小，可直接在微信 / 文档里发送。
    /// 输入位图统一转成 NV12（H.264 编码器的通用输入格式）。
    /// </summary>
    internal sealed class VideoEncoder : IDisposable, IVideoSink
    {
        // ---- 属性主键（GUID）与子类型（用 ref 传入 COM，故不能为 readonly）----
        private static Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
        private static Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72E294E");
        private static Guid MF_MT_AVG_BITRATE = new Guid("203326DE-6992-4B2B-9B07-2743F4EC6115");
        private static Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
        private static Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
        private static Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
        private static Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
        private static Guid MF_MT_MPEG2_PROFILE = new Guid("AD76A80B-2D5C-4E0B-B375-64E520137036");
        private static Guid MF_MT_DEFAULT_STRIDE = new Guid("644B94E0-08FE-42B9-A15D-D7D0FAE32695");
        private static Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        private static Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        private static Guid MFVideoFormat_NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");

        private const int MFVideoInterlace_Progressive = 2;
        private const int eAVEncH264VProfile_Baseline = 66;

        private static bool startupDone;
        private static readonly object startupLock = new object();

        private readonly int width;
        private readonly int height;
        private readonly int fps;
        private readonly long frameDuration;          // 每帧时长（100 纳秒）
        private readonly byte[] nv12;
        private long clock;                            // 已写入的时长（100 纳秒）
        private int frames;
        private readonly object sinkLock = new object();
        private bool finalized;
        private readonly string pathPrivate;           // 输出文件路径（首帧时才真正打开 SinkWriter）

        private IMFSinkWriter sink;
        private int streamIndex;

        private VideoEncoder(int width, int height, int fps, string path)
        {
            // H.264 / NV12 要求偶数宽高，先规整，避免色度面错位
            if ((width & 1) == 1) width++;
            if ((height & 1) == 1) height++;
            this.width = width;
            this.height = height;
            this.fps = fps;
            this.pathPrivate = path;
            this.frameDuration = (long)(10000000.0 / fps + 0.5);
            nv12 = new byte[width * height + width * height / 2];   // NV12：Y + 交错 UV
        }

        /// <summary>尝试启动 Media Foundation；不可用时抛出可读异常。</summary>
        internal static void RequireAvailable()
        {
            if (MFStartup(0x00010070, 0) != 0)
                throw new InvalidOperationException("本机未提供 Media Foundation 视频编码组件，无法录制视频。");
            if (MFCreateSinkWriterFromURL == null)
                throw new InvalidOperationException("本机缺少 Media Foundation 的 MP4 录制组件（MFCreateSinkWriterFromURL），无法录制视频。");
        }

        internal static VideoEncoder Start(int width, int height, int fps, string path)
        {
            lock (startupLock)
            {
                if (!startupDone)
                {
                    RequireAvailable();
                    startupDone = true;
                }
            }
            return new VideoEncoder(width, height, fps, path);
        }

        /// <summary>写入一帧（RGBA 位图会被缩放到编码尺寸并转 NV12；尺寸一致时直接编码，省一次拷贝）。</summary>
        internal void AddFrame(Bitmap frame)
        {
            if (finalized) throw new ObjectDisposedException("VideoEncoder");
            if (frame.Width == width && frame.Height == height)
            {
                RgbToNv12(frame, nv12, width, height);
            }
            else
            {
                using (Bitmap scaled = ScaleTo(frame, width, height))
                    RgbToNv12(scaled, nv12, width, height);
            }
            WriteNv12(nv12, width, height, clock, frameDuration);
            clock += frameDuration;
            frames++;
        }

        internal int Frames { get { return frames; } }

        // IVideoSink 显式实现：让录屏模块可以无差别地在 ffmpeg / Media Foundation 之间切换。
        int IVideoSink.Frames { get { return frames; } }
        void IVideoSink.AddFrame(Bitmap frame) { AddFrame(frame); }

        private void WriteNv12(byte[] data, int w, int h, long time, long duration)
        {
            lock (sinkLock)
            {
                if (sink == null)
                {
                    if (MFCreateSinkWriterFromURL == null)
                        throw new InvalidOperationException("本机缺少 Media Foundation 的 MP4 录制组件（MFCreateSinkWriterFromURL）。");
                    IMFSinkWriter writer;
                    Hr(MFCreateSinkWriterFromURL(pathPrivate, IntPtr.Zero, IntPtr.Zero, out writer), "MFCreateSinkWriterFromURL");
                    sink = writer;
                    IMFMediaType outType = OutputType();
                    Hr(sink.AddStream(outType, out streamIndex), "AddStream");
                    IMFMediaType inType = InputType();
                    Hr(sink.SetInputMediaType(streamIndex, inType, IntPtr.Zero), "SetInputMediaType");
                    Hr(sink.BeginWriting(), "BeginWriting");
                }
                int bufferSize = data.Length;
                IMFMediaBuffer buffer;
                Hr(MFCreateMemoryBuffer(bufferSize, out buffer), "MFCreateMemoryBuffer");
                IntPtr ptr; int maxLen; int curLen;
                Hr(buffer.Lock(out ptr, out maxLen, out curLen), "buffer.Lock");
                Marshal.Copy(data, 0, ptr, bufferSize);
                Hr(buffer.SetCurrentLength(bufferSize), "buffer.SetCurrentLength");
                Hr(buffer.Unlock(), "buffer.Unlock");
                IMFSample sample;
                Hr(MFCreateSample(out sample), "MFCreateSample");
                Hr(sample.SetSampleTime(time), "sample.SetSampleTime");
                Hr(sample.SetSampleDuration(duration), "sample.SetSampleDuration");
                Hr(sample.AddBuffer(buffer), "sample.AddBuffer");
                Hr(sink.WriteSample(streamIndex, sample), "WriteSample");
                Marshal.ReleaseComObject(sample);
                Marshal.ReleaseComObject(buffer);
            }
        }

        private void Finish()
        {
            lock (sinkLock)
            {
                if (sink != null && !finalized)
                {
                    finalized = true;
                    Hr(sink.FinalizeSink(), "FinalizeSink");
                    Marshal.ReleaseComObject(sink);
                    sink = null;
                }
            }
        }

        private IMFMediaType OutputType()
        {
            IMFMediaType type;
            Hr(MFCreateMediaType(out type), "MFCreateMediaType(out)");
            IMFAttributes a = (IMFAttributes)type;   // 属性方法在 IMFAttributes 上，需单独 QI
            Hr(a.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), "out.SetGUID(MAJOR_TYPE)");
            Hr(a.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264), "out.SetGUID(SUBTYPE=H264)");
            int bitrate = (int)Math.Min(10000000L, Math.Max(500000L, (long)width * height * fps / 20));
            Hr(a.SetUINT32(MF_MT_AVG_BITRATE, bitrate), "out.SetUINT32(AVG_BITRATE)");
            Hr(a.SetUINT64(MF_MT_FRAME_SIZE, ((long)width << 32) | (uint)height), "out.SetUINT64(FRAME_SIZE)");
            Hr(a.SetUINT64(MF_MT_FRAME_RATE, ((long)fps << 32) | 1), "out.SetUINT64(FRAME_RATE)");
            Hr(a.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, ((long)1 << 32) | 1), "out.SetUINT64(PAR)");
            Hr(a.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive), "out.SetUINT32(INTERLACE)");
            Hr(a.SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Baseline), "out.SetUINT32(MPEG2_PROFILE)");
            return type;
        }

        private IMFMediaType InputType()
        {
            IMFMediaType type;
            Hr(MFCreateMediaType(out type), "MFCreateMediaType(in)");
            IMFAttributes a = (IMFAttributes)type;
            Hr(a.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), "in.SetGUID(MAJOR_TYPE)");
            Hr(a.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12), "in.SetGUID(SUBTYPE=NV12)");
            Hr(a.SetUINT64(MF_MT_FRAME_SIZE, ((long)width << 32) | (uint)height), "in.SetUINT64(FRAME_SIZE)");
            Hr(a.SetUINT64(MF_MT_FRAME_RATE, ((long)fps << 32) | 1), "in.SetUINT64(FRAME_RATE)");
            Hr(a.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive), "in.SetUINT32(INTERLACE)");
            Hr(a.SetUINT32(MF_MT_DEFAULT_STRIDE, width), "in.SetUINT32(DEFAULT_STRIDE)");
            return type;
        }

        // ---- RGBA -> NV12（BT.601），纯托管（与 GifEncoder 一样用 Marshal.Copy，不依赖 /unsafe）----
        private static void RgbToNv12(Bitmap source, byte[] nv12, int w, int h)
        {
            BitmapData data = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * h];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                int yStride = w;
                int uvStride = w / 2;
                int uvBase = w * h;
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int outRow = y * yStride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 4;
                        int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                        int yv = 16 + ((65 * r + 129 * g + 25 * b) >> 8);
                        nv12[outRow + x] = Clamp(yv);
                    }
                }
                for (int y = 0; y < h; y += 2)
                {
                    for (int x = 0; x < w; x += 2)
                    {
                        int r = 0, g = 0, b = 0;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                int i = (y + dy) * stride + (x + dx) * 4;
                                b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2];
                            }
                        r >>= 2; g >>= 2; b >>= 2;
                        int u = 128 + ((-38 * r - 75 * g + 112 * b) >> 8);
                        int v = 128 + ((112 * r - 94 * g - 18 * b) >> 8);
                        int idx = y / 2 * uvStride + x / 2;
                        nv12[uvBase + idx * 2] = Clamp(u);
                        nv12[uvBase + idx * 2 + 1] = Clamp(v);
                    }
                }
            }
            finally { source.UnlockBits(data); }
        }

        private static byte Clamp(int v) { return v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v; }

        private static Bitmap ScaleTo(Bitmap source, int w, int h)
        {
            if (source.Width == w && source.Height == h) return new Bitmap(source);
            Bitmap target = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, w, h);
            }
            return target;
        }

        public void Dispose() { Finish(); GC.SuppressFinalize(this); }

        // ---- Media Foundation P/Invoke ----
        // MFStartup / MFCreateMediaType / MFCreateSample / MFCreateMemoryBuffer 稳定导出在 mfplat.dll；
        // 但 MFCreateSinkWriterFromURL 在不同 Windows 版本里时而在 mfplat、时而在 mfreadwrite/mf，
        // 故运行时跨候选 DLL 动态解析，避免“找不到入口点”。
        [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IMFMediaType ppMediaType);
        [DllImport("mfplat.dll")] private static extern int MFCreateSample(out IMFSample ppSample);
        [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL, IntPtr pByteStream, IntPtr pAttributes, out IMFSinkWriter ppSinkWriter);
        private static CreateSinkWriterFromURL MFCreateSinkWriterFromURL;

        static VideoEncoder()
        {
            foreach (string dll in new[] { "mfreadwrite.dll", "mfplat.dll", "mf.dll", "mfsrcsnk.dll" })
            {
                IntPtr h = LoadLibrary(dll);
                if (h == IntPtr.Zero) continue;
                IntPtr p = GetProcAddress(h, "MFCreateSinkWriterFromURL");
                if (p != IntPtr.Zero)
                {
                    MFCreateSinkWriterFromURL = (CreateSinkWriterFromURL)Marshal.GetDelegateForFunctionPointer(p, typeof(CreateSinkWriterFromURL));
                    break;
                }
            }
        }

        private static void Hr(int code)
        {
            if (code < 0) throw new InvalidOperationException("Media Foundation 操作失败：0x" + code.ToString("X8"));
        }

        private static void Hr(int code, string step)
        {
            if (code < 0) throw new InvalidOperationException("Media Foundation 操作失败(" + step + ")：0x" + code.ToString("X8"));
        }

        // IMFAttributes —— 槽位顺序经 Set/Get 往返实测校准（见注释）。
        // 重要：真实 vtable 中【没有】独立的 LockStore/UnlockStore/GetCount/GetItemByIndex/CopyAllItems 槽位
        //（与 NAudio 等成熟实现一致）。若把它们写进接口，后续方法槽位会整体后移，
        // 调用 SetUINT32/SetGUID 时会落到别的函数上，触发 AccessViolationException。
        [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFAttributes
        {
            [PreserveSig] int GetItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [In, Out] IntPtr pValue);
            [PreserveSig] int GetItemType([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out int pType);
            [PreserveSig] int CompareItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] object pTheirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int GetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, int cbBufSize, out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out IntPtr ip, out int pcbSize);
            [PreserveSig] int GetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int SetItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr Value);
            [PreserveSig] int DeleteItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, long unValue);
            [PreserveSig] int SetDouble([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, double fValue);
            [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [In, MarshalAs(UnmanagedType.LPStruct)] Guid guidValue);
            [PreserveSig] int SetString([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [In, MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, int cbBufSize);
            [PreserveSig] int SetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [In, MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        }

        [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFMediaType
        {
            void GetMajorType(out Guid g); void IsCompressedFormat(out int v); void IsEqual(IntPtr t, out int r); void GetRepresentation(Guid g, out IntPtr p);
        }

        [ComImport, Guid("045FA593-8799-4861-9BE9-23B0153C2259"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
            [PreserveSig] int SetCurrentLength(int cbCurrentLength);
            [PreserveSig] int GetMaxLength(out int pcbMaxLength);
        }

        [ComImport, Guid("C7AFFAF4-7D2C-4E1B-AFE8-2C3B321D93C4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFSample
        {
            [PreserveSig] int GetSampleFlags(out uint pdwFlags);
            [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
            [PreserveSig] int GetSampleTime(out long phnsSampleTime);
            [PreserveSig] int SetSampleTime(long hnsSampleTime);
            [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
            [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
            [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
            [PreserveSig] int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
            [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
            [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
            [PreserveSig] int RemoveAllBuffers();
            [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
            [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);
        }

        [ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFSinkWriter
        {
            [PreserveSig] int AddStream(IMFMediaType pMediaType, out int pdwStreamIndex);
            [PreserveSig] int SetInputMediaType(int dwStreamIndex, IMFMediaType pInputMediaType, IntPtr pEncodingParameters);
            [PreserveSig] int BeginWriting();
            [PreserveSig] int WriteSample(int dwStreamIndex, IMFSample pSample);
            [PreserveSig] int SendStreamTick(int dwStreamIndex, long llTimestamp);
            [PreserveSig] int PlaceMarker(int dwStreamIndex, IntPtr pvContext);
            [PreserveSig] int NotifyEndOfSegment(int dwStreamIndex);
            [PreserveSig] int Flush(int dwStreamIndex);
            [PreserveSig] int FinalizeSink();
            [PreserveSig] int GetServiceForStream(int dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
            [PreserveSig] int GetStatistics(int dwStreamIndex, IntPtr pStats);
        }
    }
}
