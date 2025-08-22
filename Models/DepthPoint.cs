using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CPRTouchVision.Models
{
    public struct DepthPoint
    {
        /* Screen X in pixels. */
        [JsonIgnore]
        public int SX
        {
            get => _sx;
            set
            {
                _sx = value;
                if (_isEmpty)
                    _isEmpty = false;
            }

        }

        /* Screen Y in pixels. */
        [JsonIgnore]
        public int SY
        {
            get => _sy;
            set
            {
                _sy = value;
                if (_isEmpty)
                    _isEmpty = false;
            }

        }

        /* World X in meters. */
        [JsonIgnore]
        public float X
        {
            get => _x;
            set
            {
                _x = value;
                if (_isEmpty)
                    _isEmpty = false;
            }

        }

        /* World Y in meters. */
        [JsonIgnore]
        public float Y
        {
            get => _y;
            set
            {
                _y = value;
                if (_isEmpty)
                    _isEmpty = false;
            }

        }

        /* World Z in meters */
        [JsonIgnore]
        public float Z
        {
            get => _z;
            set
            {
                _z = value;
                if (_isEmpty)
                    _isEmpty = false;
            }

        }

        [JsonIgnore]
        public SKPoint Screen => _screen;

        [JsonIgnore]
        public Vector3 World => _world;

        [JsonIgnore]
        public bool IsEmpty => _isEmpty;

        [JsonProperty("sx")]
        private int _sx;

        [JsonProperty("sy")]
        private int _sy;

        [JsonProperty("x")]
        private float _x;

        [JsonProperty("y")]
        private float _y;

        [JsonProperty("z")]
        private float _z;

        [JsonProperty("is_empty")]
        private bool _isEmpty;

        [JsonProperty("screen")]
        private SKPoint _screen;

        [JsonProperty("world")]
        private Vector3 _world;

        public DepthPoint()
        {
            _sx = default;
            _sy = default;
            _x = default;
            _y = default;
            _z = default;
            _screen = new(_sx, _sy);
            _world = new(_x, _y, _z);
            _isEmpty = true;
        }

        public static DepthPoint From(int sx, int sy, float x, float y, float z)
        {
            return new DepthPoint()
            {
                SX = sx,
                SY = sy,
                X = x,
                Y = y,
                Z = z,
                _screen = new(sx, sy),
                _world = new(x, y, z),
                _isEmpty = false
            };
        }

        public static DepthPoint Empty => new DepthPoint();
    }

    public struct DepthStat
    {
        int Sum;
        int Count;
        int SumSq;
        int Min;
        int Max;

        public void Add(int depth)
        {
            if (depth > 0)
            {
                if (Count == 0)
                {
                    Min = Max = depth;
                }
                else
                {
                    if (depth < Min) Min = depth;
                    if (depth > Max) Max = depth;
                }

                Sum += depth;
                SumSq += depth * depth;
                Count++;
            }
        }
        public float GetAverage()
        {
            return Count > 0 ? Sum / Count : 0;
        }
        public float GetStdDev()
        {
            if (Count <= 1) return 0;
            float mean = GetAverage();
            float variance = (SumSq / Count) - (mean * mean);
            return variance > 0 ? (float)Math.Sqrt(variance) : 0;
        }
    }


    public class DepthBuffer
    {
        private readonly float[] buffer;
        private int index = 0;
        private int count = 0;

        public DepthBuffer(int capacity = 16)
        {
            buffer = new float[capacity];
        }

        public void AddSample(float depth)
        {
            if (depth <= 0) return; 

            buffer[index] = depth;
            index = (index + 1) % buffer.Length;
            if (count < buffer.Length) count++;
        }

        // Median
        public float GetMedian()
        {
            if (count == 0) return 0;

            float[] sorted = new float[count];
            Array.Copy(buffer, sorted, count);
            Array.Sort(sorted);

            int mid = count / 2;
            if (count % 2 == 0)
                return (sorted[mid - 1] + sorted[mid]) / 2.0f;
            else
                return sorted[mid];
        }

        // Trimmed average (trimm edges 20%)
        public float GetTrimmedMean(float trimFraction = 0.2f)
        {
            if (count == 0) return 0;

            float[] sorted = new float[count];
            Array.Copy(buffer, sorted, count);
            Array.Sort(sorted);

            int trim = (int)(count * trimFraction);
            int start = trim;
            int end = count - trim;

            if (end <= start) return sorted[count / 2]; // fallback → median

            float sum = 0;
            int validCount = 0;
            for (int i = start; i < end; i++)
            {
                sum += sorted[i];
                validCount++;
            }

            return validCount > 0 ? sum / validCount : 0;
        }
    }

}
