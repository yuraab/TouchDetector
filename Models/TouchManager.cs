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


namespace CPRTouchVision.Models
{
    internal partial class TouchManager : ObservableObject, ICalibrationProgress, IDisposable
    {
        [ObservableProperty]
        string _toglleStartStop = "Start";

        [ObservableProperty]
        bool _isAutoMode = true;

        [ObservableProperty]
        bool _startInAutoMode = true;

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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsHardwareReady))]
        [NotifyPropertyChangedFor(nameof(HardwareErrorMessage))]
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
        private bool _isDeviceChecking;

        // This controls the AutoStart button IsEnabled
        [ObservableProperty]
        private bool _canStartCalibration;

        public bool CanStartAutoCalibration => IsAutoMode && CanStartCalibration;

        partial void OnCanStartCalibrationChanged(bool value)
        {
            OnPropertyChanged(nameof(CanStartCalibration));
        }

        public event EventHandler HardwareChecksCompleted;

        // This command can be bound directly to a Button's Command property
        [RelayCommand(CanExecute = nameof(CanRetry))]
        private async Task RunAllHardwareChecksAsync()
        {
            IsDeviceChecking = true;

            // Reset all statuses to Pending before starting
            foreach (var item in HardwareItems) item.Status = StatusCode.Pending;

            // Run all checks in parallel
            var tasks = _checkers.Select(c => c.CheckConnection());
            await Task.WhenAll(tasks);

            IsDeviceChecking = false;
            UpdateHardwareReadiness();

            // Notify MainWindow listening 
            HardwareChecksCompleted?.Invoke(this, EventArgs.Empty);
        }

        // request frame for calibration,
        // MainWindow will call this when auto-calibration is started,
        // and complete the TaskCompletionSource when the frame is ready
        private TaskCompletionSource<CalibrationFrame>? _calibrationRequest;
        public Task<CalibrationFrame> GetNextCalibrationFrameAsync(System.Threading.CancellationToken token)
        {
            _calibrationRequest = new TaskCompletionSource<CalibrationFrame>();
            return _calibrationRequest.Task;
        }

        private bool CanRetry() => !IsDeviceChecking;

        public void OnStatus(string message) 
        { 
            CalibrationStatus = message;
            App.Log(message);
        }

        partial void OnCalibrationStatusChanged(string value)
        {
            InfoMessage = value;
            App.Log(value);
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

        public async void OnCompleted(Point[] points) 
        { 
            foreach (var point in points) {
                var projectedPoint = Convert2DToDepthPoint((int) point.X, (int) point.Y);

                _touchZonePoints.Add(projectedPoint);
            }

            await FinalizeTouchZoneAsync(); 
        } 
        public void OnFailed(string reason) 
        { 
            CalibrationStatus = $"{Constants.FailureIndicatorMessage}: {reason}";
            _isError = true;
        }


        private readonly Dictionary<string, StatusCode> _hardwareStates = new();
        
        private readonly List<IHardwareChecker> _checkers = new();

        public IReadOnlyList<IHardwareChecker> Checkers => _checkers;

        

        // This controls the Start button IsEnabled
        public bool CanStartCalibration => AllHardwareConnected;

        private void UpdateHardwareReadiness()
        {
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

        private List<DepthPoint> _touchZonePoints = new();

        private List<TouchEvent> _touches = new();
        public List<TouchEvent> Touches => _touches;

        private float _planeD = float.MinValue;
        private Vector3 _planeNormal = Vector3.Zero;
        private Vector3 _cameraPosition = Vector3.Zero;

        private TouchVolume _detectableSpace;
        public TouchVolume DetectableSpace => _detectableSpace;

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
                Stop();
            else
                Start();
        }
        private void Start()
        {
            if (IsRunning || !Device.TryOpen(out var device)) return;

            _captureLoop = new(device);
            _captureLoop.CaptureReady += OnCaptureReady;
            _captureLoop.LoopFailed += OnLoopFailed;
            _captureLoop.GetCalibration(out _calibration);
            _captureLoop.CameraPositionReady += OnCameraPositionReady;

            _transformation = new Transformation(_calibration);

            _captureLoop.Run();
            StartTouchLoop();
            IsRunning = true;

        }

        private void StartTouchLoop()
        {
            CheckIsReadyRunTouchLoop();
#if DEBUG
            App.Log($"Track Touch Loop ready to start = {IsReadyTrackTouch} because  IsWallPlaneSet = {IsWallPlaneSet} && IsTouchZoneSet = {IsTouchZoneSet} && MaxOffset > MinOffset = {MaxOffset > MinOffset} && !IsFittingPlane = {!IsFittingPlane}");
#endif
            if (IsRunningTrackTouch || !IsReadyTrackTouch) return;

            _detectableSpace = new TouchVolume( _planeNormal, 
                                                _planeD,
                                                MinOffset*10,   //convert from centimeters
                                                MaxOffset*10,   //convert from centimeters
                                                TouchZonePoints,
                                                _minWallDepth, _maxWallDepth,
                                                _fw, _fh, _calibration
                                               );

            _touchLoop = new(_detectableSpace, _calibration, _exclusionZones);
            _touchLoop.TouchFrameReady += OnTouchFrameReady;
            _touchLoop.TouchLoopFailed += OnTouchLoopFailed;
            _touchLoop.ReadyForNewImage += OnReadyForNewImage;
            
            _touchLoop.Run();
            IsRunningTrackTouch = true;
            _isReadyReceiveNewCapture = true;
            _detectableSpace.WallNotAligned += OnWallNotAligned;
            App.Log("Touch detection is started");
            OnStatus("Touch detection is running");
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
#if DEBUG
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
            using var colorImage = capture.ColorImage;
            if (colorImage != null)
            {
                if (!_floorCamera)
                    FlipColor180(colorImage);
                _colorBitmap.Update((bitmap) =>
                {
                    bitmap.InstallPixels(_colorBitmapInfo, colorImage.Buffer);
                });
                if (_calibrationRequest != null)
                {
                    int totalBytes = colorImage.SizeBytes;

                    //  Create the managed destination array
                    byte[] managedColorData = new byte[totalBytes];

                    //  Copy from the raw pointer (nint) to a new array to avoid disposal issues
                    System.Runtime.InteropServices.Marshal.Copy(colorImage.Buffer, managedColorData, 0, totalBytes);

                    var frame = new CalibrationFrame
                    {
                        ColorData = managedColorData, 
                        Width = _fw,
                        Height = _fh
                    };
                    _calibrationRequest.TrySetResult(frame);
                    _calibrationRequest = null; // Clear request after fulfilling it
                }
                    
            }

            using var depthImage = capture.DepthImage;

            if (depthImage != null)
            {
                using var aligned = new OB.Image(OB.ImageFormat.Depth16, _fw, _fh);
                _transformation?.DepthImageToColorCamera(depthImage, aligned);

                if (!_floorCamera)
                    FlipDepth180(aligned); // in-place flip

                _depthData.CopyFrom(aligned);

                _depthBitmap.Update(bitmap =>
                {
                    bitmap.Pixels = _depthVisualizer.Update(_depthData);
                });

                if (IsCollectingFrames)
                {
                    WindowAccumulator? acc = null;
                    lock (_collectLock) acc = _winAccum;
                    if (acc != null)
                    {
                        acc.AddFrame(_depthData);
                    }
                }

                if (_isReadyReceiveNewCapture && _touchLoop != null)
                {
                    _ = _touchLoop.TrySendImage(_depthData, _fw, _fh, now);
                }
            }
            Changed?.Invoke(this, TouchManagerEventType.NewFrame);

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

            _depthBitmap.Update((bitmap) =>
            {
                bitmap.Pixels = _depthVisualizer.Update(_depthData);
            });
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

        private bool IsPointOnPlane(System.Numerics.Vector3 point, System.Numerics.Vector3 planeNormal, float planeDistance)
        {
            const float epsilon = 10;
            float dist = System.Numerics.Vector3.Dot(planeNormal, point) + planeDistance;
#if DEBUG
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

        OBSharp.Float3 TransformToDepthSpace(OBSharp.Float3 worldPoint, CalibrationExtrinsics extrinsics)
        {
            // Invert rotation (transpose for orthonormal matrix)
            var R = extrinsics.Rotation;
            var T = extrinsics.Translation;

            // Subtract translation
            float x = worldPoint.X - T[0];
            float y = worldPoint.Y - T[1];
            float z = worldPoint.Z - T[2];

            // Apply transposed rotation
            float dx = R[0] * x + R[3] * y + R[6] * z;
            float dy = R[1] * x + R[4] * y + R[7] * z;
            float dz = R[2] * x + R[5] * y + R[8] * z;

            return new OBSharp.Float3(dx, dy, dz);
        }

        Vector3 Convert2DTo3DPoint(int pointX, int pointY, ushort d)
        {
            var worldPoint1 = _calibration.Convert2DTo3D(new(pointX, pointY), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
            var x = worldPoint1.Value.X;
            var y = worldPoint1.Value.Y;
            var z = worldPoint1.Value.Z;
            Vector3 pointD = new Vector3(x, y, z);
#if DEBUG
            IsPointOnPlane(pointD, _planeNormal, _planeD);
            var extrinsics = _calibration.GetExtrinsics(CalibrationGeometry.Color, CalibrationGeometry.Depth);
            var depthSpacePoint = TransformToDepthSpace(new OBSharp.Float3(x, y, z), extrinsics);
            float recoveredDepth = depthSpacePoint.Z;
            App.Log($"Converted 2D point ({pointX}, {pointY}) with depth {d} to 3D point {pointD}. Recovered depth in depth space: {recoveredDepth}");
#endif
            return pointD;
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
            return ProjectPointOntoPlane(p3D);
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

        private (int, int, int, int) GetCalibrationWindow(List<Vector3> points)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            foreach (var point in points)
            {
                minX = Math.Min((int)point.X, minX);
                minY = Math.Min((int)point.Y, minY);
                maxX = Math.Max((int)point.X, maxX);
                maxY = Math.Max((int)point.Y, maxY);
            }

            int w = maxX - minX;
            int h = maxY - minY;
            return (minX, minY, w, h);
        }

        private async void WallPlane()
        {
            if (IsFittingPlane)
                return;

            IsFittingPlane = true;

            int minX, minY, w, h;
            //var calibrationPoints = _calibrationPoints.Select(p => new Vector2(p.X, p.Y)).ToList();
            (minX, minY, w, h) = GetCalibrationWindow(_touchZonePoints.Select(p => p.World).ToList());
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

            IsTouchZoneDefining = true;
            IsTouchZoneSet = false;

            int minX, minY, w, h;
            var touchZonePoints = _touchZonePoints.Select(p => new Vector3(p.SX, p.SY, 0)).ToList();
            (minX, minY, w, h) = GetCalibrationWindow(touchZonePoints);
            
            float minD = float.MaxValue, maxD = 0f;
            _winAccum = new WindowAccumulator(minX, minY, w, h, _fw, _fh, _accCapacityPerPixel);
            IsCollectingFrames = true;

            await Task.Run(async () =>
            {
                // safeguard: максимум 2 секунды при 15 FPS → подстрой при необходимости
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    WindowAccumulator? acc;
                    lock (_collectLock) acc = _winAccum;
                    if (acc != null && acc.FramesCollected >= _targetFrames) break;
                    if (sw.ElapsedMilliseconds > 4000) break; // тайм-аут, чтобы не зависнуть
                    await Task.Delay(5);
                }
            });

            float[] winDepth = new float[w * h];
            WindowAccumulator? ready;
            lock (_collectLock) { ready = _winAccum; _winAccum = null; IsCollectingFrames = false; }
            if (ready == null) return;

            ready.BuildDepthMap(winDepth, useMedian: true);

            var points = new List<Vector3>(winDepth.Length);

            Parallel.For(0, winDepth.Length, i =>
            {
                int x = minX + i % w;
                int y = minY + i / w;
                int index = y * _fw + x;

                float d = winDepth[i];
                if (d <= 0) return;

                var world = _calibration.Convert2DTo3D(new(x, y), d,
                                CalibrationGeometry.Color, CalibrationGeometry.Depth);
                if (world == null) return;

                var point = new Vector3(
                            world.Value.X,
                            world.Value.Y,
                            world.Value.Z
                        );
                lock (points) points.Add(point);

            });

            //float planeD;
            //Vector3 planeNormal;
            Vector3 cameraPosition;

            //(planeD, planeNormal) = await Task.Run(() => FitPlaneSVD2(points.ToArray()));
            
            var fitter = new RANSACPlaneFitter(iterations: 1000, threshold: 10);
            PlaneResult plane = await Task.Run(() => fitter.FitPlane(points));
            if (!plane.Success)
            { 
                App.Log("Plane fitting failed. Not enough inliers or points.");
                IsTouchZoneDefining = false;
                Changed?.Invoke(this, TouchManagerEventType.CalibrationFailed);
                return;
            }
            _planeNormal = plane.Normal;
            _planeD = plane.D;

            float numerator = MathF.Abs(Vector3.Dot(_planeNormal, Vector3.Zero) + _planeD);
            float denominator = _planeNormal.Length();

            if (denominator == 0)
                throw new ArgumentException("The wall plane normal cannot be a zero vector.");

            float distance = numerator / denominator;
            cameraPosition = new Vector3(distance, 0, 0); // ← Adjust axis if wall is along Z instead of X

#if DEBUG
            App.Log($"Plane fitting done. {plane}");
            App.Log($"Camera position {cameraPosition}");

            foreach (var p in _touchZonePoints)
            {
                //SKPoint p2 = new(point.X, point.Y);
                //var p = _calibration.Convert2DTo3D(new(point.X, point.Y), point.Z, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                //Vector3 p3 = new Vector3(p.Value.X, p.Value.Y, p.Value.Z);
                App.Log($"Checking if touchZonePoint point {p} on wall = {IsPointOnPlane(p.World, _planeNormal, _planeD)}");
                var p2 = _calibration.Convert3DTo2D(p.World.ToFloat3(), CalibrationGeometry.Depth, CalibrationGeometry.Color);
                App.Log($"Checking if calibrating point back to screen  {p2.Value}");
            }
            Helper.CheckPlane(points, plane);
#endif
           // TODO: - cuts out more then needed, in a real world rock climbing walls setup, needs adjustment
           // _exclusionZones = DetectLedges(plane.Outliers, _planeNormal, _planeD);

            //await SaveConfig(); 
            IsTouchZoneDefining = false;
            IsWallPlaneSet = true;
            IsTouchZoneSet = true;
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
            IsTouchZoneToggleEnabled = value; // !IsSelectingTouchZone && !IsTouchZoneDefining;
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
                InfoMessage = $"({value.X};{value.Y}) - ({p.Value.X}; {p.Value.Y}; {p.Value.Z})m;";
            }
            else
            {
                InfoMessage = $"({value.X};{value.Y})";
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