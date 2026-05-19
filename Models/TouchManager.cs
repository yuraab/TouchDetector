using ABI.System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputeSharp;
#if !DISABLE_XAML_GENERATED_MAIN
using CPRLib;
#endif
using Emgu.CV;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSharp.Sensor;
using OpenCvSharp;
using SkiaSharp;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinUIEx.Messaging;
using OB = OBSharp.Sensor;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;


namespace CPRTouchVision.Models
{
    internal partial class TouchManager : ObservableObject, ICalibrationProgress, IDisposable
    {
        [ObservableProperty]
        string _toglleStartStop = "Start";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartAutoCalibration))]
        bool _isAutoMode = Constants.InitialIsAutoMode;

        [ObservableProperty]
        bool _startInAutoMode = Constants.InitialStartInAutoMode;

        [ObservableProperty]
        bool _isRunning = false;

        [ObservableProperty]
        bool _isRunningTrackTouch = false;

        [ObservableProperty]
        bool _isReadyTrackTouch = false;

        [ObservableProperty]
        SKPoint _cursor = SKPoint.Empty;

        [ObservableProperty]
        string _infoMessage = "";

        [ObservableProperty]
        bool _isError = false;

        [ObservableProperty]
        bool _isWallPlaneSet = false;

        [ObservableProperty]
        bool _isTouchZoneSet = false;

        [ObservableProperty]
        string _wallPlaneStatus = "Not set";

        [ObservableProperty]
        string _touchZoneStatus = "Not set";

        [ObservableProperty]
        Brush _wallPlaneForeground = new SolidColorBrush(Colors.OrangeRed);

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
        bool _isTouchZoneDefining = false;

        [ObservableProperty]
        bool _isCollectingFrames = false;

        [ObservableProperty]
        int _minOffset;

        [ObservableProperty]
        int _maxOffset;

        [ObservableProperty]
        int _gameScreenWidth;

        [ObservableProperty]
        int _gameScreenHeight;

        [ObservableProperty]
        private bool _isCameraPositionDefined;

        [ObservableProperty]
        private string hardwareStatusSummary;

        [ObservableProperty] 
        private bool isHardwareReady;

        public bool CanStartCalibration => IsCameraConnected;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartCalibration))]
        private bool _isCameraConnected;

        [ObservableProperty]
        private bool _allHardwareConnected;

        [ObservableProperty] 
        private bool autoStartCalibration; 
        
        [ObservableProperty] 
        private ObservableCollection<HardwareStatusItem> hardwareItems = new();

        [ObservableProperty] 
        private string calibrationStatus; 
        
        [ObservableProperty] 
        private double calibrationProgress;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartAutoCalibration))]
        private bool _isDeviceChecking;

        public bool CanStartAutoCalibration => IsAutoMode && AllHardwareConnected && !IsDeviceChecking;

        public event EventHandler HardwareChecksCompleted;

        private List<Vector2> _pendingColorROI;
        private TaskCompletionSource<List<Vector2>>? _depthROIRequest;
        private int getROIAttempts;


        private void SetInfoMessage(string message, bool isError = false)
        {
            InfoMessage = message;
            IsError = isError;
        }

        // This command can be bound directly to a Button's Command property
        [RelayCommand(CanExecute = nameof(CanRetry))]

        private async Task RunAllHardwareChecksAsync()
        {
            IsDeviceChecking = true;

            SetInfoMessage("Checking hardware connections...");

            // Reset all statuses to Pending before starting
            foreach (var item in HardwareItems) item.Status = StatusCode.Pending;

            // Run all checks in parallel
            var tasks = _checkers.Select(c => c.CheckConnection());
            await Task.WhenAll(tasks);

            IsDeviceChecking = false;

            SetInfoMessage("Hardware check completed.");

            UpdateHardwareReadiness();

            // Notify MainWindow listening 
            HardwareChecksCompleted?.Invoke(this, EventArgs.Empty);
        }

        // request frame for calibration,
        // MainWindow will call this when auto-calibration is started,
        // and complete the TaskCompletionSource when the frame is ready
        private TaskCompletionSource<CalibrationFrame>? _calibrationRequest;

        // To give BackgroundLoop access to the _calibrationRequest
        public TaskCompletionSource<CalibrationFrame>? PendingCalibrationRequest { get; set; }


        public Task<CalibrationFrame> GetNextCalibrationFrameAsync(System.Threading.CancellationToken token)
        {
            App.Log("Redirect frame request to CaptureLoop");

            // Pass the request directly to the loop that handles the hardware
            return _captureLoop.RequestFrame(token);
        }

        private bool CanRetry() => !IsDeviceChecking;

        public void OnStatus(string message) 
        { 
            CalibrationStatus = message;
            App.Log(message);
        }

        partial void OnCalibrationStatusChanged(string value)
        {
            SetInfoMessage(value);
            //App.Log(value);
        }

        partial void OnInfoMessageChanged(string value)
        {
            if (value.StartsWith("(")) return; // not log coordinates
            App.Log(value);
        }

        partial void OnAllHardwareConnectedChanged(bool value)
        {
            OnPropertyChanged(nameof(IsHardwareReady));
            OnPropertyChanged(nameof(HardwareErrorMessage));

            if (!value)
            {
                AutoStartCalibration = false;
                IsAutoMode = false;
            }
            else
            {
                IsAutoMode = Constants.InitialIsAutoMode;
                AutoStartCalibration = Constants.InitialStartInAutoMode;
            }

            OnPropertyChanged(nameof(CanStartAutoCalibration));
        }


        public void OnProgress(double percent) 
        { 
            CalibrationProgress = percent; 
        }
        //public void OnPointDetected(Point3D point)
        //{ // Optional: draw on canvas or store points }

        // Message to show when hardware is missing
        public string HardwareErrorMessage => AllHardwareConnected
            ? ""
            : "Please ensure all devices are connected to start.";

        private bool IfDetectedZoneMeetExpectations(Point[] points)
        {
            if (points == null || points.Length != TouchZonePointsCount)
            {
                App.Log($"Expected {TouchZonePointsCount} points, but got {(points == null ? 0 : points.Length)}.");
                return false;
            }
            for (int i = 0; i < TouchZonePointsCount; i++)
            {
                var p = points[i % TouchZonePointsCount];
                if (p.X < 0 || p.Y < 0)
                {
                    App.Log($"Point {i} has invalid coordinates: ({p.X}, {p.Y}).");
                    return false;
                }
                int nextIndex = (i + 1) % TouchZonePointsCount;
                if (p.DistanceTo(points[nextIndex]) < Constants.TouchZoneDistanceThreshold)
                {
                    App.Log($"Point {i}: {p.X}/{p.Y} is too close to the next point: {points[nextIndex].X}/{points[nextIndex].Y}.");
                    return false;
                }
            }
            return true;
        }

        public async void OnCompleted(Point[] points) 
        { 
            if(IfDetectedZoneMeetExpectations(points))            {
                SetInfoMessage("Touch zone detected successfully.");

                foreach (var point in points)
                {
                    //var projectedPoint = Convert2DToDepthPoint((int) point.X, (int) point.Y);
                    var projectedPoint = DepthPoint.From((int)point.X, (int)point.Y, 0, 0, 0);
                    _touchZonePoints.Add(projectedPoint);
                    App.Log($"Detected touch zone point at ({point.X}, {point.Y}), projected to depth point ({_touchZonePoints.Last().SX}, {_touchZonePoints.Last().SY}, {_touchZonePoints.Last().X}, {_touchZonePoints.Last().Y}, {_touchZonePoints.Last().Z})");
                }

                await FinalizeTouchZoneAsync();
            }
            else
            {
                OnFailed(Constants.ZoneDetectionUnexpected);
                return;
            }
             
        } 
        public void OnFailed(string reason) 
        { 
            CalibrationStatus = $"{Constants.FailureIndicatorMessage}: {reason}";
            _isError = true;
            switch (reason)
            {
                case string s when s.StartsWith(Constants.ZoneDetectionFailed):
                    //InfoMessage = "Calibration failed: No valid points detected. Please ensure the camera has a clear view of the wall and try again.";
                    Changed?.Invoke(this, TouchManagerEventType.CalibrationFailed);
                    break;
                case "Camera position not defined":
                    SetInfoMessage(Constants.CalibrationFailedCameraPosition, true);
                    break;
                default:
                    SetInfoMessage($"Calibration failed: {reason}", true);
                    break;
            }  
        }


        private readonly Dictionary<string, StatusCode> _hardwareStates = new();
        
        private readonly List<IHardwareChecker> _checkers = new();

        public IReadOnlyList<IHardwareChecker> Checkers => _checkers;


        private void UpdateHardwareReadiness()
        {
            IsCameraConnected = HardwareItems.Any(x =>
                x.Name == "Camera" && x.Status == StatusCode.Connected);

            // Check if every item in the list has a 'Connected' status
            AllHardwareConnected = HardwareItems.Count > 0 &&
                                   HardwareItems.All(x => x.Status == StatusCode.Connected);
        }

        public DispatcherQueue UIDispatcherQueue { get; set; }
        
        private bool _disposed = false;
        private Calibration _calibration = new();
        public Calibration Calibration => _calibration;
        private Transformation? _transformation;

        // Getting data from the camera
        private CaptureLoop? _captureLoop;

        // Prosessing the data captured
        private TouchLoop? _touchLoop;

        private ushort[] _depthData = [];
        private ushort[] _nativeDepthData = [];

        private List<DepthPoint> _touchZonePoints = new();

        private List<TouchEvent> _touches = new();
        public List<TouchEvent> Touches => _touches;

        private float _planeD = float.MinValue;
        private Vector3 _planeNormal = Vector3.Zero;
        private Vector3 _cameraPosition = Vector3.Zero;

        private ITouchVolume _detectableSpace;
        public ITouchVolume DetectableSpace => _detectableSpace;

        private List<ExclusionZone>? _exclusionZones;
        public ExclusionZone[]? ExclusionZones => _exclusionZones?.ToArray();

        private System.Numerics.Quaternion _cameraRotation = System.Numerics.Quaternion.Identity;
        private DepthVisualizer _depthVisualizer;

#if !DISABLE_XAML_GENERATED_MAIN
        private PipeServer _server = new PipeServer(PipeName.TouchVision);
#endif

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

        private readonly object _depthLock = new object();
        private readonly int _defaltMaxOffset = 15;
        private readonly int _defaltMinOffset = 5;
        private readonly int _defaultGameScreenWidth = 1920;
        private readonly int _defaultGameScreenHeight = 1080;
        private OSCClient _oscClient = new();

        public DoubleBufferedBitmap ColorBitmap => _colorBitmap;
        public DoubleBufferedBitmap DepthBitmap => _depthBitmap;
        public int FW => _fw;
        public int FH => _fh;

        public int FrameWidth { get => _frameWidth; set => _frameWidth = value; }
        public int FrameHeight { get => _frameHeight; set => _frameHeight = value; }    

        public float PlaneD => _planeD;
        public Vector3 PlaneNormal => _planeNormal;
        public Vector3 CameraPosition => _cameraPosition;

        public DepthPoint[] TouchZonePoints => _touchZonePoints.ToArray();

        public EventHandler<TouchManagerEventType>? Changed;

        private UdpClient _client = new();
        private bool _isConfigGotten = false;

#if DEBUG || DISABLE_XAML_GENERATED_MAIN
        private bool _isSingleOutputDone;
        
#endif
        public const int CalibrationPointsCount = 3;
        public const int TouchZonePointsCount = 4;
        private bool isImageSaved;
        private bool _isReadyReceiveNewCapture = false;
        private ushort _maxWallDepth = 1;
        private ushort _minWallDepth = ushort.MaxValue;
        private bool _floorCamera; // camera is mounted on the floor/ceil

        private WindowAccumulator? _winAccum = null;
        private readonly object _collectLock = new();
        private int _targetFrames = 30;        
        private int _accCapacityPerPixel = 30;

        public TouchManager()
        {
            _fsize = _fw * _fh;
            _depthData = new ushort[_fsize];
            _depthVisualizer = new(_fw, _fh);
            _colorBitmapInfo = new(_fw, _fh, SKColorType.Bgra8888, SKAlphaType.Premul);
            _colorBitmap = new DoubleBufferedBitmap(_colorBitmapInfo);
            _depthBitmap = new DoubleBufferedBitmap(_colorBitmapInfo);
            MaxOffset = _defaltMaxOffset;
            MinOffset = _defaltMinOffset;
            GameScreenWidth = _defaultGameScreenWidth;
            GameScreenHeight = _defaultGameScreenHeight;
#if !DISABLE_XAML_GENERATED_MAIN
            _server.Start();
            _server.MessageReceived += OnMessageReceived;
#endif
            IsCameraPositionDefined = false;
        }

        public void RegisterHardware(IHardwareChecker checker) 
        {
            // 1. Store the logic object so CheckConnection() can be called in the loop
            _checkers.Add(checker);

            // 2. Create the UI object
            var newItem = new HardwareStatusItem { 
                Name = checker.DeviceName, 
                Status = StatusCode.Pending 
            };

            // 3. Add to the UI collection (Ensure HardwareItems is an ObservableCollection)
            HardwareItems.Add(newItem);

            // 4. Hook up the event with the UI Dispatcher
            checker.OnStatusChanged += (status) =>
            {
                // Use the dispatcher you assigned in MainWindow!
                UIDispatcherQueue?.TryEnqueue(() =>
                {
                    newItem.Status = status;
#if DEBUG || TEST
                    Debug.WriteLine($"newItem.Status = {newItem.Status}");
                    Debug.WriteLine($"status = {status}");
#endif
                    // Update the global state
                    UpdateHardwareReadiness();
                });
            };
        }

        /*
        private void UpdateHardwareUI() 
        { 

            IsHardwareReady = _hardwareStates.Values.All(s => s == StatusCode.Connected); 
        }

        private void UpdateHardwareSummary() 
        { 
            HardwareStatusSummary = string.Join(", ", 
                _hardwareStates.Select(kvp => $"{kvp.Key}: {kvp.Value}")); 
        }
        */

        public void Undo()
        {
            if (IsSelectingTouchZone && _touchZonePoints.Count > 0)
                _touchZonePoints.RemoveAt(_touchZonePoints.Count - 1);
        }

        public void StartStopCapture()
        {
            //App.Log($"CanStartAutoCalibration = {CanStartAutoCalibration}; IsAutoMode = {IsAutoMode}; CanStartCalibration = {CanStartCalibration}");
            if (IsRunning)
            {
                Stop();
            }             
            else
            {
                Start();
            }
        }
        private async Task Start()
        {
            App.Log("Trying Start Capture");
            if (IsRunning || !Device.TryOpen(out var device)) return;

            App.Log("Start Capture");
            _captureLoop = new(device);
            _captureLoop.CaptureReady += OnCaptureReady;
            _captureLoop.LoopFailed += OnLoopFailed;
            _captureLoop.GetCalibration(out _calibration);
            _captureLoop.CameraPositionReady += OnCameraPositionReady;
            
            _transformation = new Transformation(_calibration);

            _depthWidth = _captureLoop.GetDepthResolution().width;
            _depthHeight = _captureLoop.GetDepthResolution().height;

            _captureLoop.Run();
            await StartTouchLoop();
            IsRunning = true;

        }

        private async Task StartTouchLoop()
        {
            CheckIsReadyRunTouchLoop();
#if DEBUG || TEST
            App.Log($"Track Touch Loop ready to start = {IsReadyTrackTouch} because  IsWallPlaneSet = {IsWallPlaneSet} && IsTouchZoneSet = {IsTouchZoneSet} && MaxOffset > MinOffset = {MaxOffset > MinOffset} && !IsFittingPlane = {!IsFittingPlane}");
#endif
            if (IsRunningTrackTouch || !IsReadyTrackTouch) return;

            if (!useRawDepthData)
            {
                App.Log("Using aligned depth data for touch detection");

                _detectableSpace = new TouchVolume(_planeNormal,
                                                    _planeD,
                                                    MinOffset * 10,   //convert from centimeters
                                                    MaxOffset * 10,   //convert from centimeters
                                                    TouchZonePoints,
                                                    _minWallDepth, _maxWallDepth,
                                                    _depthWidth, _depthHeight,
                                                    _calibration
                                                   );
            }
            else
            {
                App.Log("Using LUT touch volume for touch detection");

                _detectableSpace = await LutTouchVolume.CreateAsync(
                                                _planeNormal,
                                                _planeD,
                                                MinOffset * 10,   //convert from centimeters
                                                MaxOffset * 10,   //convert from centimeters
                                                depthSpaceTouchZone.ToArray(),
                                                _depthWidth, _depthHeight,
                                                _calibration
                                               );
            }

            _touchLoop = new(_detectableSpace, _calibration, _exclusionZones);

            _touchLoop.TouchFrameReady += OnTouchFrameReady;
            _touchLoop.TouchLoopFailed += OnTouchLoopFailed;
            _touchLoop.ReadyForNewImage += OnReadyForNewImage;
            
            if  (!_touchLoop._isRunning) 
            {
                _touchLoop.Run();
                OnStatus("Touch detection is running");
            }
            else
                App.Log("Touch loop is already running!!!");

            IsRunningTrackTouch = true;

            _isReadyReceiveNewCapture = true;
            //_detectableSpace.WallNotAligned += OnWallNotAligned;

#if TEST || MOCK
            isImageSaved = false;
#endif

        }

        private void OnWallNotAligned()
        {
            App.Log("Wall is not aligned to camera");
            ResetTouchZone();
        }

        private void OnReadyForNewImage(object? sender, bool isReady)
        {
            if (isReady) _isReadyReceiveNewCapture = true;
        }

        private void OnTouchFrameReady(object? sender, TouchFrame e)
        {
            int idCounter = 0;

            if (e.Clusters == null || e.Clusters.Count == 0) return;
            _touches.Clear();
            var time = DateTime.UtcNow;
            foreach (var c in e.Clusters) 
            {
                if (c.NormalizedCenter.X < 0 || c.NormalizedCenter.X > 1) continue;
                if (c.NormalizedCenter.Y < 0 || c.NormalizedCenter.Y > 1) continue;
                OBSharp.Float2 center = new OBSharp.Float2(0,0);
                if (c.Center3D == Vector3.Zero)
                {
                    c.Center3D = _detectableSpace.Get3DPointFromLocal2DPoint(c.Center);
                }

                var center2D = _calibration.Convert3DTo2D(new(c.Center3D.X, c.Center3D.Y, c.Center3D.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
                center = (center2D.HasValue) ? center2D.Value : new OBSharp.Float2(0,0);

                var right = GetScreenPointFromPlanePoint(new (c.Center.X + c.Radius, c.Center.Y)); // right
                var left =  GetScreenPointFromPlanePoint(new (c.Center.X - c.Radius, c.Center.Y)); // left
                var down =  GetScreenPointFromPlanePoint(new (c.Center.X, c.Center.Y + c.Radius)); // up
                var up =    GetScreenPointFromPlanePoint(new (c.Center.X, c.Center.Y - c.Radius)); // down


                _touches.Add(new TouchEvent
                {
                    Id = idCounter++,
                    X = center.X,
                    Y = center.Y,
                    NormalizedX = c.NormalizedCenter.X,
                    NormalizedY = c.NormalizedCenter.Y,
                    ScreenRadius = (Vector2.Distance(right, left) + Vector2.Distance(up, right)) / 2f,
                    Radius = c.Radius,
                    Timestamp = time
                });
#if DEBUG || TEST
                var t = _touches.Last();
                string message = $"[id:{t.Id} Local Center:({c.Center}) Screen Center:({t.X:F2},{t.Y:F2}) normalizedCenter:({t.NormalizedX}:{t.NormalizedY}) r:{t.Radius:F2}]";
                App.Log(message);
#endif

            }

            Task.Run(() => _oscClient.Send(_touches));

            Changed?.Invoke(this, TouchManagerEventType.NewFrame);
        }
         private Vector2 GetScreenPointFromPlanePoint(OBSharp.Float2 point)
        {
            var point3D = _detectableSpace.Get3DPointFromLocal2DPoint(new (point.X, point.Y));
            var p = _calibration.Convert3DTo2D(new(point3D.X, point3D.Y, point3D.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
            return p.HasValue ? new (p.Value.X, p.Value.Y) : new Vector2(0,0);
        }
        private void OnLoopFailed(object? sender, LoopFailedEventArgs e)
        {
            App.Log("Capture loop failed");
        }

        private void OnTouchLoopFailed(object? sender, Exception e)
        {
            App.Log("Touch loop failed");
        }

        private void OnCameraPositionReady(object? sender, bool onFloorMounted)
        {
            var where = onFloorMounted ? "floor" : "ceil";
            
            App.Log($"Camera is mounted on the {where}");
            if (onFloorMounted != _floorCamera)
            {
                var whereWas = _floorCamera == null ? "not defined" : _floorCamera ? "floor" : "ceil";
                App.Log($"Camera position was changed from {whereWas} to {where}");
                
                _floorCamera = onFloorMounted;
                IsCameraPositionDefined = true;

                // Redo calibration if position of camera was changed
                // Make sure StartDefineZone runs on UI thread
                UIDispatcherQueue.TryEnqueue(() => StartDefineZone()); 
            }
            else
            {
                _floorCamera = onFloorMounted; // ensure it's assigned (first run)
                IsCameraPositionDefined = true;
                CheckIsReadyRunTouchLoop();
                StartTouchLoop();
            }

        }

        private void OnChangedCameraPositionDefined()
        {
            CheckIsReadyRunTouchLoop();
        }

        private void OnCaptureReady(object? sender, CaptureLoopEventArgs e)
        {
            var now = DateTime.UtcNow;

            if (e.Capture == null || e.Capture.IsDisposed)
                return;

            using var capture = e.Capture;

            //
            // COLOR
            //

            using var colorImage = capture.ColorImage;

            if (colorImage != null)
            {
                if (!_floorCamera)
                    FlipColor180(colorImage);

                _colorBitmap.Update(bitmap =>
                {
                    bitmap.InstallPixels(
                        _colorBitmapInfo,
                        colorImage.Buffer);
                });

                //
                // Calibration frame request
                //

                if (_calibrationRequest != null &&
                    !_calibrationRequest.Task.IsCompleted)
                {
                    int totalBytes = colorImage.SizeBytes;

                    byte[] managedColorData =
                        new byte[totalBytes];

                    Marshal.Copy(
                        colorImage.Buffer,
                        managedColorData,
                        0,
                        totalBytes);

                    _calibrationRequest.TrySetResult(
                        new CalibrationFrame
                        {
                            ColorData = managedColorData,
                            Width = colorImage.WidthPixels,
                            Height = colorImage.HeightPixels
                        });

                    _calibrationRequest = null;

                    App.Log("Calibration color frame captured.");
                }
            }

            //
            // NATIVE DEPTH
            //

            using var depthImage = capture.DepthImage;
             
                if (depthImage == null)
                    return;

                if (!_floorCamera)
                    FlipDepth180(depthImage);

                //
                // MAIN PIPELINE USES NATIVE DEPTH
                //
                int required =
                    depthImage.WidthPixels *
                    depthImage.HeightPixels;

                if (_nativeDepthData.Length != required)
                {
                    _nativeDepthData =
                        new ushort[required];
                }

                _nativeDepthData.CopyFrom(depthImage);

                //
                // Optional visualization
                /*

                _depthBitmap.Update(bitmap =>
                {
                    bitmap.Pixels =
                        _depthVisualizer.Update(_nativeDepthData);
                });

                */
                // Calibration accumulation
                //

                if (IsCollectingFrames)
                {
                    WindowAccumulator? acc;

                    lock (_collectLock)
                        acc = _winAccum;

                    acc?.AddFrame(_nativeDepthData);
                }

                if (_depthROIRequest != null)
                {

                    App.Log("Got the ROI request");
                    try
                    {
                        var result = new List<Vector2>();
#if DEBUG || TEST
                        App.Log(
                            $"Having {_touchZonePoints.Count} Depth point(s)");

#endif
                        //
                        // Use ORIGINAL native depth image
                        //



                        foreach (var p in _touchZonePoints)
                        {
                            var depthPoint =
                                _calibration.ConvertColor2DToDepth2D(
                                    new OBSharp.Float2(p.SX, p.SY),
                                    depthImage);
#if DEBUG || TEST
                            App.Log(
                                $"Trying convert Color point ({p.SX}, {p.SY}) to Depth point");
#endif

                            if (depthPoint != null)
                            {
                                result.Add(new Vector2(
                                    depthPoint.Value.X,
                                    depthPoint.Value.Y));

    #if DEBUG || TEST
                                App.Log(
                                    $"Color point ({p.SX}, {p.SY}) " +
                                    $"-> depth point " +
                                    $"({depthPoint.Value.X}, {depthPoint.Value.Y})");
    #endif
                            }
                            else
                            {

                                throw new Exception($"Failed to convert color point ({p.SX}, {p.SY}) to depth space.");
                            }
                        }

                        _depthROIRequest.TrySetResult(result);
                        ResetROIAttemps();
                    }
                    catch (Exception ex)
                    {
                        getROIAttempts++;
#if DEBUG || TEST
                        App.Log(
                             $"Attempt {getROIAttempts}: Failed to get ROI points in depth space. Error: {ex.Message}");
#endif
                        if (getROIAttempts == Constants.MaxROIAttempts)
                        {
                            _depthROIRequest.TrySetResult(new List<Vector2>());
                            App.Log("Failed to get ROI after maximum attempts. Returning empty result.");
                            ResetROIAttemps();
                        }
                    }
                    finally
                    {
                        if (getROIAttempts == Constants.MaxROIAttempts)
                        {
                            _depthROIRequest.TrySetResult(new List<Vector2>());
                            App.Log("Failed to get ROI after maximum attempts. Returning empty result.");
                            ResetROIAttemps();
                        }
                        else
                        {
                            App.Log("ROI request completed");
                        }
                         
                    }
                }

                //
                // Runtime touch processing
                //

                if (_isReadyReceiveNewCapture &&
                    _touchLoop != null)
                {
                    _ = _touchLoop.TrySendImage(
                        _nativeDepthData,
                        depthImage.WidthPixels,
                        depthImage.HeightPixels,
                        now);
                }
            
            Changed?.Invoke(
                this,
                TouchManagerEventType.NewFrame);
        }

        private void StartROIAttemps()
        {
            _depthROIRequest =new TaskCompletionSource<List<Vector2>>();
            getROIAttempts = 0;
        }

        private void ResetROIAttemps()
        {
            _depthROIRequest = null;
            getROIAttempts = 0;
        }


        // Unsafe helper for color flip (4 bytes per pixel assumed, RGBA)
        private unsafe void FlipColor180(OB.Image colorImage)
        {
            byte* ptr = (byte*)colorImage.Buffer;
            int totalPixels = colorImage.WidthPixels * colorImage.HeightPixels;
            int bpp = 4; // RGBA

            // Use 32-bit chunks for faster swapping
            uint* start = (uint*)ptr;
            uint* end = (uint*)(ptr + (totalPixels - 1) * bpp);

            while (start < end)
            {
                uint tmp = *start;
                *start = *end;
                *end = tmp;

                start++;
                end--;
            }
        }

        // Unsafe helper for depth flip (ushort per pixel)
        private unsafe void FlipDepth180(OB.Image img)
        {
            if (img == null) return;

            int width = img.WidthPixels;
            int height = img.HeightPixels;
            int totalPixels = width * height;

            ushort* ptr = (ushort*)img.Buffer;

            // Flip entire image by swapping symmetric pixels
            for (int i = 0; i < totalPixels / 2; i++)
            {
                int opposite = totalPixels - 1 - i;

                ushort tmp = ptr[i];
                ptr[i] = ptr[opposite];
                ptr[opposite] = tmp;
            }
        }

        private void GetDepthImageWithoutFilter(Image depthImage)
        {

            using var aligned = new OB.Image(OB.ImageFormat.Depth16, _fw, _fh);
            _transformation?.DepthImageToColorCamera(depthImage, aligned);

            if (!_floorCamera)
                FlipDepth180(aligned);
            _depthData.CopyFrom(aligned);
            /*
            _depthBitmap.Update((bitmap) =>
            {
                bitmap.Pixels = _depthVisualizer.Update(_depthData);
            });
            */
        }

        private void StopTouchLoop()
        {
            if (_touchLoop != null)
            {
                _touchLoop.TouchFrameReady -= OnTouchFrameReady;
                _touchLoop.TouchLoopFailed -= OnTouchLoopFailed;

                _touchLoop.Dispose();
                _touchLoop = null;
            }
        _isReadyReceiveNewCapture = false;

        IsRunningTrackTouch = false;
        }

        private void RestartTouchLoop()
        {
            if (IsRunning)
            {
                StopTouchLoop();
                StartTouchLoop();
            }
        }

        private void Stop()
        {
            App.Log("trying to Stop Capture");
            if (!IsRunning) return;
            
            if (_captureLoop != null)
            {
                _captureLoop.CaptureReady -= OnCaptureReady;
                _captureLoop.LoopFailed -= OnLoopFailed;
                _captureLoop.Dispose();
                _captureLoop = null;
            }

            StopTouchLoop();

            _colorBitmap = new(_colorBitmapInfo);
            _depthBitmap = new(_colorBitmapInfo);

            IsRunning = false;
            App.Log($"CanStartAutoCalibration = {CanStartAutoCalibration}");
            Debug.WriteLine("Capture stopped");
            Debug.WriteLine("CanStartAutoCalibration = " + CanStartAutoCalibration);

        }

        public void StartCalibration()
        {
            // Run UI code on the main thread
            if (UIDispatcherQueue != null)
            {
                UIDispatcherQueue.TryEnqueue(() =>
                {
                    // UI updates go here
                    PerformCalibration();
                });
            }
            else
            {
                // fallback if DispatcherQueue not set
                PerformCalibration();
            }
        }

        private void PerformCalibration()
        {
            if (IsSelectingTouchZone || IsTouchZoneDefining )
                return;

            IsWallPlaneSet = false;
            IsSelectingTouchZone = true;
            ResetTouchZone();
        }

        private void ResetTouchZone()
        {
            _touchZonePoints.Clear();
            IsTouchZoneSet = false;
            CheckIsReadyRunTouchLoop();
            StopTouchLoop();
        }

        public void StartDefineZone()
        {
            App.Log("Start Define Touch Zone");

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
                    StartInAutoMode = config.StartInAutoMode ?? true;
                    if (StartInAutoMode)
                    {
                        IsAutoMode = true;
                        AutoStartCalibration = true;
                    }
                    else
                    {
                        IsAutoMode = config.IsAutoMode ?? true;

                        if (config.PlaneD.HasValue && config.PlaneNormal.HasValue)
                        {
                            _planeD = config.PlaneD.Value;
                            _planeNormal = config.PlaneNormal.Value;
                            _cameraRotation = config.CameraRotation.HasValue ? config.CameraRotation.Value : System.Numerics.Quaternion.Identity;
                            _cameraPosition = config.CameraPosition.HasValue ? config.CameraPosition.Value : Vector3.Zero;
                            IsWallPlaneSet = true;
                        }

                        Debug.WriteLine($"D: {config.PlaneD}; Normal: {config.PlaneNormal}");

                        if (IsWallPlaneSet && config.TouchZoneCorners != null && config.TouchZoneCorners.Count() == 4)
                        {
                            _touchZonePoints = config.TouchZoneCorners.ToList();
                            IsTouchZoneSet = true;
                            _exclusionZones = (config.ExclusionZones != null && config.ExclusionZones.Length > 0) ? config.ExclusionZones.ToList() : null;
                        }
                    }
                    MinOffset = config.MinOffset ?? _defaltMinOffset;
                    MaxOffset = config.MaxOffset ?? _defaltMaxOffset;
                    _minWallDepth = config.MinWallDepth ?? 0;
                    _maxWallDepth = config.MaxWallDepth ?? ushort.MaxValue;
                    GameScreenWidth = config.GameScreenWidth ?? _defaultGameScreenWidth;
                    GameScreenHeight = config.GameScreenHeight ?? _defaultGameScreenHeight;
                    _floorCamera = config.FloorCamera ?? true;
                    
                }
            }
            else
            {
                MinOffset = _defaltMinOffset;
                MaxOffset = _defaltMaxOffset;
                GameScreenWidth = _defaultGameScreenWidth;
                GameScreenHeight = _defaultGameScreenHeight;
                IsWallPlaneSet = false;
                IsTouchZoneSet = false;
                _minWallDepth = 0;
                _maxWallDepth = ushort.MaxValue;
                _floorCamera = true; // default
                IsAutoMode = true;

            }
                _isConfigGotten = true;
            
        }

        public async Task SaveConfig()
        {

            if (!_isConfigGotten) return; // To avoid a call SaveConfig before Config was loaded
            var config = new Config(
                GameScreenWidth,
                GameScreenHeight,
                MinOffset,
                MaxOffset,
                _minWallDepth,
                _maxWallDepth,
                _planeD == float.MinValue ? null : _planeD,
                _planeNormal == Vector3.Zero ? null : _planeNormal,
                TouchZonePoints,
                _cameraPosition,
                _cameraRotation.IsIdentity ? null : _cameraRotation,
                _floorCamera,
                ExclusionZones,
                StartInAutoMode,
                IsAutoMode
            );
            var json = JsonConvert.SerializeObject(config) ?? "";
#if DEBUG
            App.Log($"Saved Data:{json}");
#endif
            var data = Encoding.UTF8.GetBytes(json);
            Directory.CreateDirectory(CONFIG_FOLDER);
            await File.WriteAllBytesAsync(CONFIG_PATH, data);
        }

        private bool IsPointOnPlane(Vector3 point, Vector3 planeNormal, float planeDistance)
        {
            const float epsilon = 10;
            float dist = Vector3.Dot(planeNormal, point) + planeDistance;
#if DEBUG || TEST
            App.Log($"Is point {point} on plane with planeNormal = {planeNormal} and planeDistance = {planeDistance} => {MathF.Abs(dist) < epsilon}. Distance = {MathF.Abs(dist)}");
#endif
            return MathF.Abs(dist) < epsilon;
        }

        DepthPoint ProjectPointOntoPlane(Vector3 point)
        {
            float distance = Vector3.Dot(_planeNormal, point) + _planeD;

            var point3D = point - _planeNormal * distance;
            var point2D = _calibration.Convert3DTo2D(new(point3D.X, point3D.Y, point3D.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
            if (point2D.HasValue)
            {
#if DEBUG
                App.Log($"Point {point} has Distance {distance} to  plane. Project it to plane {point3D}. New screen coordinates {point2D}");
#endif
                return DepthPoint.From((int)point2D.Value.X, (int)point2D.Value.Y, point3D.X, point3D.Y, point3D.Z);
            }
            return DepthPoint.Empty;
        }


        Vector3 Convert2DTo3DPoint(int pointX, int pointY, ushort d)
        {
            var worldPoint1 = _calibration.Convert2DTo3D(new(pointX, pointY), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
            return worldPoint1.HasValue ? worldPoint1.Value.ToVector3() : Vector3.Zero;
        }
        public DepthPoint Convert2DToDepthPoint(int pointX, int pointY)
        {
            var d = GetDepth(new(pointX, pointY));

            if (d <= 0)
            {
#if DEBUG
                App.Log("Invalid depth at point");
#endif
                return DepthPoint.Empty;
            }

            var p3D = Convert2DTo3DPoint(pointX, pointY, d);
#if DEBUG
            App.Log($" Corner point => {p3D}. Depth => {d}");
#endif
            return DepthPoint.From(pointX, pointY, p3D.X, p3D.Y, p3D.Z);  //ProjectPointOntoPlane(p3D);
        }

        private async Task FinalizeTouchZoneAsync(List<DepthPoint>? points =null) 
        { 
            if (points != null)
            {
                _touchZonePoints.Clear();
                _touchZonePoints.AddRange(points);
            }
             
            IsFittingPlane = false; 
            await WallPlaneStat(); 
            await SaveConfig(); 
            IsTouchZoneSet = true; 
            CheckIsReadyRunTouchLoop(); 
        }

        public async void AddTouchZonePoint(int pointX, int pointY)
        {
            if (!IsSelectingTouchZone)
                return;

            IsSelectingTouchZone = true;

            var projectedPoint = Convert2DToDepthPoint(pointX, pointY);

            if (projectedPoint.IsEmpty) return;

            if (_touchZonePoints.Count < TouchZonePointsCount)
                _touchZonePoints.Add(projectedPoint);

            if (_touchZonePoints.Count == TouchZonePointsCount)
            {
                // _touchZonePoints = CornerPointsSorter(_touchZonePoints);
                IsSelectingTouchZone = false;

                await FinalizeTouchZoneAsync(_touchZonePoints.ToList());
            }

        }

        private List<DepthPoint> CornerPointsSorter(List<DepthPoint> points)
        {
            if (points.Count != 4)
                throw new ArgumentException($"Touch zone points should be 4. Actual count {_touchZonePoints.Count}");

            DepthPoint center = DepthPoint.From(
                (int)points.Average(p => p.SX),
                (int)points.Average(p => p.SY),
                points.Average(p => p.X),
                points.Average(p => p.Y),
                points.Average(p => p.Z)
                );

            var topLeft = points.OrderBy(p => p.X).ThenBy(p => p.Y).FirstOrDefault(p => p.X < center.X && p.Y < center.Y);
            var topRight = points.OrderByDescending(p => p.X).ThenBy(p => p.Y).FirstOrDefault(p => p.X > center.X && p.Y < center.Y);

            var bottomLeft = points.OrderBy(p => p.X).ThenByDescending(p => p.Y).FirstOrDefault(p => p.X < center.X && p.Y > center.Y);
            var bottomRight = points.OrderByDescending(p => p.X).ThenByDescending(p => p.Y).FirstOrDefault(p => p.X > center.X && p.Y > center.Y);
            var sorted_points = new List<DepthPoint> { topLeft, topRight, bottomRight, bottomLeft };
            return _floorCamera ? sorted_points : Flip180(sorted_points);
        }

        private List<DepthPoint> Flip180(List<DepthPoint> points)
        {
            return new List<DepthPoint>
            {
                points[2],  // bottom-right => top-left
                points[3],  // bottom-left => top-right
                points[0],  // top-left => bottom-right
                points[1]   // bottom-left => top-right
            };
        }
        private List<Vector3> ScalePoints(List<Vector3> originalPoints, int fromWidth, int fromHeight, int toWidth, int toHeight)
        {
            float scaleX = (float)toWidth / fromWidth;
            float scaleY = (float)toHeight / fromHeight;

            return originalPoints.Select(p =>
                new Vector3(p.X * scaleX, p.Y * scaleY, p.Z)
            ).ToList();
        }

        private async Task<(bool, int, int, int, int)> GetCalibrationWindow(
            List<Vector2> points, 
            int width, int height, int extra = 0)
        {
            StartROIAttemps();

            List<Vector2> depthPoints = await _depthROIRequest.Task;

            if (depthPoints.Count < 4)
                return (false, 0, 0, width, height);

            //
            // Compute bounds
            //

            int minX =
                Math.Max(
                    0,
                    (int)depthPoints.Min(p => p.X) - extra);

            int minY =
                Math.Max(
                    0,
                    (int)depthPoints.Min(p => p.Y) - extra);

            int maxX =
                Math.Min(
                    width - 1,
                    (int)depthPoints.Max(p => p.X) + extra);

            int maxY =
                Math.Min(
                    height - 1,
                    (int)depthPoints.Max(p => p.Y) + extra);

            return (true, minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private async void WallPlane()
        {
            if (IsFittingPlane)
                return;

            IsFittingPlane = true;

            bool success;
            int minX, minY, w, h;
            //var calibrationPoints = _calibrationPoints.Select(p => new Vector2(p.X, p.Y)).ToList();
            (success, minX, minY, w, h) = await GetCalibrationWindow(
                _touchZonePoints.Select(p => new Vector2(p.World.X, p.World.Y)).ToList(),
                _fw, _fh);
            Vector3[] result = new Vector3[w * h];
            float minD = float.MaxValue, maxD = 0f;
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
                        if (d > maxD) maxD = d;
                        if (d < minD) minD = d;
                        result[i] = new Vector3(
                            world.Value.X,
                            world.Value.Y,
                            world.Value.Z
                        );
                    }
                    else
                        result[i] = new Vector3(0f, 0f, 0f);
                });
            }

            _minWallDepth = (ushort)minD;
            _maxWallDepth = (ushort)maxD;
            var points = result.Where(p => !p.IsZero()).ToArray();
#if DEBUG
            App.Log($"Min/Max depth of wall {_minWallDepth}/{_maxWallDepth}");
#endif
            (_planeD, _planeNormal) = await Task.Run(() => FitPlaneSVD2(points));

            float numerator = MathF.Abs(Vector3.Dot(_planeNormal, Vector3.Zero) + _planeD);
            float denominator = _planeNormal.Length();

            if (denominator == 0)
                throw new ArgumentException("The wall plane normal cannot be a zero vector.");

            float distance = numerator / denominator;
            _cameraPosition = new Vector3(distance, 0, 0); // ← Adjust axis if wall is along Z instead of X

#if DEBUG
            App.Log($"Camera position {_cameraPosition}");
            App.Log($"Wall normal: {_planeNormal}");
            App.Log($"Wall equation: {_planeNormal.X:F4}x + {_planeNormal.Y:F4}y + {_planeNormal.Z:F4}z + {_planeD:F4} = 0");

            foreach (var point in _touchZonePoints)
            {
                //SKPoint p2 = new(point.X, point.Y);
                var p = _calibration.Convert2DTo3D(new(point.X, point.Y), point.Z, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                Vector3 p3 = new Vector3(p.Value.X, p.Value.Y, p.Value.Z);
                App.Log($"Checking if calibrating point on wall = {IsPointOnPlane(p3, _planeNormal, _planeD)}");
                var p2 = _calibration.Convert3DTo2D(p.Value, CalibrationGeometry.Depth, CalibrationGeometry.Color);
                App.Log($"Checking if calibrating point back to screen  {p2.Value}");
            }

            IsFittingPlane = false;

            //WallPlaneStat();

            await SaveConfig();
            IsWallPlaneSet = true;
            
            IsTouchZoneToggleEnabled = true;
#endif
        }


        private async Task WallPlaneStat()
        {
            if (IsTouchZoneDefining)
                return;

            StartWallDefinig();
            IsTouchZoneSet = false;

            SetInfoMessage(Constants.CollectingFramesForPlaneFitting);

            //
            // STEP 1
            // Build APPROXIMATE calibration ROI
            // from color-space polygon
            //

            bool success;
            int minX, minY, w, h;

            var colorROI = _touchZonePoints.Select(p => new Vector2(p.World.X, p.World.Y)).ToList();
            (success, minX, minY, w, h) = await GetCalibrationWindow(
                colorROI,
                _depthWidth, _depthHeight);

#if DEBUG || TEST
            App.Log($"Transformation ROI from color to depth space is successful = {success}");
            int minXColor =
                Math.Max(
                    0,
                    (int)colorROI.Min(p => p.X));

            int minYColor =
                Math.Max(
                    0,
                    (int)colorROI.Min(p => p.Y));

            int maxXColor =
                Math.Min(
                    _fw - 1,
                    (int)colorROI.Max(p => p.X));

            int maxYColor =
                Math.Min(
                    _fh - 1,
                    (int)colorROI.Max(p => p.Y));


            App.Log($"Color ROI ({minXColor}/{maxXColor}, {minYColor}/{maxYColor}) -> depth space ROI ({minX}/{minX+w+1}, {minY}/{minY+h+1})");
#endif

            if (!success)
            {
                OnFailed(Constants.ROIForPlaneFittingFailed);
                StopWallDefinig();
                return;
            }
            //
            // IMPORTANT:
            // During bootstrap we use 
            // native depth accumulation.
            //


            _winAccum = new WindowAccumulator(
                minX, minY, w, h,
                _depthWidth,
                _depthHeight,
                _accCapacityPerPixel);

            IsCollectingFrames = true;

            //
            // Wait accumulation
            //

            await Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();

                while (true)
                {
                    WindowAccumulator? acc;

                    lock (_collectLock)
                        acc = _winAccum;

                    if (acc != null &&
                        acc.FramesCollected >= _targetFrames)
                        break;

                    if (sw.ElapsedMilliseconds > 5000)
                        break;

                    await Task.Delay(5);
                }
            });

            //
            // Stop accumulation
            //

            WindowAccumulator? ready;

            lock (_collectLock)
            {
                ready = _winAccum;
                _winAccum = null;
                IsCollectingFrames = false;
            }

            if (ready == null)
            {
                OnFailed(Constants.FrameCollectionFailed);
                StopWallDefinig();
                return;
            }

            //
            // Build stabilized native depth map
            //

            ushort[] stableDepth =
                new ushort[
                    _depthWidth * _depthHeight];

            ready.BuildDepthMap(
                stableDepth,
                useMedian: true);

            SetInfoMessage(Constants.FittingPlane);

            //
            // STEP 2
            // Native depth-space point cloud
            //

            var points =
                new ConcurrentBag<Vector3>();

            Parallel.For(0, _depthHeight, y =>
            {
                int row = y * _depthWidth;

                for (int x = 0; x < _depthWidth; x += 2)
                {
                    int i = row + x;

                    ushort d = stableDepth[i];

                    if (d < 300 || d > 8000)
                        continue;

                    var world =
                        _calibration.Convert2DTo3D(
                            new(x, y), d,
                            CalibrationGeometry.Depth,
                            CalibrationGeometry.Depth);

                    if (world == null)
                        continue;

                    points.Add(new Vector3(
                        world.Value.X,
                        world.Value.Y,
                        world.Value.Z));
                }
            });

            var pointList =
                points.ToList();

            //
            // STEP 3
            // Fit wall plane IN NATIVE DEPTH SPACE
            //

            var fitter =
                new RANSACPlaneFitter(
                    iterations: 1000,
                    threshold: 10);

            PlaneResult plane =
                await Task.Run(() =>
                    fitter.FitPlane(pointList));

            if (!plane.Success)
            {
                IsTouchZoneDefining = false;

                OnFailed(
                    Constants.PlaneFittingFailed);

                return;
            }

            //
            // Ensure normal faces camera
            //

            if (plane.Normal.Z < 0)
            {
                plane.Normal = -plane.Normal;
                plane.D = -plane.D;
            }

            _planeNormal = plane.Normal;
            _planeD = plane.D;

            App.Log(
                $"Depth-space plane: " +
                $"{plane.Normal.X:F4}x + " +
                $"{plane.Normal.Y:F4}y + " +
                $"{plane.Normal.Z:F4}z + " +
                $"{plane.D:F4} = 0");

            //
            // STEP 4
            // NOW accurately convert Color ROI
            // → native depth ROI
            //

            depthSpaceTouchZone = new List<Vector3>();

            foreach (var p in _touchZonePoints)
            {
                var point =
                    PlaneChecker.ConvertColorPointToDepthSpaceViaPlane(
                        p.SX,
                        p.SY,
                        _planeNormal,
                        _planeD,
                        _calibration);

                if (point != null)
                {
                    depthSpaceTouchZone.Add(point.Value);

#if DEBUG || TEST
                    App.Log(
                        $"Color point ({p.SX}, {p.SY}) " +
                        $"-> depth space {point.Value}");
#endif
                }
                else
                {
#if DEBUG || TEST
                    App.Log(
                        $"Failed to convert color point " +
                        $"({p.SX}, {p.SY}) to depth space.");
#endif
                }
            }

#if DEBUG || TEST || MOCK

            var filePath = Path.Combine(Constants.LOG_FOLDER, "stableDepth.bin");
            Utilities.SaveCapturedFrame(stableDepth, filePath);

            App.Log("Depth-space points are saved to: " + filePath);

            App.Log("Depth-space touch zone points:");
            foreach (var p in depthSpaceTouchZone)
            {
                App.Log($"Depth-space point: {p}");
                App.Log($"Checking if this point on wall = {IsPointOnPlane(p, _planeNormal, _planeD)}");

            }
#endif
            //
            // DONE
            //

            IsWallPlaneSet = true;
            IsTouchZoneSet = true;
            StopWallDefinig();

            SetInfoMessage(Constants.WallPlaneDetected);
        }

        private void StartWallDefinig()
        {
            IsTouchZoneDefining = true;
        }

        private void StopWallDefinig()
        {
            IsTouchZoneDefining = false;
        }

        private List<ExclusionZone>? DetectLedges(
            List<Vector3> points, 
            Vector3 planeNormal,
            float planeDistance,
            int minOffset=10, int maxOffset=50
            )
        {
            if (points.Count < 4)
                return null;

            TouchVolume ledgeVolume = new TouchVolume(
                planeNormal, planeDistance,
                minOffset, maxOffset, // capture near-plane protrusions
                TouchZonePoints,
                _minWallDepth, _maxWallDepth, 
                _fw, _fh, 
                _calibration);

            var ledgePoints = ledgeVolume.ExtractProjectedPointsInsideVolumeFromList(points);

            var ledgeClusterManager = new Touch2DClusterManager();

            var clusters = ledgeClusterManager.DetectClusters(ledgePoints);

            if (clusters.Count == 0) return null;

            var zones = new List<ExclusionZone>();
            foreach (var c in clusters)
            {
                zones.Add(new ExclusionZone(c.Center, c.Radius));
#if DEBUG
                App.Log($"Detected ledge at local center {c.Center} with radius {c.Radius}");
                var c3 = ledgeVolume.Get3DPointFromLocal2DPoint(c.Center);
                App.Log($"Detected ledge has 3D center {c3}");
                var screen = _calibration.Convert3DTo2D(new(c3.X, c3.Y, c3.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
                App.Log($"Detected ledge has screen center {screen}");
#endif
            }

            return zones;
        }


        private (float, Vector3) FitPlaneSVD2(Vector3[] points)
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
                    var relative = point - centroid;

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

            // Flip coordinates if ceiling-mounted
            //if (!_floorCamera)
            if (false)
            {
#if DEBUG
                var x_ = x;
                var y_ = y;
#endif
                x = _fw - 1 - x; // horizontal flip
                y = _fh - 1 - y; // vertical flip
#if DEBUG
                App.Log($"Ceil-mounted camera. Flip {x_}:{y_} to {x}:{y}");
#endif
            }

            var i = y * _fw + x;

            lock (_depthLock)
            {
                if ((uint)i < (uint)_depthData.Length)
                    d = _depthData[i];
                else
                {
#if DEBUG
                    App.Log($"index out of range {i} where max {_depthData.Length}");
#endif
                }
            }

            return d;
        }

        private void CheckIsReadyRunTouchLoop()
        {
            IsReadyTrackTouch =  IsWallPlaneSet && IsTouchZoneSet && (MaxOffset > MinOffset) && !IsFittingPlane; //  && IsCameraPositionDefined
        }

        partial void OnIsRunningChanged(bool value)
        {
            ToglleStartStop = IsRunning ? "Stop" : "Start";
            //IsCalibrationToggleEnabled = IsRunning && !IsCalibrating && !IsFittingPlane;
            // Enable the touch zone toggle only when the app is running, the wall plane is defined,
            // we are not currently selecting the touch zone, and we are not fitting a plane.
            //IsTouchZoneToggleEnabled = IsCalibrationToggleEnabled && IsWallPlaneSet && !IsSelectingTouchZone;
            IsTouchZoneToggleEnabled = IsRunning && !IsCalibrating && !IsFittingPlane; // !IsSelectingTouchZone && !IsTouchZoneDefining;
            CheckIsReadyRunTouchLoop();
        }

        partial void OnCursorChanged(SKPoint value)
        {
            if (!IsRunning || IsRunningTrackTouch)
                return; // Does not display when no running capture or running touch detection 
            var d = GetDepth(value);
            if (d > 0)
            {
                var p = _calibration.Convert2DTo3D(new(value.X, value.Y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth)?.ToVector3();
                p = p / 1000.0f;
                SetInfoMessage($"({value.X};{value.Y}) - ({p.Value.X}; {p.Value.Y}; {p.Value.Z})m;");
            }
            else
            {
                SetInfoMessage($"({value.X};{value.Y})");
            }

        }

        partial void OnIsTouchZoneDefiningChanged(bool value) 
        {
            IsTouchZoneToggleEnabled = IsRunning && !IsSelectingTouchZone && !IsTouchZoneDefining;
            if (IsTouchZoneDefining)
            {
                TouchZoneStatus = "Defining...";
                TouchZoneForeground = new SolidColorBrush(Colors.DarkGray);
            }
            else
            {
                TouchZoneStatus = IsWallPlaneSet ? "Set" : "Not Set";
                TouchZoneForeground = new SolidColorBrush(IsWallPlaneSet ? Colors.Green : Colors.OrangeRed);
            }
            CheckIsReadyRunTouchLoop();
        }

        partial void OnIsWallPlaneSetChanged(bool value)
        {
            WallPlaneStatus = value ? "Set" : "Not Set";
            WallPlaneForeground = new SolidColorBrush(value ? Colors.Green : Colors.OrangeRed);
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
            var offset = await GetConfigValue<int?>("MinOffset");
            MinOffset = (offset.HasValue) ? offset.Value : _defaltMinOffset;
        }

        private async Task LoadMaxOffset()
        {
            var offset = await GetConfigValue<int?>("MaxOffset");
            MaxOffset = (offset.HasValue) ? offset.Value : _defaltMaxOffset;
        }

        partial void OnMinOffsetChanged(int value)
        {
            if (IsReadySaveConfig())
            {
                _ = SaveConfig();
                CheckIsReadyRunTouchLoop();
                RestartTouchLoop();
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
                RestartTouchLoop();
            }
            else
            {
                _ = LoadMaxOffset();
            }
        }

        partial void OnGameScreenWidthChanged(int value)
        {
#if DEBUG
            App.Log($"GameScreenWidth changed to {GameScreenWidth}");
            App.Log($"GameScreenHeight = {GameScreenHeight}");
#endif
            _ = SaveConfig();
        }
        partial void OnGameScreenHeightChanged(int value)
        {
#if DEBUG
            App.Log($"GameScreenWidth = {GameScreenWidth}");
            App.Log($"GameScreenHeight changed to {GameScreenHeight}");
#endif
            _ = SaveConfig();
        }

        partial void OnIsCameraPositionDefinedChanged(bool value)
        {
            CheckIsReadyRunTouchLoop();
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

        private void OnMessageReceived(object? sender, string message)
        {
            Debug.WriteLine(message);
            switch (message)
            {
                case "start":
                    if (!IsRunning)
                        App.Current.DispatcherQueue.TryEnqueue(() => Start());
                    break;
                case "stop":
                    if (IsRunning)
                        App.Current.DispatcherQueue.TryEnqueue(() => Stop());
                    break;
                default:
                    break;
            }
        }

        
        partial void OnAutoStartCalibrationChanged(bool value) 
        { 
            OnPropertyChanged(nameof(CanStartAutoCalibration)); 
        }
        
        partial void OnIsHardwareReadyChanged(bool value) 
        {
            OnPropertyChanged(nameof(CanStartAutoCalibration)); 
        }

        partial void OnIsAutoModeChanged(bool value) 
        {
            if (!value) AutoStartCalibration = false; // Auto-start only valid in Auto mode


            OnPropertyChanged(nameof(CanStartAutoCalibration)); 
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

        [Obfuscation(Exclude = true, Feature = "string encryption")]
        public static string CONFIG_SUB_FOLDER = "CPR Touch Vision";
        private int _depthWidth;
        private int _depthHeight;
        private List<Vector3> depthSpaceTouchZone;
        private bool useRawDepthData = true;

        public static string CONFIG_FOLDER => System.IO.Path.Combine(CommonFolderPath, CONFIG_SUB_FOLDER);
        [Obfuscation(Exclude = true, Feature = "string encryption")]
        public static string CONFIG_PATH => System.IO.Path.Combine(CommonFolderPath, CONFIG_SUB_FOLDER, "config.json");
        public static string CommonFolderPath
        {
            get
            {
                var result = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CPR Soft");

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
        CalibrationUpdate,
        CalibrationFailed
    }

    public class ConfigMissingException : Exception
    {
        public ConfigMissingException(string message) : base(message) { }
    }

    public class CalibrationFrame
    {
        public byte[] ColorData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}