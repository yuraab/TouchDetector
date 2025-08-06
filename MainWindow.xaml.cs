using ABI.System.Numerics;
using CPRTouchVision.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using System;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Numerics;
using WinUIEx;
using SN = System.Numerics;

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

        //private bool _isClosingTrampoline = false;

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

        private void OnDefineFloorClicked(object? sender, RoutedEventArgs e)
        {
            _manager.StartCalibration();
        }

        private void OnDefineTouchZoneClicked(object? sender, RoutedEventArgs e)
        {
            _manager.StartCalibration();
        }

        private void OnManagerChanged(object? sender, TouchManagerEventType e)
        {
            //DispatcherQueue.TryEnqueue(() => 
            //{
            switch (e)
            {
                case TouchManagerEventType.NewFrame:
                    Canvas.Invalidate();
                    break;
                default:
                    break;
            }
            //});
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

            //if (_manager.IsSelectingTouchZone || !_manager.IsTouchZoneSet)
            //if (_manager.IsTouchZoneSet || _manager.IsSelectingTouchZone)
            else if (_manager.IsTouchZoneSet)
            {
#if DEBUG
                if (!_singleOutPutDone)
                {
                    App.Log("Display Touch Zone");
                    _singleOutPutDone = true;
                }
#endif
                DrawTouchZone(canvas);
            }
            //if (_manager.IsRunning)
                //DrawTrampolines(canvas);

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
            if (!_manager.IsTouchZoneSet)
                return;

            var paint = new SKPaint()
            {
                Style = SKPaintStyle.Fill,
                Color = SKColors.Orange.WithAlpha(128),
                IsAntialias = true
            };
            var p1 = _manager.TouchZoneCornerScreen1;
            var p2 = _manager.TouchZoneCornerScreen2;

            var left = Math.Min(p1.X, p2.X);
            var right = Math.Max(p1.X, p2.X);
            var top = Math.Min(p1.Y, p2.Y);
            var bottom = Math.Max(p1.Y, p2.Y);
            // touch zone
            var touchZoneRect = new SKRect(left, top, right, bottom);

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
            canvas.DrawRect(touchZoneRect, maskPaint);
            //var path = GetTouchZoneScreenPath(ScreenToPlane(p1), ScreenToPlane(p2));
            //canvas.DrawPath(path, maskPaint);
            canvas.Restore();


            // Optional: Draw a border around the zone
            using var borderPaint = new SKPaint
            {
                Color = SKColors.Lime,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };
            canvas.DrawRect(touchZoneRect, borderPaint);
        }

        private SN.Vector3[] GetRectangleOnWall(SN.Vector3 p1, SN.Vector3 p2, SN.Vector3 planeNormal)
        {
            // Basis vectors on the wall plane
            var up = new SN.Vector3(0, 1, 0);
            if (Math.Abs(SN.Vector3.Dot(up, planeNormal)) > 0.99f) // If plane is vertical
                up = new SN.Vector3(1, 0, 0);

            var u = SN.Vector3.Normalize(SN.Vector3.Cross(planeNormal, up)); // horizontal on wall
            var v = SN.Vector3.Normalize(SN.Vector3.Cross(planeNormal, u));  // vertical on wall

            // Get local basis (u,v) rectangle extents from corner1 to corner2
            var delta = p2 - p1;
            float uLen = SN.Vector3.Dot(delta, u);
            float vLen = SN.Vector3.Dot(delta, v);

            var corner1 = p1;
            var corner2 = p1 + u * uLen;
            var corner3 = p1 + u * uLen + v * vLen;
            var corner4 = p1 + v * vLen;

            return new[] { corner1, corner2, corner3, corner4 };
        }

        private SKPoint ProjectToScreen(SN.Vector3 worldPoint)
        {
            float centerX = _manager.FW / 2f;
            float centerY = _manager.FH / 2f;
            SN.Vector3 cameraForward = SN.Vector3.Normalize(_manager.PlaneNormal); // or hardcode: Vector3.UnitZ
            SN.Vector3 toPoint = worldPoint - _manager.CameraPosition;

            // Camera coordinates: assume right-handed camera
            float x = SN.Vector3.Dot(toPoint, SN.Vector3.UnitX);
            float y = SN.Vector3.Dot(toPoint, SN.Vector3.UnitY);
            float z = SN.Vector3.Dot(toPoint, cameraForward); // distance along viewing direction

            if (z <= 0.01f) z = 0.01f; // avoid division by zero or behind camera

            // Basic perspective projection
            float screenX = _manager.FW * (x / z) + centerX;
            float screenY = _manager.FH * (-y / z) + centerY;

            return new SKPoint(screenX, screenY);
        }

        private SN.Vector3 ScreenToPlane(SKPoint screenPoint)
        {
            // Convert screen point to normalized device coordinates (-1 to 1)
            float ndcX = (screenPoint.X - _manager.FW / 2f) / _manager.FW;
            float ndcY = -(screenPoint.Y - _manager.FH / 2f) / _manager.FH; // Y is inverted in screen space

            // Construct ray direction in world space
            SN.Vector3 cameraForward = SN.Vector3.Normalize(_manager.PlaneNormal);
            SN.Vector3 rayDir = SN.Vector3.Normalize(cameraForward + ndcX * SN.Vector3.UnitX + ndcY * SN.Vector3.UnitY); // Adjust depending on FOV/aspect

            // Ray-plane intersection
            float denom = SN.Vector3.Dot(rayDir, _manager.PlaneNormal);
            if (Math.Abs(denom) < 1e-5f)
                return _manager.CameraPosition; // No intersection, return camera position as fallback

            float t = (_manager.PlaneD - SN.Vector3.Dot(_manager.CameraPosition, _manager.PlaneNormal)) / denom;
            return _manager.CameraPosition + rayDir * t;
        }

        private SKPath GetTouchZoneScreenPath(SN.Vector3 corner1, SN.Vector3 corner2)
        {
            // Reconstruct wall plane rectangle in 3D
            SN.Vector3 planeRight = SN.Vector3.Normalize(SN.Vector3.Cross(SN.Vector3.UnitY, _manager.PlaneNormal));
            SN.Vector3 planeUp = SN.Vector3.Normalize(SN.Vector3.Cross(_manager.PlaneNormal, planeRight));

            // Ensure consistent corner ordering (bottom-left, bottom-right, top-right, top-left)
            var minX = Math.Min(corner1.X, corner2.X);
            var maxX = Math.Max(corner1.X, corner2.X);
            var minY = Math.Min(corner1.Y, corner2.Y);
            var maxY = Math.Max(corner1.Y, corner2.Y);

            SN.Vector3 p1 = new SN.Vector3(minX, minY, corner1.Z); // Bottom-left
            SN.Vector3 p2 = new SN.Vector3(maxX, minY, corner1.Z); // Bottom-right
            SN.Vector3 p3 = new SN.Vector3(maxX, maxY, corner1.Z); // Top-right
            SN.Vector3 p4 = new SN.Vector3(minX, maxY, corner1.Z); // Top-left

            // Project to screen
            var pts = new[]
                    {
                ProjectToScreen(p1),
                ProjectToScreen(p2),
                ProjectToScreen(p3),
                ProjectToScreen(p4)
            };

            // Build path
            var path = new SKPath();
            path.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++)
                path.LineTo(pts[i]);
            path.Close();

            return path;
        }

        private void DrawProjectedTouchZoneByCursor(SKCanvas canvas)
        {
            var corner1 = _manager.TouchZoneCornerWorld1;
            var corner2 = ScreenToPlane(_manager.Cursor); // convert from screen to 3D
            var path = GetTouchZoneScreenPath(corner1, corner2);

            var paint = new SKPaint()
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Orange.WithAlpha(128),
                StrokeWidth = 3,
                IsAntialias = true
            };

            canvas.DrawPath(path, paint);
        }

        private void DrawRectTouchZoneByCursor(SKCanvas canvas)
        {
            if (_manager.TouchZoneCorner1.IsEmpty) return;
            //var p1 = _manager.TouchZoneCornerWorld1.ToSKPoint();
            var p1 = _manager.TouchZoneCornerScreen1;
            var p2 = _manager.Cursor; // convert from screen to 3D

            var rect = new SKRect(
                Math.Min(p1.X, p2.X),
                Math.Min(p1.Y, p2.Y),
                Math.Max(p1.X, p2.X),
                Math.Max(p1.Y, p2.Y)
            );
            var paint = new SKPaint()
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Orange.WithAlpha(128),
                StrokeWidth = 3,
                IsAntialias = true
            };

            canvas.DrawRect(rect, paint);
        }

        private void DrawTouchZoneByCursor(SKCanvas canvas)
        {
            //DrawProjectedTouchZoneByCursor(canvas);
            DrawRectTouchZoneByCursor(canvas);
        }
        private void DrawCursor(SKCanvas canvas)
        {
            var color = SKColors.GreenYellow;
            /*
            if (_manager.IsAddingTrampoline)
                color = _manager.CurrentTrampoline.Color.ToSKColor();
            */

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

            // Touch zone definition line and preview rectangle
            else if (_manager.IsSelectingTouchZone && !_manager.TouchZoneCorner1.IsEmpty)
                DrawTouchZoneByCursor(canvas);

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
