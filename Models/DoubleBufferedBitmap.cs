using System;
using SkiaSharp;

namespace CPRTouchVision.Models
{
    internal class DoubleBufferedBitmap : IDisposable
    {
        private SKBitmap _frontBuffer;
        private SKBitmap _backBuffer;
        private readonly object _lock = new object();

        public SKBitmap Value
        {
            get
            {
                lock (_lock)
                {
                    return _frontBuffer;
                }
            }
        }

        public bool IsEmpty
        {
            get
            {
                lock (_lock)
                {
                    return _frontBuffer.GetPixels() == IntPtr.Zero;
                }
            }
        }

        public int Width;
        public int Height;


        public DoubleBufferedBitmap(SKImageInfo imageInfo)
        {
            Width = imageInfo.Width;
            Height = imageInfo.Height;
            _frontBuffer = new SKBitmap(imageInfo);
            _backBuffer = new SKBitmap(imageInfo);
        }

        public void Update(Action<SKBitmap> action)
        {
            action(_backBuffer);
            SwapBuffers();
        }

        private void SwapBuffers()
        {
            lock (_lock)
            {
                var temp = _frontBuffer;
                _frontBuffer = _backBuffer;
                _backBuffer = temp;
            }
        }

        public void Draw(SKCanvas canvas, bool shouldScaleCanvas = false)
        {
            lock (_lock)
            {
                if (shouldScaleCanvas)
                {
                    var sx = (float)canvas.DeviceClipBounds.Width / _frontBuffer.Width;
                    var sy = (float)canvas.DeviceClipBounds.Height / _frontBuffer.Height;
                    canvas.Scale(sx, sy);
                }

                canvas.DrawBitmap(_frontBuffer, 0, 0);
            }
        }

        public void Dispose()
        {
            _frontBuffer?.Dispose();
            _backBuffer?.Dispose();
        }
    }
}
