using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public interface IProjectorDisplay
    {
        void ShowImage(string path); 
        void Hide();
    }

    public class ProjectorService : IProjectorDisplay 
    { 
        private ProjectionWindow? _window; 
        public void ShowImage(string path) 
        { 
            var displays = DisplayArea.FindAll(); 
            var projector = displays.FirstOrDefault(d => !d.IsPrimary); 
            if (projector is null) return; 
            _window?.Close(); 
            _window = new ProjectionWindow(); 
            _window.AppWindow.MoveAndResize(
                new Windows.Graphics.RectInt32(
                    projector.WorkArea.X,
                    projector.WorkArea.Y, 
                    projector.WorkArea.Width, 
                    projector.WorkArea.Height
                    )
                ); 
            _window.SetImage(path); 
            _window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen); 
            _window.Activate(); 
        } 
        public void Hide() 
        { 
            _window?.Close(); 
            _window = null; 
        } 
    }

    public interface ICalibrationProgress
    {
        void OnStatus(string message); 
        void OnProgress(double value); // 0..1
        void OnCompleted(List<DepthPoint> points); 
        void OnFailed(string reason); 
    }

    public class AutoCalibrationService
    {
        private readonly IProjectorDisplay _projector; 
        private readonly ICalibrationProgress _progress;

        public AutoCalibrationService(IProjectorDisplay projector, ICalibrationProgress progress) 
        { 
            _projector = projector; 
            _progress = progress; 
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            try
            {
                _progress.OnStatus("Starting auto calibration..."); 
                _progress.OnProgress(0.05); 
                // 1. Show pattern
                _projector.ShowImage("Assets/Patterns/calibration_pattern.png");
                _progress.OnStatus("Projecting pattern...");
                await Task.Delay(500, token); // let projector settle 
                
                // 2. Capture + detect (replace with real logic)
                _progress.OnStatus("Capturing depth frame...");
                _progress.OnProgress(0.3); 
                var points = await DetectTouchZonePointsAsync(token); 
                if (points == null || points.Count < 4) 
                { 
                    _progress.OnFailed("Failed to detect touch zone"); 
                    _projector.Hide(); 
                    return; 
                } 
                _progress.OnStatus("Fitting plane and finalizing..."); 
                _progress.OnProgress(0.8); 
                _projector.Hide(); 
                _progress.OnCompleted(points); 
                _progress.OnProgress(1.0); 
                _progress.OnStatus("Auto calibration completed"); 
            } catch (OperationCanceledException) 
            { 
                _projector.Hide(); 
                _progress.OnFailed("Auto calibration canceled"); 
            } catch (Exception ex) 
            { 
                _projector.Hide(); 
                _progress.OnFailed($"Auto calibration error: {ex.Message}"); 
            } 
        } 
        private Task<List<DepthPoint>> DetectTouchZonePointsAsync(CancellationToken token) 
        { 
            // TODO: plug in AutoCalibrator logic here
            throw new NotImplementedException(); 
        } 
    }

}
