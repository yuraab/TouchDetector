using OBSharp;
using OBSharp.BodyTracking;
using OBSharp.Sensor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace CPRTouchVision.Models
{
    internal sealed class TouchLoop : IDisposable
    {
        private readonly TouchTracker_ _tracker;
        private readonly Thread _thread;
        private volatile bool _isRunning = false;

        public event EventHandler<TouchLoopEventArgs>? TouchFrameReady;
        public event EventHandler<TouchLoopFailedEventArgs>? LoopFailed;

        public TouchLoop(TouchVolume touchVolume, Calibration calibration)
        {
            _tracker = new TouchTracker_(touchVolume, calibration);
            _thread = new Thread(BackgroundLoop) { IsBackground = true };
        }

        public TouchLoop(Vector3 wallNormal, float wallDistance,
                         float minOffset, float maxOffset,
                         Vector3 wallCorner1, Vector3 wallCorner2,
                         int frameWidth, int frameHeight,
                         Calibration calibration)
            : this(new TouchVolume(wallNormal, wallDistance, minOffset, maxOffset,
                                   wallCorner1, wallCorner2 
                                   //frameWidth, frameHeight
                ), calibration)
        { }

        public void Run()
        {
            if (_isRunning)
                throw new InvalidOperationException("Touch loop already running.");

            _isRunning = true;
            _thread.Start();
#if DEBUG
            App.Log("Touch Loop running!");
#endif
        }

        public void Enqueue(Capture capture)
        {
            if (capture.IRImage == null || capture.DepthImage == null)
                return;

            _tracker.EnqueueCapture(capture);
        }

        private void BackgroundLoop()
        {
            try
            {
                while (_isRunning)
                {
                    if (_tracker.TryPopResult(out var frame) && frame?.HasTouches == true)
                    {
                        TouchFrameReady?.Invoke(this, new TouchLoopEventArgs(frame.Clusters));
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                LoopFailed?.Invoke(this, new TouchLoopFailedEventArgs(ex));
            }
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                _isRunning = false;
                _thread.Join();
            }

            _tracker.Dispose();
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
