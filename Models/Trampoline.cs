using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.UI;

namespace CPRTouchVision.Models
{
    internal class Trampoline
    {
        [JsonProperty("is_closed")]
        private bool _isClosed;

        [JsonProperty("color_value")]
        private uint _colorValue;

        [JsonProperty("id")]
        public int Id;

        [JsonProperty("vertices")]
        public List<DepthPoint> Vertices;

        [JsonIgnore]
        public string Name => $"Trampoline #{Id + 1}";

        [JsonIgnore]
        public Color Color => new SKColor(_colorValue).ToColor();

        [JsonIgnore]
        public Brush Foreground => new SolidColorBrush(Color);

        [JsonIgnore]
        public bool IsClosed => _isClosed;

        public Trampoline(int id, Color foreground)
        {
            Id = id;
            Vertices = new();
            _isClosed = false;
            _colorValue = (uint)foreground.ToSKColor();
        }

        public void Add(DepthPoint v)
        {
            if (_isClosed)
                return;

            Vertices.Add(v);
        }

        public void Close()
        {
            _isClosed = true;
        }
    }
}
