using CommunityToolkit.Mvvm.ComponentModel;
using ComputeSharp;
using Emgu.CV;
using Emgu.CV.Dai;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSharp;
using OBSharp.BodyTracking;
using OBSharp.Sensor;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.AI.MachineLearning;
using CV = Emgu.CV;
using OB = OBSharp.Sensor;

namespace CPRTouchVision.Models
{
    internal partial class TouchManager : ObservableObject, IDisposable
    {
        [ObservableProperty]
        string _toggleTitle = "Start";

        [ObservableProperty]
        bool _isRunning = false;

        [ObservableProperty]
        bool _isRunningTrackTouch = false;

        [ObservableProperty]
        bool _isReadyTrackTouch = false;

        [ObservableProperty]
        SKPoint _cursor = SKPoint.Empty;

        [ObservableProperty]
        string _cursorLabel = "";

        [ObservableProperty]
        bool _isFloorLevelSet = false;

        [ObservableProperty]
        bool _isWallPlaneSet = false;

        [ObservableProperty]
        bool _isTouchZoneSet = false;

        [ObservableProperty]
        string _floorLevelStatus = "Not set";

        [ObservableProperty]
        string _touchZoneStatus = "Not set";

        [ObservableProperty]
        Brush _floorLevelForeground = new SolidColorBrush(Colors.OrangeRed);

        [ObservableProperty]
        Brush _touchZoneForeground = new SolidColorBrush(Colors.OrangeRed);

        [ObservableProperty]
        bool _isCalibrating = false;

        [ObservableProperty]
        bool _isCalibrationToggleEnabled = false;

        [ObservableProperty]
        bool _isSelectingTouchZone = false;

        [ObservableProperty]
        bool _isTouchZoneToggleEnabled = false;

        [ObservableProperty]
        bool _isFittingPlane = false;

        [ObservableProperty]
        bool _isTouchZone = false;

        [ObservableProperty]
        int _offset = 15;

        [ObservableProperty]
        int _minOffset;

        [ObservableProperty]
        int _maxOffset;

        private bool _disposed = false;
        private Calibration _calibration = new();
        private Transformation? _transformation;

        // Getting data from the camera
        private CaptureLoop? _captureLoop;

        // Prosessing the data captured
        private TouchLoop? _touchLoop;

        private volatile bool _isProcessingTouch = false;

        private ushort[] _depthData = [];
        private byte[] _depthPixels = [];

        private List<Vector3> _calibrationPoints = new();
        private List<Vector3> _touchZonePoints = new();

        private TouchTracker _tracker;

        private float _planeD = float.MinValue;
        private Vector3 _planeNormal = Vector3.Zero;
        private Vector3 _cameraPosition = Vector3.Zero;
        private DepthPoint _touchZoneCorner1 = DepthPoint.Empty;
        private Vector3 _touchZoneCornerWorld1 = Vector3.Zero;
        private DepthPoint _touchZoneCorner2 = DepthPoint.Empty;
        private Vector3 _touchZoneCornerWorld2 = Vector3.Zero;
        private System.Numerics.Quaternion _cameraRotation = System.Numerics.Quaternion.Identity;
        private DepthVisualizer _depthVisualizer;
        public readonly object Lock = new object();

        // Rendering
        private int _fw = 1280;
        private int _fh = 720;
        // Handling
        private int _frameWidth = 640;
        private int _frameHeight = 576;

        private int _fsize;

        private SKImageInfo _colorBitmapInfo;
        private DoubleBufferedBitmap _colorBitmap;
        private DoubleBufferedBitmap _depthBitmap;
        private Dictionary<int, float> _elevationOffsets = new();


        private readonly object _depthLock = new object();
        private readonly object _bodiesLock = new object();
        private readonly int _defaltMaxOffset = 15;
        private readonly int _defaltMinOffset = 1;
        private OSCClient _oscClient = new();

        public DoubleBufferedBitmap ColorBitmap => _colorBitmap;
        public DoubleBufferedBitmap DepthBitmap => _depthBitmap;
        public int FW => _fw;
        public int FH => _fh;

        public int FrameWidth { get => _frameWidth; set => _frameWidth = value; }
        public int FrameHeight { get => _frameHeight; set => _frameHeight = value; }

        private bool _notSetTouchConfig = false;     

        public float PlaneD => _planeD;
        public Vector3 PlaneNormal => _planeNormal;
        public Vector3 CameraPosition => _cameraPosition;

        public Vector3[] CalibrationPoints => _calibrationPoints.ToArray();
        public Vector3[] TouchZonePoints => _touchZonePoints.ToArray();
        public DepthPoint TouchZoneCorner1 => _touchZoneCorner1;
        public DepthPoint TouchZoneCorner2 => _touchZoneCorner2;
        public Vector3 TouchZoneCornerWorld1 => _touchZoneCorner1.World;
        public SKPoint TouchZoneCornerScreen1 => _touchZoneCorner1.Screen;
        public Vector3 TouchZoneCornerWorld2 => _touchZoneCorner2.World;
        public SKPoint TouchZoneCornerScreen2 => _touchZoneCorner2.Screen;

        public EventHandler<TouchManagerEventType>? Changed;

        private UdpClient _client = new();
        private IPEndPoint _endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
        private CalibrationGeometry _sourceCamera;
        private List<TouchCluster> _clusters = [];
        private CalibrationGeometry _sourseCameraHandleTouch = CalibrationGeometry.Unknown;
        private bool _isConfigGotten = false; 
        public const int CalibrationPointsCount = 3;

        public TouchManager()
        {
            _fsize = _fw * _fh;
            _depthData = new ushort[_fsize];
            _depthPixels = new byte[_fsize * 4];
            _depthVisualizer = new(_fw, _fh);
            _colorBitmapInfo = new(_fw, _fh, SKColorType.Bgra8888, SKAlphaType.Premul);
            _colorBitmap = new DoubleBufferedBitmap(_colorBitmapInfo);
            _depthBitmap = new DoubleBufferedBitmap(_colorBitmapInfo);
            MaxOffset = _defaltMaxOffset;
            MinOffset = _defaltMinOffset;
        }

        public void Toggle()
        {
            if (IsRunning)
                Stop();
            else
                Start();
        }

        public SKPoint JointToPoint(Joint joint)
        {
            var result = _calibration.Convert3DTo2D(joint.PositionMm, CalibrationGeometry.Depth, CalibrationGeometry.Color);
            return result != null ? new(result.Value.X, result.Value.Y) : SKPoint.Empty;
        }

        private void Start()
        {
            if (IsRunning || !OBSharp.Sensor.Device.TryOpen(out var device)) return;

            _captureLoop = new(device);
            _captureLoop.CaptureReady += OnCaptureReady;
            _captureLoop.LoopFailed += OnLoopFailed;
            _captureLoop.GetCalibration(out _calibration);
       
#if DEBUG
            App.Log($"Calibration gotten with Depth Mode: {_calibration.DepthMode} :{_calibration.DepthCameraCalibration}");
#endif

            _transformation = new Transformation(_calibration);

            _captureLoop.Run();
            //_trackingLoop.Run();
            StartTouchLoop();
            IsRunning = true;

        }

        private void StartTouchLoop()
        {
            CheckIsReadyRunTouchLoop();
#if DEBUG
            App.Log($"Track Touch Loop ready to start = {IsReadyTrackTouch}");
#endif
            if (IsRunningTrackTouch || !IsReadyTrackTouch) return;

            TouchVolume detectableSpace = new TouchVolume(  _planeNormal, 
                                                            _planeD,
                                                            MinOffset*10,   //convert from centimeters
                                                            MaxOffset*10,   //convert from centimeters
                                                            TouchZoneCornerWorld1,
                                                            TouchZoneCornerWorld2
                                                            //_frameWidth, _frameHeight
                                                            );

            //_touchLoop = new(detectableSpace, _calibration);
            //_touchLoop.TouchFrameReady += OnTouchFrameReady;
            //_touchLoop.LoopFailed += OnTouchLoopFailed;
            //_touchLoop.Run();
            IsRunningTrackTouch = true;

            _tracker = new(detectableSpace, _calibration);
        }


        private void OnTouchFrameReady(object? sender, TouchLoopEventArgs e)
        {
            if (e.Clusters == null || e.Clusters.Count == 0) return;

            string message = string.Join(";", e.Clusters.Select(c =>
                $"[{c.Center.X:F2},{c.Center.Y:F2},{c.Center.Z:F2} R:{c.Radius:F2} µs:{c.TimestampMicroseconds}]"));

            App.Log(message);
            // used to be body frame processing logic to display skeletons, joints, jumps, etc.

            // TODO: - use OSCClient to send touch events instead of body data.
            // Task.Run(() => _oscClient.Send(bodies));
            // Changed?.Invoke(this, TouchManagerEventType.NewFrame);
        }

        private void OnLoopFailed(object? sender, LoopFailedEventArgs e)
        {
            // TODO: - Display Error
            App.Log("Capture loop failed");
        }

        private void OnTouchLoopFailed(object? sender, TouchLoopFailedEventArgs e)
        {
            // TODO: - Display Error
            App.Log("Touch loop failed");
        }

        public static Image CloneImage(Image source)
        {
            int width = source.WidthPixels;
            int height = source.HeightPixels;
            var format = source.Format;

            int stride = format.StrideBytes(width);
            int totalBytes = height * stride;

            // Rent buffer and copy data
            var memoryOwner = MemoryPool<byte>.Shared.Rent(totalBytes);
            var destinationSpan = memoryOwner.Memory.Span.Slice(0, totalBytes);
            // Convert raw pointer to Span
            unsafe
            {
                var sourceSpan = new Span<byte>((void*)source.Buffer, totalBytes);
                sourceSpan.CopyTo(destinationSpan);
            }
            // Create new image from memory
            return Image.CreateFromMemory(memoryOwner, format, width, height, stride);
        }

        private void OnCaptureReady(object? sender, CaptureLoopEventArgs e)
        {
            // TODO: - Handle touch logic here or modify TrackingLoop (preferred) to process touches instead of body frames.
            // This is the place where we can handle the data captured from the camera.
            // For body tracker (trampolines, immersive dancing, etc.) we used OBSharp BodyTracking sdk to process 
            // capture and get body frames in a separate background thread with TrackingLoop.
            
            if (e.Capture == null || e.Capture.IsDisposed)
                return;

            var clonedCapture = e.Capture.DuplicateReference();

            //_touchLoop?.Enqueue(e.Capture);

            if (!_isProcessingTouch && _tracker != null)
            {
                _isProcessingTouch = true;

                //Image clonedImage = CloneImage(clonedCapture.ColorImage);
                Image clonedImage = CloneImage(clonedCapture.DepthImage);
                _sourseCameraHandleTouch = CalibrationGeometry.Depth;
#if DEBUG
                //DateTime now = DateTime.Now;
                //var time = now.ToString("HH:mm:ss.fff");
                //App.Log($"Image size {clonedImage.WidthPixels}x{clonedImage.HeightPixels}");
#endif
                Task.Run(() =>
                {
                    try
                    {
                        if (_tracker.ProcessTouchPresence(clonedImage, _sourseCameraHandleTouch, out _clusters))
                        {
                            if (_clusters == null || _clusters.Count == 0) return;

                            string message = string.Join(";", _clusters.Select(c =>
                                $"[{c.Center.X:F2},{c.Center.Y:F2},{c.Center.Z:F2} R:{c.Radius:F2} Count:{c.Count} µs:{c.TimestampMicroseconds}]"));

                            App.Log(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[OnCaptureReady] ERROR during ProcessTouchPresence: {ex.Message}");
                    }
                    finally
                    {
                        clonedCapture.Dispose();
                        _isProcessingTouch = false;
#if DEBUG
                        //now = DateTime.Now;
                        //time = now.ToString("HH:mm:ss.fff");
                        //App.Log($"Stop process image {time}");
#endif
                    }
                });
            }


            // For now i'll just fire TouchManagerEventType.NewFrame event to display video output in the main window.
            using var capture = e.Capture;
            using var colorImage = capture.ColorImage;
            _sourceCamera = CalibrationGeometry.Color;
            if (colorImage != null)
            {
                _colorBitmap.Update((bitmap) =>
                {
                    bitmap.InstallPixels(_colorBitmapInfo, colorImage.Buffer);
                });
            }

            using var depthImage = capture.DepthImage;

            if (depthImage != null)
            {
                using var aligned = new OB.Image(OB.ImageFormat.Depth16, _fw, _fh);
                _transformation?.DepthImageToColorCamera(depthImage, aligned);
                _depthData.CopyFrom(aligned);
                _depthBitmap.Update((bitmap) =>
                {
                    bitmap.Pixels = _depthVisualizer.Update(_depthData);
                });
            }

            Changed?.Invoke(this, TouchManagerEventType.NewFrame);

        }

        private void OnTouchHappened(object? sender, TouchLoopEventArgs e)
        {
            if (e.Clusters == null || e.Clusters.Count == 0) return;

            string message = string.Join(";", e.Clusters.Select(c =>
                $"[{c.Center.X:F2},{c.Center.Y:F2},{c.Center.Z:F2} R:{c.Radius:F2} µs:{c.TimestampMicroseconds}]"));
            
            App.Log(message);
        }

        private void StopTouchLoop()
        {
            if (_touchLoop != null)
            {
                _touchLoop.TouchFrameReady -= OnTouchFrameReady;
                _touchLoop.LoopFailed -= OnTouchLoopFailed;
                _touchLoop.Dispose();
                _touchLoop = null;
            }
            IsRunningTrackTouch = false;
        }

        private void Stop()
        {
            if (!IsRunning) return;

            if (_captureLoop != null)
            {
                _captureLoop.CaptureReady -= OnCaptureReady;
                _captureLoop.LoopFailed -= OnLoopFailed;
                _captureLoop.Dispose();
                _captureLoop = null;
            }
            /*
            if (_trackingLoop != null)
            {
                _trackingLoop.BodyFrameReady -= OnBodyFrameReady;
                _trackingLoop.LoopFailed -= OnLoopFailed;
                _trackingLoop.Dispose();
                _trackingLoop = null;
            }
            */
            StopTouchLoop();

            _colorBitmap = new(_colorBitmapInfo);
            _depthBitmap = new(_colorBitmapInfo);

            IsRunning = false;
            
        }

        public void StartCalibration()
        {
            if (IsCalibrating)
                return;

            _calibrationPoints.Clear();
            IsFloorLevelSet = false;
            IsWallPlaneSet = true;
            IsCalibrating = true;
            ResetTouchZone();
        }

        private void ResetTouchZone()
        {
            _touchZoneCorner1 = DepthPoint.Empty;
            _touchZoneCorner2 = DepthPoint.Empty;
            IsTouchZoneSet = false;
            CheckIsReadyRunTouchLoop(); 
        }

        public void StartDefineZone()
        {
            if (IsSelectingTouchZone)
                { return; }

            ResetTouchZone();
            IsSelectingTouchZone = true;
        }

        public static async Task<T?> GetConfigValue<T>(string propertyName)
        {
            if (!File.Exists(CONFIG_PATH))
                return default;

            var bytes = await File.ReadAllBytesAsync(CONFIG_PATH);
            var json = Encoding.UTF8.GetString(bytes);

            var jObject = JObject.Parse(json);
            var token = jObject[propertyName];

            if (token == null)
                return default;

            try
            {
                return token.ToObject<T>()!;
            }
            catch (Exception ex)
            {
                return default;
            }
        }

        public async Task LoadConfig()
        {
            _isConfigGotten = false;

            if (File.Exists(CONFIG_PATH))
            {
                var bytes = await File.ReadAllBytesAsync(CONFIG_PATH);
                var json = Encoding.UTF8.GetString(bytes);

#if DEBUG
                App.Log($"Loaded Data: {json}");
#endif
                if (JsonConvert.DeserializeObject<Config>(json) is Config config)
                {
                    if (config.PlaneD.HasValue && config.PlaneNormal.HasValue)
                    {
                        _planeD = config.PlaneD.Value;
                        _planeNormal = config.PlaneNormal.Value;
                        _cameraRotation = config.CameraRotation.HasValue ? config.CameraRotation.Value : System.Numerics.Quaternion.Identity;
                        _cameraPosition = config.CameraPosition.HasValue ? config.CameraPosition.Value : Vector3.Zero;
                        IsFloorLevelSet = true;
                        IsWallPlaneSet = true; 
                    }

                    if (IsWallPlaneSet && config.TouchZoneCorner1.HasValue && config.TouchZoneCorner2.HasValue)
                    {
                        _touchZoneCorner1 = config.TouchZoneCorner1.Value;
                        _touchZoneCorner2 = config.TouchZoneCorner2.Value;
                        IsTouchZoneSet = true;
                    }
                    if (config.MinOffset.HasValue)
                    {
                        MinOffset = config.MinOffset.Value;
                    }
                    if (config.MaxOffset.HasValue)
                    {
                        MaxOffset = config.MaxOffset.Value;
                    }
                }
            }
            _isConfigGotten = true;
        }

        public async Task SaveConfig()
        {
            if (!_isConfigGotten) return; // To avoid a call SaveConfig before Config was loaded
            var config = new Config(
                MinOffset,
                MaxOffset,
                _planeD == float.MinValue ? null : _planeD,
                _planeNormal == Vector3.Zero ? null : _planeNormal,
                _touchZoneCorner1.IsEmpty ? null : _touchZoneCorner1,
                _touchZoneCorner2.IsEmpty ? null : _touchZoneCorner2,
                _cameraPosition,
                _cameraRotation.IsIdentity ? null : _cameraRotation
            );
            var json = JsonConvert.SerializeObject(config) ?? "";
#if DEBUG
            App.Log($"Saved Data:{json}");
#endif
            var data = Encoding.UTF8.GetBytes(json);
            Directory.CreateDirectory(CONFIG_FOLDER);
            await File.WriteAllBytesAsync(CONFIG_PATH, data);
        }

        public void AddCalibrationPoint(SKPoint point)
        {
            if (!IsCalibrating)
                return;

            var d = GetDepth(point);
            App.Log($"Depth {d}");

            if (d > 0 && _calibrationPoints.Count < CalibrationPointsCount)
            {
                _calibrationPoints.Add(new(point.X, point.Y, d));
#if DEBUG
                App.Log($"Calibration point count {_calibrationPoints.Count} now");
#endif
                if (_calibrationPoints.Count == CalibrationPointsCount)
                {
                    IsCalibrating = false;
#if DEBUG
                    App.Log("Calibration done!");
#endif
                    //FitPlane();
                    WallPlane();
                }
            }
        }


        bool IsPointOnPlane(Vector3 point, Vector3 planeNormal, float planeDistance)
        {
            const float epsilon = 10;
            float dist = Vector3.Dot(planeNormal, point) + planeDistance;
#if DEBUG
            App.Log($"Is point {point} on plane with planeNormal = {planeNormal} and planeDistance = {planeDistance} => {MathF.Abs(dist) < epsilon}. Distance = {MathF.Abs(dist)}");
#endif
            return MathF.Abs(dist) < epsilon;
        }

        public async void AddTouchZonePoint(int pointX, int pointY)
        {
            if (!IsSelectingTouchZone)
                return;

            var d = GetDepth(new(pointX, pointY));
            App.Log($"Depth of TouchZone {d}");

            if (d <= 0)
            {
#if DEBUG
                App.Log("Invalid depth at point");
#endif
                return;
            }

            if (_touchZoneCorner1.IsEmpty)
            {

                var worldPoint1 = _calibration.Convert2DTo3D(new(pointX,pointY), d, _sourceCamera, CalibrationGeometry.Depth);
                var x = worldPoint1.Value.X;
                var y = worldPoint1.Value.Y;
                var z = worldPoint1.Value.Z;
                _touchZoneCorner1 = DepthPoint.From(pointX, pointY, x, y, z);
                IsPointOnPlane(_touchZoneCorner1.World, _planeNormal, _planeD);
#if DEBUG
                App.Log($"Corner point1 added: Screen: v={pointX}, y={pointY}; World: x={x}, y={y}, z={z}");
#endif
            }
            else
            {
                var worldPoint2 = _calibration.Convert2DTo3D(new(pointX, pointY), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                var x = worldPoint2.Value.X;
                var y = worldPoint2.Value.Y;
                var z = worldPoint2.Value.Z;
                _touchZoneCorner2 = DepthPoint.From(pointX, pointY, x, y, z);
                IsPointOnPlane(_touchZoneCorner2.World, _planeNormal, _planeD);
#if DEBUG
                App.Log($"Corner point2 added: Screen: x={pointX}, y={pointY}; World: x={x}, y={y}, z={z}");
#endif
                IsSelectingTouchZone = false;
                IsTouchZoneSet = true;
                IsFittingPlane = false;

                await SaveConfig();
                CheckIsReadyRunTouchLoop();

            }
        }
        private List<Vector3> ScalePoints(List<Vector3> originalPoints, int fromWidth, int fromHeight, int toWidth, int toHeight)
        {
            float scaleX = (float)toWidth / fromWidth;
            float scaleY = (float)toHeight / fromHeight;

            return originalPoints.Select(p =>
                new Vector3(p.X * scaleX, p.Y * scaleY, p.Z)
            ).ToList();
        }

        private void GetNormalizedPlane(out Vector3 normal, out float d)
        {
            // Step 1: Compute vectors in the plane
            Vector3 vector1 = _calibrationPoints[1] - _calibrationPoints[0];
            Vector3 vector2 = _calibrationPoints[2] - _calibrationPoints[0];

            // Step 2: Compute the normal vector using cross product
            normal = Vector3.Cross(vector1, vector2);

            // Step 3: Normalize the normal vector
            normal = Vector3.Normalize(normal);

            // Step 4: Compute d using the plane equation
            d = -Vector3.Dot(normal, _calibrationPoints[0]);
        }

        private async void WallPlane()
        {
            if (IsFittingPlane)
                return;

            IsFittingPlane = true;

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            foreach (var point in _calibrationPoints)
            {
                minX = Math.Min((int)point.X, minX);
                minY = Math.Min((int)point.Y, minY);
                maxX = Math.Max((int)point.X, maxX);
                maxY = Math.Max((int)point.Y, maxY);
            }

            int w = maxX - minX;
            int h = maxY - minY;
            DepthPoint[] result = new DepthPoint[w * h];

            lock (_depthLock)
            {
                Parallel.For(0, result.Length, (i) =>
                {
                    int x = minX + i % w;
                    int y = minY + i / w;
                    int index = y * _fw + x;
                    float d = (float)_depthData[index];

                    var world = _calibration.Convert2DTo3D(new(x, y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                    if (d > 0 && world != null)
                    {
                        result[i] = DepthPoint.From(
                            x,
                            y,
                            world.Value.X,
                            world.Value.Y,
                            world.Value.Z
                        );
                    }
                    else
                        result[i] = DepthPoint.Empty;
                });
            }

            var points = result.Where(p => !p.IsEmpty).ToArray();
            App.Log($"Point amaount {points.Length}");
            (_planeD, _planeNormal) = await Task.Run(() => FitPlaneSVD2(points));

            //Debug.WriteLine($"Wall normal: {_planeNormal}");
            //Debug.WriteLine($"Wall equation: {_planeNormal.X:F4}x + {_planeNormal.Y:F4}y + {_planeNormal.Z:F4}z + {_planeD:F4} = 0");

            float numerator = MathF.Abs(Vector3.Dot(_planeNormal, Vector3.Zero) + _planeD);
            float denominator = _planeNormal.Length();

            if (denominator == 0)
                throw new ArgumentException("The wall plane normal cannot be a zero vector.");

            float distance = numerator / denominator;
            _cameraPosition = new Vector3(distance, 0, 0); // ← Adjust axis if wall is along Z instead of X


            await SaveConfig();
            IsWallPlaneSet = true;
            IsFloorLevelSet = true;
            IsFittingPlane = false;
            IsTouchZoneToggleEnabled = true;
            Vector3 planeN = Vector3.Zero;
            float dist = 0;
#if DEBUG
            App.Log($"Camera position {_cameraPosition}");
            App.Log($"Wall normal: {_planeNormal}");
            App.Log($"Wall equation: {_planeNormal.X:F4}x + {_planeNormal.Y:F4}y + {_planeNormal.Z:F4}z + {_planeD:F4} = 0");
            
            GetNormalizedPlane(out planeN, out dist);

            foreach (var point in _calibrationPoints)
            {
                //SKPoint p2 = new(point.X, point.Y);
                var p = _calibration.Convert2DTo3D(new(point.X, point.Y), point.Z, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                Vector3 p3 = new Vector3(p.Value.X, p.Value.Y, p.Value.Z);
                App.Log($"Checking if calibrating point on wall = {IsPointOnPlane(p3, _planeNormal, _planeD)}");
                App.Log($"Checking if calibrating point on wall alt way = {IsPointOnPlane(p3, planeN, dist)}");
                var p2 = _calibration.Convert3DTo2D(p.Value, CalibrationGeometry.Depth, CalibrationGeometry.Color);
                App.Log($"Checking if calibrating point back to screen  {p2.Value}");
            }
#endif
        }

/*
        private (float, Vector3) FitPlaneSVD(DepthPoint[] points)
        {
            int count = points.Length;

            // Step 1: Compute centroid
            Vector3 centroid = Vector3.Zero;
            foreach (var p in points)
                centroid += p.World;
            centroid /= count;

            // Step 2: Center the points
            CV.Matrix<float> mat = new CV.Matrix<float>(count, 3);
            for (int i = 0; i < count; i++)
            {
                var p = points[i].World - centroid;
                mat[i, 0] = p.X;
                mat[i, 1] = p.Y;
                mat[i, 2] = p.Z;
            }

            // Step 3: SVD
            CV.Matrix<float> w = new CV.Matrix<float>(3, 1);     // Singular values
            CV.Matrix<float> u = new CV.Matrix<float>(count, 3); // Left singular vectors
            CV.Matrix<float> vt = new CV.Matrix<float>(3, 3);    // Right singular vectors (transpose of V)

            CV.CvInvoke.SVDecomp(mat, w, u, vt, CV.CvEnum.SvdFlag.Default);

            // Plane normal = last row of V^T = last column of V
            Vector3 normal = new Vector3(vt[2, 0], vt[2, 1], vt[2, 2]);
            normal = Vector3.Normalize(normal);

            if (normal.Z > 0)
                normal = -normal;

            // Plane equation: n.X * x + n.Y * y + n.Z * z + d = 0
            float d = -Vector3.Dot(normal, centroid);

            return (d, normal);
        }
*/
        private (float, Vector3) FitPlaneSVD2(DepthPoint[] points)
        {
            int count = points.Length;

            if (count < 3)
                throw new Exception("At least three valid points are required to fit a plane.");

            Vector3 centroid = new Vector3(
                points.AsParallel().Average(p => p.X),
                points.AsParallel().Average(p => p.Y),
                points.AsParallel().Average(p => p.Z)
            );

            float[,] covariance = new float[3, 3];

            Parallel.ForEach(
                Partitioner.Create(points),
                () => new float[3, 3],
                (point, state, local) =>
                {
                    var relative = point.World - centroid;

                    local[0, 0] += relative.X * relative.X;
                    local[0, 1] += relative.X * relative.Y;
                    local[0, 2] += relative.X * relative.Z;
                    local[1, 0] += relative.Y * relative.X;
                    local[1, 1] += relative.Y * relative.Y;
                    local[1, 2] += relative.Y * relative.Z;
                    local[2, 0] += relative.Z * relative.X;
                    local[2, 1] += relative.Z * relative.Y;
                    local[2, 2] += relative.Z * relative.Z;

                    return local;
                },
                (local) =>
                {
                    lock (covariance)
                    {
                        for (int i = 0; i < 3; i++)
                            for (int j = 0; j < 3; j++)
                                covariance[i, j] += local[i, j];
                    }
                }
            );

            Matrix<float> data = new Matrix<float>(new float[,]
            {
                { covariance[0, 0] / count, covariance[0, 1] / count, covariance[0, 2] / count },
                { covariance[1, 0] / count, covariance[1, 1] / count, covariance[1, 2] / count },
                { covariance[2, 0] / count, covariance[2, 1] / count, covariance[2, 2] / count },
            });

            Matrix<float> W = new Matrix<float>(3, 3);
            Matrix<float> U = new Matrix<float>(3, 3);
            Matrix<float> Vt = new Matrix<float>(3, 3);

            CvInvoke.SVDecomp(data, W, U, Vt, Emgu.CV.CvEnum.SvdFlag.FullUV);

            Vector3 normal = new Vector3(Vt[2, 0], Vt[2, 1], Vt[2, 2]);
            normal = Vector3.Normalize(normal);

            if (normal.Z > 0)
                normal = -normal;

            float d = -Vector3.Dot(normal, centroid);

            return (d, normal);
        }

        public ushort GetDepth(SKPoint point)
        {
            return GetDepth((int)point.X, (int)point.Y);
        }

        public ushort GetDepth(int x, int y)
        {
            ushort d = 0;
            var i = y * _fw + x;

            lock (_depthLock)
            {
                if (i < _depthData.Length)
                    d = _depthData[i];
            }

            return d;
        }


        // 3D Helpers

        private Projection ProjectToFloor(Vector3 point)
        {
            if (!IsFloorLevelSet)
                throw new InvalidOperationException("Cannot project to floor if the floor plane is not set yet.");

            // 1. Define an origin on the plane.
            // For a normalized normal, origin = normal * d is on the plane.
            Vector3 origin = _planeNormal * _planeD;

            // 2. Project the point onto the plane.
            // Compute the signed distance from the point to the plane:
            float distance = Vector3.Dot(point, _planeNormal) + _planeD;
            // Remove the component along the normal:
            Vector3 projectedPoint = point - distance * _planeNormal;

            return new Projection()
            {
                Distance = distance,
                Point = projectedPoint
            };
            /*

            // 3. Define a local coordinate system on the plane.
            // Choose an arbitrary vector that is not parallel to the plane normal.
            Vector3 arbitrary = Math.Abs(Vector3.Dot(_planeNormal, Vector3.UnitY)) < 0.999f
                                ? Vector3.UnitY
                                : Vector3.UnitX;
            //// The first basis vector in the plane:
            Vector3 u = Vector3.Normalize(Vector3.Cross(_planeNormal, arbitrary));
            //// The second basis vector, orthogonal to both:
            Vector3 v = Vector3.Cross(_planeNormal, u);

            // 4. Express the projected point in the plane's local 2D coordinates.
            Vector3 relative = projectedPoint - origin;
            float uCoord = Vector3.Dot(relative, u);
            float vCoord = Vector3.Dot(relative, v);

            return (distance, new Vector3(uCoord, vCoord, 0));
            //return new Projection() 
            //{
            //    Point = new Coordinate(uCoord, vCoord),
            //    Distance = distance
            //};
            */
        }

        private Projection ProjectToFloor(DepthPoint point)
        {
            return ProjectToFloor(point.World);
        }
/*
        private IList<Vector3> ProjectTrampolineToFloor(Trampoline trampoline)
        {
            var result = new List<Vector3>();

            foreach (var v in trampoline.Vertices)
            {
                var p = ProjectToFloor(v);
                result.Add(p.Point);
            }

            return result;
        }

        private (Projection left, Projection right) ProjectSkeletonToFloor(Skeleton skeleton)
        {
            var left = ProjectToFloor(skeleton.FootLeft.PositionMm.ToVector3());
            var right = ProjectToFloor(skeleton.FootRight.PositionMm.ToVector3());
            return (left, right);
        }


        private bool IsSkeletonInsideTrampoline(Trampoline trampoline, Skeleton skeleton)
        {
            try
            {
                var (left, right) = ProjectSkeletonToFloor(skeleton);
                return IsSkeletonInsideTrampoline(trampoline, left, right);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            return false;
        }

        private bool IsSkeletonInsideTrampoline(Trampoline trampoline, Projection leftFoot, Projection rightFoot)
        {
            try
            {
                var polygon = ProjectTrampolineToFloor(trampoline);

                var isInside = GeometryHelper.IsPointInPolygon(polygon, leftFoot.Point);
                isInside = isInside || GeometryHelper.IsPointInPolygon(polygon, rightFoot.Point);

                return isInside;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            return false;
        }
*/
        private void CheckIsReadyRunTouchLoop()
        {
            IsReadyTrackTouch = IsWallPlaneSet && IsTouchZoneSet && (MaxOffset > MinOffset);
        }

        partial void OnIsRunningChanged(bool value)
        {
            ToggleTitle = IsRunning ? "Stop" : "Start";
            IsCalibrationToggleEnabled = IsRunning && !IsCalibrating && !IsFittingPlane;
            // Enable the touch zone toggle only when the app is running, the wall plane is defined,
            // we are not currently selecting the touch zone, and we are not fitting a plane.
            IsTouchZoneToggleEnabled = IsRunning && IsWallPlaneSet && !IsSelectingTouchZone && !IsFittingPlane;
            CheckIsReadyRunTouchLoop();
        }

        partial void OnCursorChanged(SKPoint value)
        {
            if (!IsRunning)
                return;
            var d = GetDepth(value);
            if (d > 0)
            {
                var p = _calibration.Convert2DTo3D(new(value.X, value.Y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth)?.ToVector3();
                p = p / 1000.0f;
                CursorLabel = $"({value.X};{value.Y}) - ({p.Value.X}; {p.Value.Y}; {p.Value.Z})m;";
            }
            else
            {
                CursorLabel = $"({value.X};{value.Y})";
            }

        }

        partial void OnIsCalibratingChanged(bool value)
        {
            IsCalibrationToggleEnabled = IsRunning && !IsCalibrating && !IsFittingPlane;

            if (_captureLoop != null)
                _captureLoop.ShouldCollectIMU = IsCalibrating;
            CheckIsReadyRunTouchLoop();
        }

        partial void OnIsFittingPlaneChanged(bool value)
        {
            IsCalibrationToggleEnabled = IsRunning && !IsCalibrating && !IsFittingPlane;

            if (IsFittingPlane)
            {
                FloorLevelStatus = "Defining...";
                FloorLevelForeground = new SolidColorBrush(Colors.DarkGray);
            }
            CheckIsReadyRunTouchLoop();
        }

        partial void OnIsTouchZoneChanged(bool value)
        {
            IsTouchZoneToggleEnabled = IsRunning && !IsCalibrating && !IsTouchZone;

            if (IsTouchZone)
            {
                TouchZoneStatus = "Defining...";
                TouchZoneForeground = new SolidColorBrush(Colors.DarkGray);
            }
            CheckIsReadyRunTouchLoop();
        }


        partial void OnIsFloorLevelSetChanged(bool value)
        {
            FloorLevelStatus = value ? "Set" : "Not Set";
            FloorLevelForeground = new SolidColorBrush(value ? Colors.Green : Colors.OrangeRed);
            CheckIsReadyRunTouchLoop();
        }
        partial void OnIsTouchZoneSetChanged(bool value)
        {
            TouchZoneStatus = value ? "Set" : "Not Set";
            TouchZoneForeground = new SolidColorBrush(value ? Colors.Green : Colors.OrangeRed);
            CheckIsReadyRunTouchLoop();
        }

        private bool IsReadySaveConfig()
        {
            return MaxOffset > MinOffset;
        }

        private async Task LoadMinOffset()
        {
            var offset = await GetConfigValue<int>("MinOffset");
            MinOffset = (offset != null) ? offset: _defaltMinOffset;
        }

        private async Task LoadMaxOffset()
        {
            var offset = await GetConfigValue<int>("MaxOffset");
            MaxOffset = (offset != null) ? offset : _defaltMaxOffset;
        }

        partial void OnMinOffsetChanged(int value)
        {
            if (IsReadySaveConfig())
            {
                _ = SaveConfig();
                CheckIsReadyRunTouchLoop();
            }
            else
            {
                _ = LoadMinOffset();
            }
        }

        partial void OnMaxOffsetChanged(int value)
        {
            if (MaxOffset > MinOffset)
            {
                _ = SaveConfig();
                CheckIsReadyRunTouchLoop();
            }
            else
            {
                _ = LoadMaxOffset();
            }
        }

        partial void OnIsReadyTrackTouchChanged(bool value)
        {
            if (IsRunning && IsReadyTrackTouch)
            {
                if (IsRunningTrackTouch) return;
                StartTouchLoop();
            }
            else if (IsRunningTrackTouch)
            {
                StopTouchLoop();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _client.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~TouchManager()
        {
            Dispose();
        }

        public static string CONFIG_FOLDER => Path.Combine(CommonFolderPath, "CPR Trampolines");
        public static string CONFIG_PATH => Path.Combine(CommonFolderPath, "CPR Trampolines", "config.json");
        public static string CommonFolderPath
        {
            get
            {
                var result = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CPR Soft");

                if (!Directory.Exists(result))
                {
                    Directory.CreateDirectory(result);
                }

                return result;
            }
        }
    }

    public enum TouchManagerEventType
    {
        NewFrame,
        CalibrationUpdate
    }

    public static class Extensions
    {
        public static SKPoint ToSKPoint(this Vector3 point)
        {
            return new SKPoint(point.X, point.Y);
        }

        public static bool IsPointInsideTriangle(this Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var v0 = c - a;
            var v1 = b - a;
            var v2 = p - a;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float denom = dot00 * dot11 - dot01 * dot01;
            if (denom == 0) return false;

            float u = (dot11 * dot02 - dot01 * dot12) / denom;
            float v = (dot00 * dot12 - dot01 * dot02) / denom;

            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }

        public static void CopyFrom(this ushort[] buffer, OB.Image image)
        {
            if (buffer.Length * sizeof(ushort) != image.SizeBytes)
                throw new ArgumentException("Image buffer size is not matching destination buffer size.");

            unsafe
            {
                ushort* source = (ushort*)image.Buffer.ToPointer();
                long size = buffer.Length * sizeof(ushort);

                fixed (ushort* destination = buffer)
                {
                    Buffer.MemoryCopy(source, destination, size, size);
                }
            }
        }

        public static void CopyFrom(this ReadOnlyBuffer<uint> buffer, ushort[] data)
        {
            Span<uint> casted = MemoryMarshal.Cast<ushort, uint>(data.AsSpan());
            buffer.CopyFrom(casted);
        }

        public static void CopyTo(this ReadWriteBuffer<uint> buffer, SKBitmap bitmap)
        {
            var bytes = MemoryMarshal.AsBytes<uint>(buffer.ToArray()).ToArray();
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        }

        public static Vector3 GetPosition(this Joint joint)
        {
            return new(joint.PositionMm.X, joint.PositionMm.Y, joint.PositionMm.Z);
        }

        public static OBSharp.Float3 ToFloat3(this Vector3 point)
        {
            return new OBSharp.Float3(point.X, point.Y, point.Z);
        }

        public static Vector3 ToVector3(this OBSharp.Float3 point)
        {
            return new Vector3(point.X, point.Y, point.Z);
        }
    }

    public class ConfigMissingException : Exception
    {
        public ConfigMissingException(string message) : base(message) { }
    }
}