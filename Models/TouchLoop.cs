using Microsoft.UI.Xaml.Documents;
using OBSharp;
using OBSharp.BodyTracking;
using OBSharp.Sensor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public sealed class TouchLoop : IDisposable
    {
        private readonly AutoResetEvent _readyToReceive = new(true);  // Initially ready
        private readonly AutoResetEvent _imageAvailable = new(false); // No image yet
        private OBSharp.Sensor.Image? _imageForProcessing;
        private CalibrationGeometry? _calibrationGeometry;
        private readonly Thread _thread;
        private bool _isRunning;
        private bool _isDisposed;
        private readonly object _lock = new();
        private readonly TouchTracker_ _tracker;

        public event EventHandler<TouchFrame>? TouchFrameReady;
        public event EventHandler<Exception>? TouchLoopFailed;

        public TouchLoop(TouchVolume volume, Calibration calibration)
        {
            _tracker = new TouchTracker_(volume, calibration);  
            _thread = new Thread(ProcessingLoop)
            {
                IsBackground = true,
                Name = "TouchLoop"
            };
        }

        public void Run()
        {
            if (_isRunning)
                throw new InvalidOperationException("Could not run tracking loop. It's already running.");

            _isRunning = true;

            // Subscribe to tracker events
            _tracker.TouchFrameReady += OnTrackerFrameReady;
            _thread.Start();
#if DEBUG
            App.Log("Loop started");
#endif
        }

        public bool TrySendImage(OBSharp.Sensor.Image clonedImage, CalibrationGeometry calibrationGeometry)
        {
            if (!_isRunning || _isDisposed)
                return false;

            if (!_readyToReceive.WaitOne(100)) // short timeout to avoid blocking main thread too long
                return false;
#if DEBUG
            App.Log("Image received in TouchLoop to process");
#endif
            _imageForProcessing = clonedImage;
            _calibrationGeometry = calibrationGeometry;
            _imageAvailable.Set();
            return true;
        }

        private void OnTrackerFrameReady(object? sender, TouchFrame e)
        {
            TouchFrameReady?.Invoke(this, e); // Forward event to main process
        }

        private void OnTouchLoopException(Exception ex)
        {
            TouchLoopFailed?.Invoke(this, ex); // Notify main process
        }

        private void ProcessingLoop()
        {
            while (_isRunning)
            {
                _imageAvailable.WaitOne();

                OBSharp.Sensor.Image? image = null;
                CalibrationGeometry? geometry = null;

                lock (_lock)
                {
                    image = _imageForProcessing;
                    geometry = _calibrationGeometry;

                    _imageForProcessing = null;
                    _calibrationGeometry = null;
                }

                if (image is not null && geometry is not null)
                {
                    try
                    {
                        _tracker.EnqueueImage(image, (CalibrationGeometry)geometry!);
#if DEBUG
                        App.Log("Image is sent to tracker");
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TouchLoop] Processing error: {ex}");
                    }
                    finally
                    {
                        image.Dispose(); 
                    }
                    // Always signal readiness for the next frame
                    _readyToReceive.Set();
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _isRunning = false;
            _imageAvailable.Set(); // Wake the thread if it's waiting
            if (_thread.IsAlive)
                _thread.Join();

            _tracker.TouchFrameReady -= OnTrackerFrameReady;
            _tracker.Dispose();
            _imageForProcessing?.Dispose();
            _imageForProcessing = null;

            _readyToReceive.Dispose();
            _imageAvailable.Dispose();
        }
    }


    internal class TouchLoopEventArgs : EventArgs
    {
        public IReadOnlyList<TouchCluster> Clusters { get; }

        public TouchLoopEventArgs(IReadOnlyList<TouchCluster> clusters) => Clusters = clusters;

    }
    internal class TouchLoopFailedEventArgs : EventArgs
    {
        public TouchLoopFailedEventArgs(Exception exception)
            => Exception = exception;

        public Exception Exception { get; }
    }
}
