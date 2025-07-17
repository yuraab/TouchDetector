using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace CPRTouchVision.Models
{
    internal class DepthVisualizer
    {
        private int _fw;
        private int _fh;
        private int _fsize;

        public DepthVisualizer(int width, int height)
        {
            _fw = width;
            _fh = height;
            _fsize = width * height;
        }

        public SKColor[] Update(ushort[] data)
        {
            var result = new SKColor[data.Length];

            Parallel.For(0, _fsize, (i) =>
            {
                result[i] = DepthToBGRA(data[i]);
            });

            return result;
        }

        private SKColor DepthToBGRA(ushort value)
        {
            if (value == 0)
                return SKColors.Transparent;
            var clamped = Math.Clamp(value, MinDepth, MaxDepth);
            var fraction = (float)(clamped - MinDepth) / (MaxDepth - MinDepth);
            var hue = fraction * 240.0f;
            return SKColor.FromHsv(hue, 100, 100, 70);
        }

        const ushort MinDepth = 500;
        const ushort MaxDepth = 4500;
    }
}
