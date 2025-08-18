using System;
using System.Runtime.CompilerServices;
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

        public float Median()
        {
            if (_count == 0) return 0;
            var tmp = new int[_count];
            Array.Copy(_buf, tmp, _count);
            Array.Sort(tmp);
            int mid = _count / 2;
            return (_count % 2 == 0) ? 0.5f * (tmp[mid - 1] + tmp[mid]) : tmp[mid];
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
    public int FramesCollected { get; private set; }

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
        lock (_depthLock)
        {
            Parallel.For(0, W * H, (i) =>
            {
                int x = MinX + i % W;
                int y = MinY + i / H;
                int index = y * frameW + x;
                _cells[i].AddByRing(depthData[index]);
            });  
        }
        FramesCollected++;
    }

    // Итоговая «чистая» глубина окна
    public void BuildDepthMap(float[] outDepth, bool useMedian, float trimmedFrac = 0.2f)
    {
        // outDepth.Length == W*H
        for (int i = 0; i < _cells.Length; i++)
            outDepth[i] = useMedian ? _cells[i].Median() : _cells[i].TrimmedMean(trimmedFrac);
    }
}


}
