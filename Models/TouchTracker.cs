using CPRTouchVision;
using CPRTouchVision.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using OBSharp;
using OBSharp.Sensor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OB = OBSharp;

namespace CPRTouchVision.Models
{
    public sealed class TouchTracker_ : IDisposable
    {
        private readonly TouchVolume _volume;
        private readonly Touch2DClusterManager _clusterManager;
        private readonly Calibration _calibration;
        private readonly int _fw;
        private readonly int _fh;

        private Thread _thread;
        private OBSharp.Sensor.Image? _currentImage;
        private CalibrationGeometry _calibrationGeometry;
        private readonly AutoResetEvent _readyToReceive = new(true);     // Initially ready
        private readonly AutoResetEvent _imageAvailable = new(false);    // Wait for image
        private readonly CancellationTokenSource _cts = new();

        private readonly object _queueLock = new();
        private readonly Queue<(ushort[] Image, CalibrationGeometry Geometry, DateTime time)> _imageQueue = new();

        private bool _isRunning;
        private bool _isDisposed;
        private bool _isCountPointsDisplayed = false;
        private int _maxQueueSize;
        private readonly object _depthLock = new object();
        private bool flag = false;

        public event EventHandler<TouchFrame>? TouchFrameReady;

        public TouchTracker_(TouchVolume volume, Calibration calibration, int maxQueueSize = 5)
        {
            _volume = volume;
            _calibration = calibration;

            _clusterManager = new Touch2DClusterManager();

            _thread = new Thread(ProcessLoop)
            {
                IsBackground = true,
                Name = "TouchTrackerLoop"
            };

#if DEBUG
            App.Log("TouchTracker created.");
#endif
            _maxQueueSize = maxQueueSize;
        }

        /// <summary>
        /// Start the background processing loop.
        /// </summary>
        public void Run()
        {
            if (_isRunning)
                throw new InvalidOperationException("TouchTracker is already running.");

            _isRunning = true;
            _thread.Start();

#if DEBUG
            //App.Log("TouchTracker started.");
#endif
        }

        /// <summary>
        /// Send a new image for processing. Waits until tracker is ready to receive it.
        /// </summary>
        public bool EnqueueImage(ushort[] image, CalibrationGeometry calibrationGeometry, DateTime time)
        {

            lock (_queueLock)
            {
                if (_imageQueue.Count >= _maxQueueSize)
                {
#if DEBUG
                    App.Log("Tracker queue full, rejecting image.");
#endif
                    return false;
                }
                // Save image and geometry for later processing
                _imageQueue.Enqueue((image, calibrationGeometry, time));
                return true;
            }

        }

        /// <summary>
        /// The main processing loop that runs in a background thread.
        /// </summary>
        private void ProcessLoop()
        {
#if DEBUG
            //App.Log($"TouchTracker processing loop started. IsRunning = {_isRunning}");
#endif

            while (_isRunning)
            {
#if DEBUG
                //App.Log("TouchTracker loop tick.");
#endif
                //_imageAvailable.WaitOne();

                while (true)
                {

                    (ushort[] image, CalibrationGeometry geometry, DateTime time)? item = null;

                    lock (_queueLock)
                    {
                        if (_imageQueue.Count > 0)
                        {
                            item = _imageQueue.Dequeue();
                        }
                        else
                        {
                            break;
                        }

                        if (item != null)
                        {
                            try
                            {
                                var (image, geometry, time) = item.Value;

                                // Process the image
                                ProcessImage(image, geometry, time);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[TouchTracker] Processing failed: {ex}");
                            }
                        }
                        else
                        {
                            Thread.Sleep(1); // avoid tight loop when queue is empty
                        }
                    }
                }
            }

        }


        private void ProcessImage(ushort[] image, CalibrationGeometry geometry, DateTime time)
        {
            List<Touch2DCluster> clusters = new List<Touch2DCluster>();

            //var points = Extract3DPointsInsideVolume(image);
            var points = Extract2DPointsInsideVolume(image);
#if DEBUG
            if (!_isCountPointsDisplayed && points.Count >= _clusterManager.MinPoints)
            {
                App.Log($"[TouchTracker] Filtered points count: {points.Count}");
                _isCountPointsDisplayed = false;
            }
#endif
            if (points.Count < _clusterManager.MinPoints)
            {
                return;
            }
            clusters = _clusterManager.DetectClusters(points);
            if (clusters?.Count > 0)
            {
                foreach (var cluster in clusters)
                {
                    cluster.NormalizedCenter = _volume.GetHomographyCoordinatesFrom2D(cluster.Center);
                    cluster.Center3D = _volume.Get3DPointFromLocal2DPoint(cluster.Center);
                }
                var frame = new TouchFrame(clusters);
                TouchFrameReady?.Invoke(this, frame);
            }
#if DEBUG
            else if (!flag)
            {
                App.Log($"Points not detected as cluster: {points}");
            }
#endif
        }

        private List<OB.Float3> Extract3DPointsInsideVolume(ushort[] depthImage)
        {
            return _volume.Extract3DPointsInsideVolume(depthImage);
        }
        private List<OB.Float2> Extract2DPointsInsideVolume(ushort[] depthImage)
        {
            return _volume.ExtractProjectedPointsInsideVolume(depthImage);
        }
        public void Stop()
        {
            _isRunning = false;

            // Wake up the loop if it's waiting
            _imageAvailable.Set();

            // Wait for background thread to exit
            _thread?.Join();

            // Dispose any remaining images in the queue
            lock (_queueLock)
            {
                while (_imageQueue.Count > 0)
                {
                    var (image, _, _) = _imageQueue.Dequeue();
                    //image.Dispose(); does not need for ushort[]
                }
            }

#if DEBUG
            App.Log("TouchTracker shutdown complete.");
#endif
        }

        public void Dispose()
        {

            if (_isDisposed) return;
            _isDisposed = true;

            Stop();


#if DEBUG
            App.Log("TouchTracker disposed.");
#endif
        }
    }
}

