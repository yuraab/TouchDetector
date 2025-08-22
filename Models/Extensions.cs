using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ComputeSharp;
using Microsoft.UI.Xaml;
using OBSharp.BodyTracking;
using SkiaSharp;
using WinUIEx;
using OB = OBSharp.Sensor;

namespace CPRTouchVision.Models
{
    public static class Extensions
    {
        public static bool IsZero(this Vector3 p)
        {
            return p.X == 0f && p.Y == 0f && p.Z == 0f;
        }

        public static SKPoint ToSKPoint(this Vector3 point)
        {
            return new SKPoint(point.X, point.Y);
        }

        public static bool IsPointInsideTriangle(this Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var v0 = c - a;
            var v1 = b - a;
            var v2 = p - a;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float denom = dot00 * dot11 - dot01 * dot01;
            if (denom == 0) return false;

            float u = (dot11 * dot02 - dot01 * dot12) / denom;
            float v = (dot00 * dot12 - dot01 * dot02) / denom;

            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }

        public static void CopyFrom(this ushort[] buffer, OB.Image image)
        {
            if (buffer.Length * sizeof(ushort) != image.SizeBytes)
                throw new ArgumentException("Image buffer size is not matching destination buffer size.");

            unsafe
            {
                ushort* source = (ushort*)image.Buffer.ToPointer();
                long size = buffer.Length * sizeof(ushort);

                fixed (ushort* destination = buffer)
                {
                    Buffer.MemoryCopy(source, destination, size, size);
                }
            }
        }

        public static void CopyFrom(this ReadOnlyBuffer<uint> buffer, ushort[] data)
        {
            Span<uint> casted = MemoryMarshal.Cast<ushort, uint>(data.AsSpan());
            buffer.CopyFrom(casted);
        }

        public static void CopyTo(this ReadWriteBuffer<uint> buffer, SKBitmap bitmap)
        {
            var bytes = MemoryMarshal.AsBytes<uint>(buffer.ToArray()).ToArray();
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        }

        public static Vector3 GetPosition(this Joint joint)
        {
            return new(joint.PositionMm.X, joint.PositionMm.Y, joint.PositionMm.Z);
        }

        public static OBSharp.Float3 ToFloat3(this Vector3 point)
        {
            return new OBSharp.Float3(point.X, point.Y, point.Z);
        }

        public static Vector3 ToVector3(this OBSharp.Float3 point)
        {
            return new Vector3(point.X, point.Y, point.Z);
        }

        public static void ActivateMinimized(this Window window)
        {
            ShowWindow(window.GetWindowHandle(), SW_MINIMIZED);
        }

        private const int SW_MINIMIZED = 6;  // Minimize the window

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
