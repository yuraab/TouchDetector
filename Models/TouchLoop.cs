using OBSharp.Sensor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace CPRTouchVision.Models
{
    public sealed class TouchLoop : IDisposable
    {
        private readonly Thread _thread;

        private readonly object _frameLock =
            new();

        private readonly TouchTracker _tracker;

        //
        // Latest frame only
        //

        private ushort[]? _latestImage;
        private DateTime _latestTime;

        private bool _hasNewFrame;

        private bool _isDisposed;

        public bool IsRunning { get; private set; }

        //
        // FPS limiting
        //

        private readonly TimeSpan _minFrameInterval;

        private DateTime _lastAcceptedFrameTime =
            DateTime.MinValue;

        //
        // Events
        //

        public event EventHandler<TouchFrame>? TouchFrameReady;

        public event EventHandler<Exception>? TouchLoopFailed;

        public event EventHandler<bool>? ReadyForNewImage;

        public TouchLoop(
            ITouchVolume volume,
            Calibration calibration,
            List<ExclusionZone>? exclusionZones = null,
            int maxRatePerSecond = 30)
        {
            _tracker =
                new TouchTracker(
                    volume,
                    calibration,
                    exclusionZones);

            //
            // Avoid duplicate subscription
            //

            _tracker.TouchFrameReady -=
                OnTrackerFrameReady;

            _tracker.TouchFrameReady +=
                OnTrackerFrameReady;

            _minFrameInterval =
                TimeSpan.FromMilliseconds(
                    1000.0 / maxRatePerSecond);

            _thread =
                new Thread(ProcessingLoop)
                {
                    IsBackground = true,
                    Name = "TouchLoop"
                };
        }

        public void Run()
        {
            if (IsRunning)
                return;

            IsRunning = true;

            _tracker.Run();

            _thread.Start();

#if DEBUG || TEST
            App.Log("TouchLoop started");
#endif
        }

        //
        // NOTE:
        // image is already copied outside
        //

        public bool TrySendImage(
            ushort[] image,
            int fw,
            int fh,
            DateTime time)
        {
            if (!IsRunning)
                return false;

            //
            // FPS limit
            //

            var now =
                DateTime.UtcNow;

            if (now - _lastAcceptedFrameTime <
                _minFrameInterval)
            {
                ReadyForNewImage?.Invoke(
                    this,
                    false);

                return false;
            }

            _lastAcceptedFrameTime =
                now;

            //
            // Store ONLY latest frame
            //

            lock (_frameLock)
            {
                _latestImage = image;
                _latestTime = time;
                _hasNewFrame = true;
            }

            ReadyForNewImage?.Invoke(
                this,
                true);

            return true;
        }

        private void ProcessingLoop()
        {
            while (IsRunning)
            {
                ushort[]? image = null;
                DateTime time = default;

                //
                // Grab latest frame
                //

                lock (_frameLock)
                {
                    if (_hasNewFrame &&
                        _latestImage != null)
                    {
                        image = _latestImage;
                        time = _latestTime;

                        //
                        // Mark consumed
                        //

                        _latestImage = null;
                        _hasNewFrame = false;
                    }
                }

                //
                // No frame available
                //

                if (image == null)
                {
                    Thread.Sleep(1);
                    continue;
                }

                try
                {
                    //
                    // Process frame
                    //

                    //_tracker.EnqueueImage(image, CalibrationGeometry.Depth, time);
                    _tracker.SubmitFrame(image, time);
                }
                catch (Exception ex)
                {
                    App.Log(
                        $"TouchLoop processing failed:\n{ex}");

                    TouchLoopFailed?.Invoke(
                        this,
                        ex);
                }
            }
        }

        private void OnTrackerFrameReady(
            object? sender,
            TouchFrame frame)
        {
            TouchFrameReady?.Invoke(
                this,
                frame);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            IsRunning = false;

            //
            // Unsubscribe events
            //

            _tracker.TouchFrameReady -=
                OnTrackerFrameReady;

            //
            // Dispose tracker
            //

            _tracker.Dispose();

#if DEBUG || TEST
            App.Log("TouchLoop disposed");
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
