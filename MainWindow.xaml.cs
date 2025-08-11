//using ABI.System.Numerics;
using CPRTouchVision.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
//using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
//using OBSharp.Sensor;
using SkiaSharp;
using SkiaSharp.Views.Windows;
//using System;
//using System.Diagnostics.Metrics;
using System.Linq;
//using System.Numerics;
using WinUIEx;
//using SN = System.Numerics;

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

#if DEBUG
        private bool _singleOutPutDone = false;
#endif

        public MainWindow()
        {
            _ = _manager.LoadConfig();
            InitializeComponent();
            _manager.Changed += OnManagerChanged;
        }

        private void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            _manager.Toggle();
        }

        private void OnDefineWallPlaneClicked(object? sender, RoutedEventArgs e)
        {
            _manager.StartCalibration();
        }

        private void OnDefineTouchZoneClicked(object? sender, RoutedEventArgs e)
        {
            _manager.StartDefineZone();
        }

        private void OnManagerChanged(object? sender, TouchManagerEventType e)
        {
            switch (e)
            {
                case TouchManagerEventType.NewFrame:
                    Canvas.Invalidate();
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

            if (_manager.IsCalibrating || !_manager.IsWallPlaneSet)
                DrawCalibration(canvas);

            else if (_manager.IsTouchZoneSet)
            {
                DrawTouchZone(canvas);
            }
            else if (_manager.IsCalibrating || _manager.IsSelectingTouchZone)
                DrawCursor(canvas);
        }

        private void DrawCalibration(SKCanvas canvas)
        {
            var paint = new SKPaint()
            {
                Style = SKPaintStyle.Fill,
                Color = SKColors.GreenYellow,
                IsAntialias = true
            };

            var path = new SKPath();

            for (int i = 0; i < _manager.CalibrationPoints.Length; i++)
            {
                var point = _manager.CalibrationPoints[i].ToSKPoint();

                if (i == 0)
                    path.MoveTo(point);
                else if (i == TouchManager.CalibrationPointsCount - 1)
                {
                    path.LineTo(point);
                    path.Close();
                }
                else
                    path.LineTo(point);

                canvas.DrawCircle(point, 6, paint);
            }

            if (_manager.CalibrationPoints.Length == TouchManager.CalibrationPointsCount)
            {
                paint.Color = SKColors.GreenYellow.WithAlpha(128);
                canvas.DrawPath(path, paint);
            }

            if (!path.IsEmpty)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 3;
                paint.Color = SKColors.GreenYellow;
                canvas.DrawPath(path, paint);
            }
        }

        private void DrawTouchZone(SKCanvas canvas)
        {
            DrawTouchZonePolygon(canvas);
        }

        private void DrawTouchZonePolygon(SKCanvas canvas)
        {
            if (!_manager.IsTouchZoneSet)
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
            if (_manager.IsCalibrating && _manager.CalibrationPoints.Length > 0)
                canvas.DrawLine(_manager.CalibrationPoints.Last().ToSKPoint(), _manager.Cursor, paint);

            else if (_manager.IsSelectingTouchZone && (TouchManager.TouchZonePointsCount > _manager.TouchZonePoints.Length && _manager.TouchZonePoints.Length > 0))
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
                string message = $"point with x={x}, y={y}";

                if (_manager.IsCalibrating)
                {
#if DEBUG
                    App.Log($"Add calibrating point {message}");
#endif
                    _manager.AddCalibrationPoint(new(x, y));
                }
                else if (_manager.IsSelectingTouchZone)
                {
#if DEBUG
                    App.Log($"Add touch zone point {message}");
#endif
                    _manager.AddTouchZonePoint(x, y);
                }

            }
        }
 
    }
}
