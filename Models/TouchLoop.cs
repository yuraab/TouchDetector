using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using OBSharp;
using OBSharp.BodyTracking;
using OBSharp.Sensor;
using System;
using System.Buffers;
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
        //private OBSharp.Sensor.Image? _imageForProcessing;
        //private CalibrationGeometry? _calibrationGeometry;
        private readonly Thread _thread;
        private bool _isRunning;
        private bool _isDisposed;
        //private readonly object _lock = new();
        private readonly TouchTracker_ _tracker;

        public event EventHandler<TouchFrame>? TouchFrameReady;
        public event EventHandler<Exception>? TouchLoopFailed;

        private int _maxQueueSize = 3;
        private readonly object _queueLock = new();
        private readonly Queue<(ushort[] Image, CalibrationGeometry Geometry, DateTime time)> _imageQueue = new();
        
        private TimeSpan _minFrameInterval = TimeSpan.FromMilliseconds(100); // 10 FPS
        private DateTime _lastSentTime = DateTime.MinValue;
        //private readonly object _rateLock = new();

        //private Thread? _processingThread;

        private readonly int _maxRatePerSecond;
        private DateTime _lastProcessedTime = DateTime.MinValue;

        // Event to notify main process about readiness
        public event EventHandler<bool>? ReadyForNewImage;
        public void SetMaxQueueSize(int max) => _maxQueueSize = max;
        public void SetTargetFps(int fps) => _minFrameInterval = TimeSpan.FromMilliseconds(1000.0 / fps);

        public TouchLoop(TouchVolume volume, Calibration calibration, int maxRatePerSecond = 10)
        {
            _tracker = new TouchTracker_(volume, calibration);  
            _thread = new Thread(ProcessingLoop)
            {
                IsBackground = true,
                Name = "TouchLoop"
            };
            _maxRatePerSecond = maxRatePerSecond;
            _minFrameInterval = TimeSpan.FromMilliseconds(1000/ _maxRatePerSecond);
        }

        public void Run()
        {
            if (_isRunning)
                throw new InvalidOperationException("Could not run tracking loop. It's already running.");

            _isRunning = true;

            // Subscribe to tracker events
            _tracker.TouchFrameReady += OnTrackerFrameReady;
            _tracker.Run();

            _thread.Start();
#if DEBUG
            App.Log("Loop started");
#endif
        }

        //public bool TrySendImage(Capture capture)
        public bool TrySendImage(ushort[] image, int fw, int fh, DateTime time)
        {
            if (!_isRunning)
                return false;

            var now = DateTime.UtcNow;

            lock (_queueLock) 
            {
                // Rate limiting check
                if (now - _lastSentTime < _minFrameInterval)
                {
                    ReadyForNewImage?.Invoke(this, false);
                    return false;
                }

                // Queue size check
                if (_imageQueue.Count >= _maxQueueSize)
                {
                    ReadyForNewImage?.Invoke(this, false);
                    return false;
                }

                _lastSentTime = now;

                ushort[] depthImage = new ushort[image.Length];
                image.AsSpan().CopyTo(depthImage);
                _imageQueue.Enqueue((depthImage, CalibrationGeometry.Color, time));

                ReadyForNewImage?.Invoke(this, true); // Notify main process that it can send new image
                _imageAvailable.Set(); // Wake processing thread
                return true;
            }
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

                (ushort[] image, CalibrationGeometry Geometry, DateTime time)? data = null;

                lock (_queueLock)
                {
                    if (_imageQueue.Count > 0)
                    {
                        data = _imageQueue.Dequeue();
                        _lastProcessedTime = DateTime.UtcNow;
                        _imageAvailable.Set();
                    }
                }

                if (data.HasValue)
                {
                    try
                    {

                        _ = _tracker.EnqueueImage((ushort[])data.Value.image, (CalibrationGeometry)data.Value.Geometry!, data.Value.time);


                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TouchLoop] Processing error: {ex}");
                    }
                    finally
                    {
                        // Do not dispose image here because _tracker is responsible for this!
                        _readyToReceive.Set();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _isRunning = false;

            // 1. Unsubscribe from events first to prevent race conditions during shutdown
            _tracker.TouchFrameReady -= OnTrackerFrameReady;

            // 2. Dispose tracker to stop background thread and clean queue
            _tracker.Dispose();

            // 3. Dispose last pending image (if any)
            //_imageForProcessing?.Dispose();
            //_imageForProcessing = null;

            // 4. Dispose signaling resources (AutoResetEvents)
            _readyToReceive.Dispose();
            _imageAvailable.Dispose();

#if DEBUG
            App.Log("TouchLoop disposed.");
#endif
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
