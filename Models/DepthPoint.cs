using System.Numerics;
using Newtonsoft.Json;
using SkiaSharp;

namespace CPRTouchVision.Models
{
    internal struct DepthPoint
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
}
