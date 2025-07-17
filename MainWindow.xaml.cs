using System.Linq;
using CPRTouchVision.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OBSharp.BodyTracking;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using WinUIEx;

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
        private bool _isClosingTrampoline = false;

        public MainWindow()
        {
            InitializeComponent();
            _manager.Changed += OnManagerChanged;
            _ = _manager.LoadConfig();
        }

        private void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            _manager.Toggle();
        }

        private void OnDefineFloorClicked(object? sender, RoutedEventArgs e)
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

            if (_manager.IsCalibrating || !_manager.IsFloorLevelSet)
                DrawCalibration(canvas);

            if (_manager.IsRunning)
                DrawTrampolines(canvas);

            if (_manager.IsCalibrating || _manager.IsAddingTrampoline)
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

        private void DrawTrampolines(SKCanvas canvas)
        {
            for (int i = 0; i < _manager.Trampolines.Count; i++)
            {
                var trampoline = _manager.Trampolines[i];
                var paint = new SKPaint()
                {
                    Color = trampoline.Color.ToSKColor(),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };

                var path = new SKPath();

                for (int vi = 0; vi < trampoline.Vertices.Count; vi++)
                {
                    var point = trampoline.Vertices[vi].Screen;

                    if (vi == 0)
                        path.MoveTo(point);
                    else if (trampoline.IsClosed && vi == trampoline.Vertices.Count - 1)
                    {
                        path.LineTo(point);
                        path.Close();
                    }
                    else
                        path.LineTo(point);

                    canvas.DrawCircle(point, 6, paint);
                }

                if (trampoline.IsClosed)
                {
                    paint.Color = trampoline.Color.ToSKColor().WithAlpha(128);
                    canvas.DrawPath(path, paint);
                }

                if (!path.IsEmpty)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.Color = trampoline.Color.ToSKColor();
                    paint.StrokeWidth = 3;
                    canvas.DrawPath(path, paint);
                }
            }
        }

        private void DrawCursor(SKCanvas canvas)
        {
            var color = SKColors.GreenYellow;

            if (_manager.IsAddingTrampoline)
                color = _manager.CurrentTrampoline.Color.ToSKColor();

            var paint = new SKPaint()
            {
                Style = SKPaintStyle.Stroke,
                Color = color,
                StrokeWidth = 3,
                IsAntialias = true
            };

            if (_manager.IsCalibrating && _manager.CalibrationPoints.Length > 0)
                canvas.DrawLine(_manager.CalibrationPoints.Last().ToSKPoint(), _manager.Cursor, paint);
            else if (_manager.IsAddingTrampoline && _manager.CurrentTrampoline.Vertices.Count > 0)
            {
                canvas.DrawLine(_manager.CurrentTrampoline.Vertices.Last().Screen, _manager.Cursor, paint);

                if (_isClosingTrampoline)
                {
                    canvas.DrawCircle(_manager.CurrentTrampoline.Vertices.First().Screen, 12, paint);
                }
            }

            paint.Style = SKPaintStyle.Fill;
            canvas.DrawCircle(_manager.Cursor, 6, paint);
        }

        private void DrawBones(SKCanvas canvas, SKPaint paint, Skeleton skeleton)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2;

            foreach (var type in JointTypes.All)
            {
                var start = skeleton[type.GetParent()];
                var end = skeleton[type];
                var startPoint = _manager.JointToPoint(start);
                var endPoint = _manager.JointToPoint(end);
                if (!startPoint.IsEmpty && !endPoint.IsEmpty)
                    canvas.DrawLine(startPoint, endPoint, paint);
            }
        }

        private void DrawJoints(SKCanvas canvas, SKPaint paint, Skeleton skeleton)
        {
            paint.Style = SKPaintStyle.Fill;

            foreach (var type in JointTypes.All)
            {
                var joint = skeleton[type];
                var point = _manager.JointToPoint(joint);

                if (!point.IsEmpty)
                {
                    var r = type.IsFaceFeature() ? 4.0f : 6.0f;
                    canvas.DrawCircle(point, r, paint);
                }
            }
        }

        private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(Canvas) is PointerPoint point)
            {
                var s = ((UIElement)sender).XamlRoot.RasterizationScale;
                var x = (int)(point.Position.X * s / _cw * _manager.FW);
                var y = (int)(point.Position.Y * s / _ch * _manager.FH);
                _manager.Cursor = new(x, y);

                if (_manager.IsAddingTrampoline)
                {
                    var count = _manager.CurrentTrampoline.Vertices.Count;

                    if (count > 3)
                    {
                        var first = _manager.CurrentTrampoline.Vertices.First().Screen;

                        if (SKPoint.Distance(first, _manager.Cursor) < 5)
                            _isClosingTrampoline = true;
                        else
                            _isClosingTrampoline = false;
                    }
                    else
                        _isClosingTrampoline = false;
                }
                else
                    _isClosingTrampoline = false;
            }
        }

        private void OnCanvasPointerPressed(object? sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(Canvas) is PointerPoint point)
            {
                var s = ((UIElement)sender).XamlRoot.RasterizationScale;
                var x = (int)(point.Position.X * s / _cw * _manager.FW);
                var y = (int)(point.Position.Y * s / _ch * _manager.FH);

                if (_isClosingTrampoline)
                    _manager.CloseTrampoline();
                if (_manager.IsCalibrating)
                    _manager.AddCalibrationPoint(new(x, y));
                else if (_manager.IsAddingTrampoline)
                    _manager.AddTrampolinePoint(x, y);
            }
        }

        private void OnAddTrampolineClicked(object sender, RoutedEventArgs e)
        {
            _manager.StartAddingTrampoline();
        }

        private void OnDeleteTrampolineClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is int id)
            {
                _manager.RemoveTrampolineWith(id);
            }
        }
    }
}
