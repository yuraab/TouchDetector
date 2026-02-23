using CPRTouchVision.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using System;
using Windows.System;
using System.Linq;
using WinUIEx;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CPRTouchVision
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WindowEx
    {
        private TouchManager _manager = new();
        private float _cw = 1.0f;
        private float _ch = 1.0f;
        private readonly TimeSpan touchDisplayTime = TimeSpan.FromSeconds(2);

        public MainWindow()
        {
            ExtendsContentIntoTitleBar = true;
            _ = _manager.LoadConfig();
            Debug.WriteLine("loaded config");
            InitializeComponent();

            // Give manager a reference to the UI dispatcher
            _manager.UIDispatcherQueue = this.DispatcherQueue;

            _manager.Changed += OnManagerChanged;
            var ctrlZ = new KeyboardAccelerator()
            {
                Key = VirtualKey.Z,
                Modifiers = VirtualKeyModifiers.Control
            };
            ctrlZ.Invoked += CtrlZ_Invoked;

            Root.KeyboardAccelerators.Add(ctrlZ);
            ToolTipService.SetToolTip(Root, null);
            //(this.Content as UIElement)?.KeyboardAccelerators.Add(ctrlZ);
            //(this.Connect as UIElement).toolti
        }

        private void CtrlZ_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            _manager.Undo();
            args.Handled = true;
        }

        private void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            _manager.Toggle();
        }

        private void OnDefineTouchZoneClicked(object? sender, RoutedEventArgs e)
        {
            _manager.StartDefineZone();
        }

        private void OnCalibrationModeToggled(object sender, RoutedEventArgs e)
        {
            //_manager.SwitchCalibrationMode(); 
 
        }

        private void OnManagerChanged(object? sender, TouchManagerEventType e)
        {
            switch (e)
            {
                case TouchManagerEventType.NewFrame:
                    Canvas?.Invalidate();
                    break;
                default:
                    break;
            }
        }

        private void OnCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            _cw = e.Info.Width;
            _ch = e.Info.Height;

            if (!_manager.IsRunning)
                return;

            _manager.ColorBitmap.Draw(canvas, true);
            _manager.DepthBitmap.Draw(canvas);

            if (_manager.IsTouchZoneSet)
            {
                DrawTouchZone(canvas);
            }
            else if (_manager.IsCalibrating || _manager.IsSelectingTouchZone)
                DrawCursor(canvas);
            else if (_manager.Touches.Count > 0 && DateTime.UtcNow < _manager.Touches[0].Timestamp + touchDisplayTime)
                DrawTouches(canvas);
        }

        private void DrawTouches(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                // Color = SKColors.Red.WithAlpha(200),
                Color = SKColors.Red,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                StrokeWidth = 3
            };

            foreach (var touch in _manager.Touches)
            {
                var radius = touch.ScreenRadius < 10 ? 10 : touch.ScreenRadius;
                canvas.DrawCircle(touch.X, touch.Y, radius, paint);
            }
        }

        private void DrawTouchZone(SKCanvas canvas)
        {
            DrawTouchZonePolygon(canvas);
        }

        private void DrawTouchZonePolygon(SKCanvas canvas)
        {
            if (!_manager.IsTouchZoneSet || _manager.TouchZonePoints.Length != 4)
                return;

            var paint = new SKPaint()
            {
                Style = SKPaintStyle.Fill,
                Color = SKColors.Orange.WithAlpha(128),
                IsAntialias = true
            };

            using var path = GetTouchZonePolygonPath();

            // Save the canvas layer to allow for blending
            using var layerPaint = new SKPaint
            {
                BlendMode = SKBlendMode.SrcOver
            };
            canvas.SaveLayer(layerPaint);

            // Fill entire canvas with translucent black (fade effect)
            using var dimPaint = new SKPaint
            {
                Color = SKColors.Black.WithAlpha(160),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(new SKRect(0, 0, _cw, _ch), dimPaint);

            // Punch a transparent hole (by setting BlendMode to `DstOut`)
            using var maskPaint = new SKPaint
            {
                BlendMode = SKBlendMode.DstOut,
                Color = SKColors.Black, // Color doesn't matter here
                Style = SKPaintStyle.Fill
            };

            canvas.DrawPath(path, maskPaint);

            canvas.Restore();

            // Optional: Draw a border around the zone
            using var borderPaint = new SKPaint
            {
                Color = SKColors.Lime,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };
            canvas.DrawPath(path, borderPaint);
        }

        private SKPath GetTouchZonePolygonPath()
        {
            var path = new SKPath();
            path.MoveTo(_manager.TouchZonePoints[0].Screen);
            for (int i = 1; i < TouchManager.TouchZonePointsCount; i++)
                path.LineTo(_manager.TouchZonePoints[i].Screen);
            path.Close();

            return path;
        }

        private void DrawCursor(SKCanvas canvas)
        {
            var color = SKColors.GreenYellow;

            if (_manager.IsSelectingTouchZone)
                color = SKColors.Orange;

            var paint = new SKPaint()
                {
                    Style = SKPaintStyle.Stroke,
                    Color = color,
                    StrokeWidth = 3,
                    IsAntialias = true
                };
            // Calibration line
            if (_manager.IsSelectingTouchZone && (TouchManager.TouchZonePointsCount > _manager.TouchZonePoints.Length && _manager.TouchZonePoints.Length > 0))
            {
                for (int i = 0; i < _manager.TouchZonePoints.Length - 1; i++)
                    canvas.DrawLine(_manager.TouchZonePoints[i].Screen, _manager.TouchZonePoints[i+1].Screen, paint);
                canvas.DrawLine(_manager.TouchZonePoints.Last().Screen, _manager.Cursor, paint);
            }
            // Cursor dot
            paint.Style = SKPaintStyle.Fill;
            paint.Color = color;
            canvas.DrawCircle(_manager.Cursor, 6, paint);
        }

        private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(Canvas) is PointerPoint point)
            {
                var s = ((UIElement)sender).XamlRoot.RasterizationScale;
                var x = (int)(point.Position.X * s / _cw * _manager.FW);
                var y = (int)(point.Position.Y * s / _ch * _manager.FH);
                _manager.Cursor = new(x, y);
            }
        }

        private void OnCanvasPointerPressed(object? sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(Canvas) is PointerPoint point)
            {
                var s = ((UIElement)sender).XamlRoot.RasterizationScale;
                var x = (int)(point.Position.X * s / _cw * _manager.FW);
                var y = (int)(point.Position.Y * s / _ch * _manager.FH);
 
                Debug.WriteLine($"Selected point with x={x}, y={y}. FW={_manager.FW}, FH={_manager.FH}");

                if (_manager.IsSelectingTouchZone)
                {
                    _manager.AddTouchZonePoint(x, y);
                }

            }
        }

        private void OnGridLoaded(object sender, RoutedEventArgs e)
        {
            if (App.Current.AutoTrack)
                DispatcherQueue.TryEnqueue(() => _manager.Toggle());
        }
    }
}
