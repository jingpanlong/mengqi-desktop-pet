using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mengqi
{
    /// <summary>
    /// 流式写 GIF89a 动图：每帧自带 256 色局部调色板（按出现频次取色、均值还原），纯托管 LZW。
    /// 不依赖系统编解码器，录出来的文件微信/QQ 里可以直接发。
    /// </summary>
    internal sealed class GifWriter : IDisposable
    {
        private readonly FileStream stream;
        private readonly int[] hist = new int[32768];
        private readonly long[] sumR = new long[32768];
        private readonly long[] sumG = new long[32768];
        private readonly long[] sumB = new long[32768];
        private readonly byte[] lut = new byte[32768];
        private readonly byte[] palette = new byte[256 * 3];
        private readonly List<int> used = new List<int>();
        private readonly int[] dict = new int[4096 * 256];
        private bool disposed;

        internal GifWriter(string path, int width, int height, int loopCount)
        {
            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            stream.Write(Encoding.ASCII.GetBytes("GIF89a"), 0, 6);
            WriteShort(width); WriteShort(height);
            stream.WriteByte(0x70);          // 无全局色表，颜色深度 7
            stream.WriteByte(0); stream.WriteByte(0);
            // NETSCAPE2.0 循环扩展
            stream.Write(new byte[] { 0x21, 0xFF, 0x0B }, 0, 3);
            stream.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"), 0, 11);
            stream.Write(new byte[] { 0x03, 0x01, (byte)(loopCount & 0xFF), (byte)((loopCount >> 8) & 0xFF), 0x00 }, 0, 5);
        }

        internal void AddFrame(Bitmap frame, int delayCentiseconds)
        {
            if (disposed) throw new ObjectDisposedException("GifWriter");
            int delay = Math.Max(2, Math.Min(6000, delayCentiseconds));
            int count;
            byte[] indices = Quantize(frame, out count);
            // 图形控制扩展：21 F9 04 | 4 字节数据(处置方式/延时2字节/透明色索引) | 00 子块结束
            stream.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04, (byte)(delay & 0xFF), (byte)((delay >> 8) & 0xFF), 0x00, 0x00 }, 0, 8);
            stream.WriteByte(0x2C);
            WriteShort(0); WriteShort(0); WriteShort(frame.Width); WriteShort(frame.Height);
            stream.WriteByte(0x87);          // 局部色表 256 项
            stream.Write(palette, 0, 768);
            stream.WriteByte(8);             // LZW 最小码长
            byte[] data = Lzw(indices);
            for (int i = 0; i < data.Length; i += 255)
            {
                int chunk = Math.Min(255, data.Length - i);
                stream.WriteByte((byte)chunk);
                stream.Write(data, i, chunk);
            }
            stream.WriteByte(0);             // 子块结束
        }

        /// <summary>统计出现过的颜色，按频次取前 256 个当调色板，其余映射到最接近的一项。</summary>
        private byte[] Quantize(Bitmap frame, out int paletteCount)
        {
            int width = frame.Width, height = frame.Height;
            BitmapData data = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = Math.Abs(data.Stride);
            byte[] pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            frame.UnlockBits(data);

            Array.Clear(hist, 0, hist.Length);
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + (x << 2);
                    int key = ((pixels[i + 2] >> 3) << 10) | ((pixels[i + 1] >> 3) << 5) | (pixels[i] >> 3);
                    hist[key]++; sumR[key] += pixels[i + 2]; sumG[key] += pixels[i + 1]; sumB[key] += pixels[i];
                }
            }
            used.Clear();
            for (int key = 0; key < 32768; key++) if (hist[key] > 0) used.Add(key);
            used.Sort(delegate(int a, int b) { return hist[b].CompareTo(hist[a]); });
            paletteCount = Math.Min(256, used.Count);
            for (int i = 0; i < paletteCount; i++)
            {
                int key = used[i], total = hist[key];
                palette[i * 3] = (byte)Math.Min(255, (sumR[key] + total / 2) / total);
                palette[i * 3 + 1] = (byte)Math.Min(255, (sumG[key] + total / 2) / total);
                palette[i * 3 + 2] = (byte)Math.Min(255, (sumB[key] + total / 2) / total);
                lut[key] = (byte)i;
            }
            for (int j = paletteCount; j < used.Count; j++)
            {
                int key = used[j];
                int r = ((key >> 10) << 3) + 4, g = (((key >> 5) & 31) << 3) + 4, b = ((key & 31) << 3) + 4;
                int best = 0, bestDistance = int.MaxValue;
                for (int i = 0; i < paletteCount; i++)
                {
                    int dr = palette[i * 3] - r, dg = palette[i * 3 + 1] - g, db = palette[i * 3 + 2] - b;
                    int distance = dr * dr + dg * dg + db * db;
                    if (distance < bestDistance) { bestDistance = distance; best = i; }
                }
                lut[key] = (byte)best;
            }
            byte[] indices = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride, outRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + (x << 2);
                    indices[outRow + x] = lut[((pixels[i + 2] >> 3) << 10) | ((pixels[i + 1] >> 3) << 5) | (pixels[i] >> 3)];
                }
            }
            return indices;
        }

        /// <summary>GIF 变长 LZW：首码 Clear、尾码 EOI，表满 4096 重新 Clear，码长 9→12。</summary>
        private byte[] Lzw(byte[] indices)
        {
            List<byte> output = new List<byte>(indices.Length + 64);
            int bitBuffer = 0, bitCount = 0, codeSize = 9, next = 258;
            Array.Clear(dict, 0, dict.Length);
            bool first = true;
            int current = indices[0];
            for (int i = 1; i < indices.Length; i++)
            {
                int symbol = indices[i];
                int key = (current << 8) | symbol;
                int found = dict[key];
                if (found != 0) { current = found - 1; continue; }
                Emit(output, ref bitBuffer, ref bitCount, current, codeSize);
                if (first) { first = false; current = symbol; continue; }   // Clear 后的第一个码不建表
                if (next < 4096)
                {
                    dict[key] = next + 1; next++;
                    if (next == (1 << codeSize) && codeSize < 12) codeSize++;
                    current = symbol; continue;
                }
                Emit(output, ref bitBuffer, ref bitCount, 256, codeSize);  // 表满，发 Clear
                Array.Clear(dict, 0, dict.Length);
                next = 258; codeSize = 9; first = true; current = symbol;
            }
            Emit(output, ref bitBuffer, ref bitCount, current, codeSize);
            // 解码器读到这条后还会补一个表项，可能把码长撑大，EOI 要跟上
            if (!first && next < 4096) { next++; if (next == (1 << codeSize) && codeSize < 12) codeSize++; }
            Emit(output, ref bitBuffer, ref bitCount, 257, codeSize);
            if (bitCount > 0) output.Add((byte)(bitBuffer & 0xFF));
            return output.ToArray();
        }

        private static void Emit(List<byte> output, ref int bitBuffer, ref int bitCount, int code, int codeSize)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                output.Add((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        private void WriteShort(int value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stream.WriteByte(0x3B);          // 结束符
            stream.Flush();
            stream.Dispose();
        }
    }
}
