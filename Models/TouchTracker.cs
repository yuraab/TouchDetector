using CPRTouchVision;
using CPRTouchVision.Models;
using Microsoft.UI.Xaml.Controls;
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
        private readonly BlockingCollection<Capture> _captureQueue;
        private readonly BlockingCollection<TouchFrame> _resultQueue;
        private readonly CancellationTokenSource _cts = new();
        private readonly TouchVolume _volume;
        private readonly TouchClusterManager _clusterManager;
        private readonly Thread _processingThread;
        private readonly Calibration _calibration;
        private bool _isCountPointsDisplayed = false;

        public event EventHandler<TouchFrame>? TouchFrameReady;

        public TouchTracker_(TouchVolume volume, Calibration calibration, int maxQueueSize = 10)
        {
            _volume = volume;
            _clusterManager = new TouchClusterManager();
            _calibration = calibration;
            _captureQueue = new(maxQueueSize);
            _resultQueue = new();
            _processingThread = new Thread(ProcessLoop) { IsBackground = true };

#if DEBUG
            App.Log("TouchTracker initialized!");
#endif
            _processingThread.Start();
        }

        public void EnqueueCapture(Capture capture)
        {
            if (!_captureQueue.TryAdd(capture, 10))
                App.Log("WARNING: Touch capture queue full. Frame dropped.");
        }

        public bool TryPopResult(out TouchFrame? frame) => _resultQueue.TryTake(out frame);

        private void ProcessLoop()
        {
            try
            {
                foreach (var capture in _captureQueue.GetConsumingEnumerable(_cts.Token))
                {
                    var points = Extract3DPointsInsideVolume(capture, out long timestamp);

                    if (!_isCountPointsDisplayed)
                    {
                        App.Log($"Count of filtering points = {points.Count}");
                        _isCountPointsDisplayed = true;
                    }

                    var clusters = _clusterManager.DetectClusters(points);

                    if (clusters?.Count > 0)
                    {
                        var frame = new TouchFrame(clusters, timestamp);
                        _resultQueue.Add(frame);
                        TouchFrameReady?.Invoke(this, frame);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine("Touch processing error: " + ex);
            }
        }

        private List<Vector3> Extract3DPointsInsideVolume(Capture capture, out long timestamp)
        {
            timestamp = 0;
            List<Vector3> result = new();

            var depthImage = capture.DepthImage;
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
                                                              CalibrationGeometry.Depth,
                                                              CalibrationGeometry.Depth);

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

        public void Dispose()
        {
            _cts.Cancel();
            _processingThread.Join();
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