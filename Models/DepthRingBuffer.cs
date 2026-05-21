using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class DepthRingBuffer
    {
        private readonly ushort[] _buf;
        private int _idx = 0;
        private int _count = 0;

        public DepthRingBuffer(int capacity) => _buf = new ushort[capacity];

        public int Count => _count;

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddByRing(ushort d)
        {
            if (d <= 0) return;
            _buf[_idx] = d;
            _idx = (_idx + 1) % _buf.Length;
            if (_count < _buf.Length) _count++;
        }

        public ushort Median()
        {
            if (_count == 0) return 0;
            var tmp = new int[_count];
            Array.Copy(_buf, tmp, _count);
            Array.Sort(tmp);
            int mid = _count / 2;
            return (_count % 2 == 0) ? (ushort)(0.5f * (tmp[mid - 1] + tmp[mid])) : (ushort)tmp[mid];
        }

        public float TrimmedMean(float trimFrac = 0.2f)
        {
            if (_count == 0) return 0;
            var tmp = new float[_count];
            Array.Copy(_buf, tmp, _count);
            Array.Sort(tmp);
            int trim = (int)(_count * trimFrac);
            int start = trim;
            int end = _count - trim;
            if (end <= start) return Median();
            float sum = 0;
            int n = 0;
            for (int i = start; i < end; i++) { sum += tmp[i]; n++; }
            return n > 0 ? sum / n : 0;
        }
    }

    public sealed class WindowAccumulator
    {
        public readonly int MinX, MinY, W, H, frameW, frameH;
        private readonly object _depthLock = new object();
        private readonly DepthRingBuffer[] _cells;

        public int CapacityPerPixel { get; }

        private int _framesCollected;
        public int FramesCollected => _framesCollected;

        public WindowAccumulator(int minX, int minY, int w, int h, int fW, int fH, int capacityPerPixel)
        {
            MinX = minX;
            MinY = minY;
            W = w;
            H = h;
            frameW = fW;
            frameH = fH;
            CapacityPerPixel = capacityPerPixel;
            _cells = new DepthRingBuffer[W * H];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = new DepthRingBuffer(capacityPerPixel);
        }

        public void AddFrame(ushort[] depthData)
        {
            Parallel.For(0, H, y =>
            {
                int srcRow = (MinY + y) * frameW + MinX;
                int dstRow = y * W;

                for (int x = 0; x < W; x++)
                {
                    ushort d = depthData[srcRow + x];
                    _cells[dstRow + x].AddByRing(d);
                }
            });

            Interlocked.Increment(ref _framesCollected);
        }

        // Final «clear» depth map of the window
        public void BuildDepthMap(ushort[] outDepth, bool useMedian, float trimmedFrac = 0.2f)
        {
            // outDepth.Length == W*H
            Parallel.For(0, _cells.Length, i =>
            {
                outDepth[i] = useMedian
                    ? _cells[i].Median()
                    : (ushort)MathF.Round(
                        _cells[i].TrimmedMean(trimmedFrac));
            });
        }

        public ushort[] BuildDepthMap(bool useMedian)
        {
            ushort[] result =
                new ushort[W * H];

            BuildDepthMap(result, useMedian);

            return result;
        }

        public int GetROIWidth() => W;
        public int GetROIHeight() => H;

        public int GetFrameXByRoiX(int x) => MinX + x;
        public int GetFrameYByRoiY(int y) => MinY + y;

        public int GetFrameIndexByRoiIndex(int i)
        {
            int roiX = i % W;
            int roiY = i / W;

            int fullX = MinX + roiX;
            int fullY = MinY + roiY;

            return fullY * frameW + fullX;
        }

    }

}
