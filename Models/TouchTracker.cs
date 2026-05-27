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
        private readonly ITouchVolume _volume;
        //private readonly Touch2DClusterManager _clusterManager;
        private readonly ConnectedComponentClusterManager _clusterManager;
        private readonly ExclusionZoneManager_? _exclusionManager;

        private Thread _thread;
        private readonly AutoResetEvent _imageAvailable = new(false);    // Wait for image
        private readonly CancellationTokenSource _cts = new();

        private readonly object _queueLock = new();
        private readonly Queue<(ushort[] Image, CalibrationGeometry Geometry, DateTime time)> _imageQueue = new();

        private bool _isRunning;
        private bool _isDisposed;
        private int _maxQueueSize;

        private ushort[] sample;
        private long _frameCounter;

        public event EventHandler<TouchFrame>? TouchFrameReady;

        public TouchTracker_(
            ITouchVolume volume, 
            Calibration calibration,
            List<ExclusionZone> exclusionZones = null,
            int maxQueueSize = 5)
        {
            _volume = volume;;

            _clusterManager = new ConnectedComponentClusterManager(
                _volume.GetFrameWidth(), 
                _volume.GetFrameHeight());

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

#if DEBUG || TEST
            

            sample = Utilities.ReadCapturedFrame(Utilities.GetPath(Constants.StableDepthFile));
            App.Log("TouchTracker created. sample loaded.");
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
#if DEBUG || TEST
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
                (ushort[] image, CalibrationGeometry geometry, DateTime time)? item = null;

                while (true)
                {
                    lock (_queueLock)
                    {
                        if (_imageQueue.Count > 0)
                        {
                            item = _imageQueue.Dequeue();
                        }

                        if (item == null)
                        {
                            Thread.Sleep(1); // avoid tight loop when queue is empty
                            continue;
                        }

                        try
                        {
                            var (image, geometry, time) = item.Value;

                            // Process the image
                            ProcessImage(image, time);
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[TouchTracker] Processing failed: {ex}");
                        }

                    }
                }
            }

        }

        private void ProcessImage(ushort[] image, DateTime time)
        {
            List<Touch2DCluster> clusters = new List<Touch2DCluster>();

            long frameId =Interlocked.Increment(ref _frameCounter);

#if DEBUG2 || TEST2
            var sw = Stopwatch.StartNew();
#endif
            var points = _volume.ExtractProjectedPointIndicesInsideVolumeFromImage(image, frameId);//Extract2DPointsIndicesInsideVolume(image);

            //var points = Extract2DPointsInsideVolume(image);
#if DEBUG2 || TEST2
            double extractMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
#endif
            clusters = _clusterManager.DetectClusters(points);
#if DEBUG2 || TEST2
            double clusterMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            double mappingMs = 0;
#endif
            if (clusters?.Count > 0)
            {
                if (_exclusionManager != null)
                {
                    clusters = _exclusionManager.FilterClusters(clusters);
                }

                foreach (var cluster in clusters)
                {
                    cluster.NormalizedCenter = _volume.GetRelativeScreenCoordinatesFrom2D(cluster.Center, image);
                    //cluster.Center3D = _volume.Get3DPointFromLocal2DPoint(cluster.Center);
                }
#if DEBUG2 || TEST2
                mappingMs = sw.Elapsed.TotalMilliseconds;
#endif

                var frame = new TouchFrame(clusters, time);
                try
                {
                    TouchFrameReady?.Invoke(this, frame);
                }
                catch (Exception ex)
                {
                    App.Log(ex.ToString());
                }
            }

#if DEBUG2 || TEST2
            if (points.Count > 0)
            {
                App.Log($"Thread={Environment.CurrentManagedThreadId} Filtered points count: {points.Count}");
            }


            if (clusters?.Count > 0)
            {
                App.Log($"[TouchTracker] Detected {clusters.Count} clusters after exclusion filtering.");
                foreach (var cluster in clusters)
                {
                    App.Log($"Cluster: Screen Center={cluster.NormalizedCenter} Center={cluster.Center}, Radius={cluster.Radius}, Count={cluster.Count}");
                }
            }

            App.Log(
                $"Extract={extractMs:F2}ms " +
                $"DBSCAN={clusterMs:F2}ms " +
                $"Map={mappingMs:F2}ms " +
                $"Total={(extractMs+ clusterMs+ mappingMs):F2}");

#endif

        }

        private List<OB.Float2> Extract2DPointsInsideVolume(ushort[] depthImage)
        {
            return _volume.ExtractProjectedPointsInsideVolumeFromImage(depthImage);
        }

        private List<int> Extract2DPointsIndicesInsideVolume(ushort[] depthImage, long frameId)
        {
            return _volume.ExtractProjectedPointIndicesInsideVolumeFromImage(depthImage, frameId);
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

#if DEBUG || TEST
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

