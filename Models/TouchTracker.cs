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

namespace CPRTouchVision.Models
{
    public sealed class TouchTracker_ : IDisposable
    {
        private readonly TouchVolume _volume;
        private readonly TouchClusterManager _clusterManager;
        private readonly Calibration _calibration;

        private Thread _thread;
        private OBSharp.Sensor.Image? _currentImage;
        private CalibrationGeometry _calibrationGeometry;
        private readonly AutoResetEvent _readyToReceive = new(true);     // Initially ready
        private readonly AutoResetEvent _imageAvailable = new(false);    // Wait for image
        private readonly CancellationTokenSource _cts = new();

        private readonly object _queueLock = new();
        private readonly Queue<(OBSharp.Sensor.Image Image, CalibrationGeometry Geometry)> _imageQueue = new();

        private bool _isRunning;
        private bool _isDisposed;
        private bool _isCountPointsDisplayed = false;
        private int _maxQueueSize;

        public event EventHandler<TouchFrame>? TouchFrameReady;

        public TouchTracker_(TouchVolume volume, Calibration calibration, int maxQueueSize = 5)
        {
            _volume = volume;
            _calibration = calibration;
            _clusterManager = new TouchClusterManager();

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
        public bool EnqueueImage(OBSharp.Sensor.Image image, CalibrationGeometry calibrationGeometry)
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
                _imageQueue.Enqueue((image, calibrationGeometry));
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

                    (OBSharp.Sensor.Image image, CalibrationGeometry geometry)? item = null;

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
#if DEBUG
                                //App.Log("Tracker processing image...");
#endif
                                var (image, geometry) = item.Value;

                                // Process the image
                                ProcessImage(image, geometry);

                                // Then dispose
                                image.Dispose();

                                // This is CRITICAL: allow next image to be enqueued
                                //_readyToReceive.Set();
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


        private void ProcessImage(OBSharp.Sensor.Image image, CalibrationGeometry geometry)
        {
            var points = Extract3DPointsInsideVolume(image, _calibrationGeometry, out long timestamp);
#if DEBUG
            if (!_isCountPointsDisplayed || points.Count > 100)
            {
                App.Log($"[TouchTracker] Filtered points count: {points.Count}");
                _isCountPointsDisplayed = true;
            }
#endif
            var clusters = _clusterManager.DetectClusters(points);

            if (clusters?.Count > 0)
            {
                var frame = new TouchFrame(clusters, timestamp);
                TouchFrameReady?.Invoke(this, frame);
            }
        }

        private List<Vector3> Extract3DPointsInsideVolume(OBSharp.Sensor.Image depthImage, CalibrationGeometry calibrationGeometry, out long timestamp)
        {
            timestamp = 0;
            var result = new List<Vector3>();

            if (depthImage.SizeBytes < sizeof(ushort)) return result;

            int width = depthImage.WidthPixels;
            int height = depthImage.HeightPixels;

            unsafe
            {
                ushort* depthData = (ushort*)depthImage.Buffer;

                for (int y = 0; y < height; y += 2)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        int index = y * width + x;
                        float depth = depthData[index];
                        if (depth == 0) continue;

                        var world = _calibration.Convert2DTo3D(new(x, y), depth,
                            calibrationGeometry, CalibrationGeometry.Depth);

                        if (world.HasValue)
                        {
                            var vector = new Vector3(world.Value.X, world.Value.Y, world.Value.Z);
                            if (_volume.IsPointInVolume(vector))
                                result.Add(vector);
                        }
                    }
                }
            }

            if (result.Count > 0)
                timestamp = depthImage.DeviceTimestamp.ValueUsec;

            return result;
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
                    var (image, _) = _imageQueue.Dequeue();
                    image.Dispose();
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

            /*
            _cts.Cancel();

            if (_thread.IsAlive)
                _thread.Join();

            _cts.Dispose();
            _readyToReceive.Dispose();
            _imageAvailable.Dispose();
            */

#if DEBUG
            App.Log("TouchTracker disposed.");
#endif
        }
    }
}


public sealed class TouchTracker
{
    private readonly TouchVolume _volume;
    private readonly TouchClusterManager _clusterManager;
    private readonly Calibration _calibration;

    public TouchTracker(TouchVolume volume, Calibration calibration)
    {
        _volume = volume;
        _clusterManager = new TouchClusterManager();
        _calibration = calibration;
    }

    public bool ProcessTouchPresence(OBSharp.Sensor.Image capture, CalibrationGeometry sourceCamera, out List<TouchCluster> clusters)
    {
        clusters = [];

        try
        {
            long timestamp;
            List<Vector3> points = Extract3DPointsInsideVolume(capture, sourceCamera, out timestamp);
#if DEBUG
            if (points.Count > 0) App.Log($"Count extracted points {points.Count}");
#endif

            clusters = _clusterManager.DetectClusters(points);
#if DEBUG
            if (points.Count > 0)  App.Log($"Count clusters {clusters.Count}");
#endif

            return clusters.Count > 0; // Touch presence
        }
        catch (Exception ex)
        {
            App.Log($"[TouchTracker] ERROR in ProcessTouchPresence: {ex.Message}");
            App.Log($"Calibration => {_calibration}");
            return false;
        }
    }

    private List<Vector3> Extract3DPointsInsideVolume(OBSharp.Sensor.Image depthImage, CalibrationGeometry sourceCamera, out long timestamp)
    {
        timestamp = 0;
        List<Vector3> result = new();
        /*
        float maxTD = _volume.WallDistance - _volume.MinOffset;
        float minTD = _volume.WallDistance - _volume.MaxOffset;
        int counter = 0;
        float minD = 100000;
        float maxD = 0;
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        float minIX = float.MaxValue, minIY = float.MaxValue;
        float maxIX = float.MinValue, maxIY = float.MinValue;
        */
        if (depthImage.SizeBytes < sizeof(ushort)) return result;

        int width = depthImage.WidthPixels;
        int height = depthImage.HeightPixels;

        unsafe
        {
            ushort* depthData = (ushort*)depthImage.Buffer;

            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 2)
                {
                    int index = y * width + x;
                    float depth = depthData[index];
#if DEBUG
                    
#endif
                    if (depth == 0) continue;

                    var world = _calibration.Convert2DTo3D(new(x, y), depth,
                                                          sourceCamera,
                                                          CalibrationGeometry.Depth);

                    if (world.HasValue)
                    {
                        var vector = new Vector3(world.Value.X, world.Value.Y, world.Value.Z);
                        if (_volume.IsPointInVolume(vector))
                            result.Add(vector);
                        //if (vector.X > maxX) maxX = vector.X;
                        //if (vector.X < minX) minX = vector.X;
                        //if (vector.Y > maxY) maxY = vector.Y;
                        //if (vector.Y < minY) minY = vector.Y;
                        ///var validD = depth <= maxTD && depth >= minTD;
                        //var validX = vector.X <= _volume.Corner4.X && vector.X >= _volume.Corner1.X;
                        //var validY = vector.Y <= _volume.Corner4.Y && vector.Y >= _volume.Corner1.Y;
                        //if (validD && validX && validY) counter++;
                    }
                    else
                    {
#if DEBUG
                        App.Log($"No value for point with x={x}, y={y}, depth={depth}");
#endif
                    }
                }
            }
        }

        if (result.Count > 0)
            timestamp = depthImage.DeviceTimestamp.ValueUsec;
#if DEBUG
        //App.Log($"Points in volume slice = {counter}");
        //App.Log($"minX = {minX}; maxX = {maxX}");
        //App.Log($"minY = {minY}; maxY = {maxY}");
#endif
        return result;
    }
}