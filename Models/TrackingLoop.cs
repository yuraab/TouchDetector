using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OBSharp;
using OBSharp.BodyTracking;
using OBSharp.Sensor;

namespace CPRTouchVision.Models
{
    internal class TrackingLoop : IDisposable
    {
        private readonly Tracker _tracker;
        private readonly Thread _thread;
        private volatile bool _isRunning = false;

        public event EventHandler<BodyFrameEventArgs>? BodyFrameReady;
        public event EventHandler<LoopFailedEventArgs>? LoopFailed;


        public TrackingLoop(in Calibration calibration)
        {
            _thread = new Thread(BackgroundLoop) { IsBackground = true };
            /*
            _tracker = new Tracker(calibration, new TrackerConfiguration()
            {
                SensorOrientation = SensorOrientation.Default,
                ProcessingMode = TrackerProcessingMode.GpuCuda,
                ModelPath = Sdk.BODY_TRACKING_DNN_MODEL_FILE_NAME
            });
            */
            //{
            //    TemporalSmoothingFactor = 0.8f
            //};
        }

        public void Run()
        {
            if (_isRunning)
                throw new InvalidOperationException("Could not run tracking loop. It's already running.");
            _isRunning = true;
            _thread.Start();
        }

        public void Enqueue(Capture capture)
        {
            using var irImage = capture.IRImage;
            if (irImage == null) return;

            using var depthImage = capture.DepthImage;
            if (depthImage == null) return;

            _tracker.EnqueueCapture(capture);
        }

        private void BackgroundLoop()
        {
            try
            {
                while (_isRunning)
                {
                    if (_tracker.TryPopResult(out var frame))
                    {
                        using (frame)
                        {
                            BodyFrameReady?.Invoke(this, new(frame));
                        }
                    }
                    else
                        Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                LoopFailed?.Invoke(this, new(ex));
            }
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                _isRunning = false;
                if (_thread != null && _thread.ThreadState != System.Threading.ThreadState.Unstarted)
                    _thread.Join();
            }

            _tracker.Dispose();
        }
    }

    internal class BodyFrameEventArgs : EventArgs
    {
        public BodyFrame Frame { get; }
        public BodyFrameEventArgs(BodyFrame frame) => Frame = frame;
    }
}
