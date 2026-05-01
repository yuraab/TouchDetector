using OBSharp.Sensor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OB = OBSharp;

namespace CPRTouchVision.Models
{
    public sealed class TouchTracker_ : IDisposable
    {
        private readonly TouchVolume _volume;
        private readonly Touch2DClusterManager _clusterManager;
        private readonly ExclusionZoneManager_? _exclusionManager;

        private Thread _thread;
        private readonly AutoResetEvent _imageAvailable = new(false);    // Wait for image
        private readonly CancellationTokenSource _cts = new();

        private readonly object _queueLock = new();
        private readonly Queue<(ushort[] Image, CalibrationGeometry Geometry, DateTime time)> _imageQueue = new();

        private bool _isRunning;
        private bool _isDisposed;
        private int _maxQueueSize;

        public event EventHandler<TouchFrame>? TouchFrameReady;

        public TouchTracker_(
            TouchVolume volume, 
            Calibration calibration,
            List<ExclusionZone> exclusionZones = null,
            int maxQueueSize = 5)
        {
            _volume = volume;;

            _clusterManager = new Touch2DClusterManager();

            if (exclusionZones == null || exclusionZones.Count == 0)
            {
                _exclusionManager = null;
            }
            else
            {
                _exclusionManager = new ExclusionZoneManager_();
                _exclusionManager.AddZones(exclusionZones);
            }

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
            while (_isRunning)
            {
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
                                ProcessImage(image, time);
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

        private void ProcessImage(ushort[] image, DateTime time)
        {
            List<Touch2DCluster> clusters = new List<Touch2DCluster>();

            //var points = Extract3DPointsInsideVolume(image);
            var points = Extract2DPointsInsideVolume(image);
#if DEBUG
            if (points.Count >= _clusterManager.MinPoints)
            {
                App.Log($"[TouchTracker] Filtered points count: {points.Count}");
            }
            else
            {
                App.Log($"[TouchTracker] Not enough points for clustering. Count: {points.Count}");
            }
#endif
            if (points.Count < _clusterManager.MinPoints)
            {
                return;
            }
            clusters = _clusterManager.DetectClusters(points);

            if (clusters?.Count > 0)
            {
                if (_exclusionManager != null)
                {
                    clusters = _exclusionManager.FilterClusters(clusters);
                }
                foreach (var cluster in clusters)
                {
                    cluster.NormalizedCenter = _volume.GetHomographyCoordinatesFrom2D(cluster.Center);
                    cluster.Center3D = _volume.Get3DPointFromLocal2DPoint(cluster.Center);
                }
                var frame = new TouchFrame(clusters);
                TouchFrameReady?.Invoke(this, frame);
            }
        }

        private List<OB.Float2> Extract2DPointsInsideVolume(ushort[] depthImage)
        {
            return _volume.ExtractProjectedPointsInsideVolumeFromImage(depthImage);
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
        }
    }
}

