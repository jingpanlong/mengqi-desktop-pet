using System;
using System.Drawing;

namespace Mengqi
{
    /// <summary>视频写入器统一接口：记录一帧、已写帧数、收尾落盘。
    /// 让录屏模块不必关心底层到底是 ffmpeg 还是系统自带的 Media Foundation。</summary>
    internal interface IVideoSink : IDisposable
    {
        void AddFrame(Bitmap frame);
        int Frames { get; }
    }

    /// <summary>
    /// 挑选视频编码后端：优先用本机的 ffmpeg（画质更好、编码更快，也不受系统编解码组件状态影响）；
    /// 机器上没有 ffmpeg 时，退回 Windows 自带的 Media Foundation（Win8+ 免安装）。
    /// </summary>
    internal static class VideoSinkFactory
    {
        internal static IVideoSink Start(int width, int height, int fps, string path)
        {
            string ffmpeg = FfmpegLocator.Path;
            if (ffmpeg != null)
            {
                try { return FfmpegEncoder.Start(width, height, fps, path); }
                catch (Exception ffmpegError)
                {
                    try { return VideoEncoder.Start(width, height, fps, path); }
                    catch (Exception mfError)
                    {
                        throw new InvalidOperationException(
                            "视频编码没能启动。ffmpeg：" + ffmpegError.Message + "；系统编解码器：" + mfError.Message);
                    }
                }
            }
            return VideoEncoder.Start(width, height, fps, path);
        }

        /// <summary>当前会用的后端名，用于提示与自检。</summary>
        internal static string BackendName
        {
            get { return FfmpegLocator.Path != null ? "ffmpeg" : "Media Foundation"; }
        }
    }
}
