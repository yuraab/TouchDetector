using System;
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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ComputeSharp;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using OBSharp;
using OBSharp.BodyTracking;
using OBSharp.Sensor;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Emgu.CV;

using OB = OBSharp.Sensor;
using CV = Emgu.CV;

namespace CPRTouchVision.Models
{
    internal partial class TouchManager : ObservableObject, IDisposable
    {
        [ObservableProperty]
        string _toggleTitle = "Start";

        [ObservableProperty]
        bool _isRunning = false;

        [ObservableProperty]
        SKPoint _cursor = SKPoint.Empty;

        [ObservableProperty]
        string _cursorLabel = "";

        [ObservableProperty]
        bool _isFloorLevelSet = false;

        [ObservableProperty]
        string _floorLevelStatus = "Not set";

        [ObservableProperty]
        Brush _floorLevelForeground = new SolidColorBrush(Colors.OrangeRed);

        [ObservableProperty]
        bool _isCalibrating = false;

        [ObservableProperty]
        bool _isCalibrationToggleEnabled = false;

        [ObservableProperty]
        bool _isFittingPlane = false;

        [ObservableProperty]
        bool _isAddingTrampoline = false;

        [ObservableProperty]
        bool _canAddTrampolines = false;

        [ObservableProperty]
        int _offset = 15;

        private bool _disposed = false;
        private Calibration _calibration = new();
        private Transformation? _transformation;

        // Getting data from the camera
        private CaptureLoop? _captureLoop;

        // Prosessing the data captured
        private TrackingLoop? _trackingLoop;

        private ushort[] _depthData = [];
        private byte[] _depthPixels = [];

        private List<Vector3> _calibrationPoints = new();
        private ObservableCollection<Trampoline> _trampolines = new();
        private float _planeD = float.MinValue;
        private Vector3 _planeNormal = Vector3.Zero;
        private Vector3 _cameraPosition = Vector3.Zero;
        private System.Numerics.Quaternion _cameraRotation = System.Numerics.Quaternion.Identity;
        private DepthVisualizer _depthVisualizer;
        public readonly object Lock = new object();

        // Rendering
        private int _fw = 1280;
        private int _fh = 720;
        private int _fsize;
        private SKImageInfo _colorBitmapInfo;
        private DoubleBufferedBitmap _colorBitmap;
        private DoubleBufferedBitmap _depthBitmap;
        private Dictionary<int, float> _elevationOffsets = new();
        private int _currentTrampolineIndex = -1;

        private readonly object _depthLock = new object();
        private readonly object _bodiesLock = new object();

        private OSCClient _oscClient = new();

        private SKColor[] _trampolineColors =
        [
            new SKColor(0xFF8F51FA),
            new SKColor(0xFF1E60F9),
            new SKColor(0xFF5FC8EE),
            new SKColor(0xFF82C90F),
            new SKColor(0xFFE3BF0C),
            new SKColor(0xFFEC6516)
        ];

        public DoubleBufferedBitmap ColorBitmap => _colorBitmap;
        public DoubleBufferedBitmap DepthBitmap => _depthBitmap;
        public int FW => _fw;
        public int FH => _fh;
        public Vector3[] CalibrationPoints => _calibrationPoints.ToArray();
        public ObservableCollection<Trampoline> Trampolines => _trampolines;
        public EventHandler<TouchManagerEventType>? Changed;
        public Trampoline CurrentTrampoline => _trampolines[_currentTrampolineIndex];
        private UdpClient _client = new();
        private IPEndPoint _endpoint = new IPEndPoint(IPAddress.Loopback, 12345);

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
            if (IsRunning || !Device.TryOpen(out var device)) return;
            Sdk.IsBodyTrackingRuntimeAvailable(out var message);
            Debug.WriteLine(message);
            _captureLoop = new(device);
            _captureLoop.CaptureReady += OnCaptureReady;
            _captureLoop.LoopFailed += OnLoopFailed;
            _captureLoop.GetCalibration(out _calibration);

            _trackingLoop = new(_calibration);
            _trackingLoop.BodyFrameReady += OnBodyFrameReady;
            _trackingLoop.LoopFailed += OnLoopFailed;

            _transformation = new Transformation(_calibration);

            _captureLoop.Run();
            _trackingLoop.Run();
            IsRunning = true;
        }

        private void OnBodyFrameReady(object? sender, BodyFrameEventArgs e)
        {
            // used to be body frame processing logic to display skeletons, joints, jumps, etc.

            // TODO: - use OSCClient to send touch events instead of body data.
            // Task.Run(() => _oscClient.Send(bodies));
            // Changed?.Invoke(this, TouchManagerEventType.NewFrame);
        }

        private void OnLoopFailed(object? sender, LoopFailedEventArgs e)
        {
            // TODO: - Display Error
        }

        private void OnCaptureReady(object? sender, CaptureLoopEventArgs e)
        {
            // TODO: - Handle touch logic here or modify TrackingLoop (preferred) to process touches instead of body frames.
            // This is the place where we can handle the data captured from the camera.
            // For body tracker (trampolines, immersive dancing, etc.) we used OBSharp BodyTracking sdk to process 
            // capture and get body frames in a separate background thread with TrackingLoop.
            
            if (e.Capture == null)
                return;

            // _trackingLoop?.Enqueue(e.Capture);

            // For now i'll just fire TouchManagerEventType.NewFrame event to display video output in the main window.
            if (e.Capture.IsDisposed) return;
            using var capture = e.Capture;
            if (capture == null) return;

            using var colorImage = capture.ColorImage;

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

            if (_trackingLoop != null)
            {
                _trackingLoop.BodyFrameReady -= OnBodyFrameReady;
                _trackingLoop.LoopFailed -= OnLoopFailed;
                _trackingLoop.Dispose();
                _trackingLoop = null;
            }

            _colorBitmap = new(_colorBitmapInfo);
            _depthBitmap = new(_colorBitmapInfo);

            IsRunning = false;
        }

        public void StartCalibration()
        {
            if (IsCalibrating || IsAddingTrampoline)
                return;

            _calibrationPoints.Clear();
            _trampolines.Clear();
            IsFloorLevelSet = false;
            IsCalibrating = true;
        }

        public void StartAddingTrampoline()
        {
            if (!IsRunning || !IsFloorLevelSet || _trampolines.Count == 6)
                return;

            _currentTrampolineIndex = GetFirstEmptyId();
            if (_currentTrampolineIndex > -1)
            {
                _trampolines.Insert(_currentTrampolineIndex, new Trampoline(_currentTrampolineIndex, _trampolineColors[_currentTrampolineIndex].ToColor()));
                IsAddingTrampoline = true;
            }
        }

        public void RemoveTrampolineWith(int id)
        {
            var index = _trampolines.ToList().FindIndex(t => t.Id == id);

            if (index >= 0 && index < _trampolines.Count)
            {
                _trampolines.RemoveAt(index);
                _ = SaveConfig();
            }

            if (IsAddingTrampoline)
            {
                if (_currentTrampolineIndex == index)
                    IsAddingTrampoline = false;
                else if (index < _currentTrampolineIndex)
                    _currentTrampolineIndex--;
            }
        }

        private int GetFirstEmptyId()
        {
            int result = -1;

            if (_trampolines.Count > 0)
            {
                int i = 0;

                foreach (var trampoline in _trampolines)
                {
                    if (i != trampoline.Id)
                    {
                        result = i;
                        break;
                    }
                    i++;
                }

                if (result == -1)
                    result = _trampolines.Count();
            }
            else
                result = 0;

            return result;
        }

        public async Task LoadConfig()
        {
            if (File.Exists(CONFIG_PATH))
            {
                var bytes = await File.ReadAllBytesAsync(CONFIG_PATH);
                var json = Encoding.UTF8.GetString(bytes);

                if (JsonConvert.DeserializeObject<Config>(json) is Config config)
                {
                    if (config.PlaneD.HasValue && config.PlaneNormal.HasValue && config.CameraRotation.HasValue)
                    {
                        _planeD = config.PlaneD.Value;
                        _planeNormal = config.PlaneNormal.Value;
                        _cameraRotation = config.CameraRotation.Value;
                        _cameraPosition = config.CameraPosition.HasValue ? config.CameraPosition.Value : Vector3.Zero;
                        IsFloorLevelSet = true;
                    }

                    if (config.Offset.HasValue)
                    {
                        Offset = config.Offset.Value;
                    }

                    // TODO: - trampolines should be replaced with touch projection area logic instead
                    //if (config.Trampolines != null)
                    //{
                    //    _trampolines = new(config.Trampolines);
                    //    OnPropertyChanged(nameof(Trampolines));
                    //}
                }
            }
        }

        public async Task SaveConfig()
        {
            var config = new Config(
                Offset,
                _planeD == float.MinValue ? null : _planeD,
                _planeNormal == Vector3.Zero ? null : _planeNormal,
                _cameraPosition,
                _cameraRotation.IsIdentity ? null : _cameraRotation
            );
            var json = JsonConvert.SerializeObject(config) ?? "";
            var data = Encoding.UTF8.GetBytes(json);
            Directory.CreateDirectory(CONFIG_FOLDER);
            await File.WriteAllBytesAsync(CONFIG_PATH, data);
        }

        public void AddCalibrationPoint(SKPoint point)
        {
            if (!IsCalibrating)
                return;

            var d = GetDepth(point);

            if (d > 0 && _calibrationPoints.Count < CalibrationPointsCount)
            {
                _calibrationPoints.Add(new(point.X, point.Y, d));

                if (_calibrationPoints.Count == CalibrationPointsCount)
                {
                    IsCalibrating = false;
                    FitPlane();
                }
            }
        }

        private async void FitPlane()
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

            var points = result.Where((item) => !item.IsEmpty).ToArray();
            (_planeD, _planeNormal) = await Task.Run(() => FitPlaneSVD2(points));

            Debug.WriteLine($"Plane normal: {_planeNormal}");
            Debug.WriteLine($"Plane equation: {_planeNormal.X:F4}x + {_planeNormal.Y:F4}y + {_planeNormal.Z:F4}z + {_planeD:F4} = 0");

            if (_captureLoop?.GetCameraRotation() is Vector3 rotation)
            {
                _cameraRotation = System.Numerics.Quaternion.Inverse(System.Numerics.Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z));
                var rad2deg = 180 / MathF.PI;
                Debug.WriteLine($"Camera Rotation: {rotation.X * rad2deg:F4} {rotation.Y * rad2deg:F4} {rotation.Z * rad2deg:F4}");
            }

            /* Get Camera Position */
            // Calculate the numerator: |a*x + b*y + c*z + d|
            float numerator = MathF.Abs(Vector3.Dot(_planeNormal, Vector3.Zero) + _planeD);

            // Calculate the denominator: sqrt(a^2 + b^2 + c^2)
            float denominator = _planeNormal.Length();

            if (denominator == 0)
            {
                throw new ArgumentException("The plane normal cannot be a zero vector.");
            }

            float distance = numerator / denominator;
            _cameraPosition = new Vector3(0, distance, 0);

            await SaveConfig();
            IsFloorLevelSet = true;
            IsFittingPlane = false;
        }

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

        public void AddTrampolinePoint(int x, int y)
        {
            if (!IsAddingTrampoline)
                return;

            var d = GetDepth(x, y);
            var world = _calibration.Convert2DTo3D(new(x, y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);

            if (d > 0 && world != null)
                CurrentTrampoline.Add(DepthPoint.From(
                    x,
                    y,
                    world.Value.X,
                    world.Value.Y,
                    world.Value.Z
                ));
        }

        public void CloseTrampoline()
        {
            if (!IsAddingTrampoline)
                return;

            CurrentTrampoline.Close();
            _ = SaveConfig();
            IsAddingTrampoline = false;
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

        partial void OnIsRunningChanged(bool value)
        {
            ToggleTitle = IsRunning ? "Stop" : "Start";
            IsCalibrationToggleEnabled = IsRunning && !IsCalibrating && !IsFittingPlane;
            CanAddTrampolines = IsRunning && IsFloorLevelSet && !IsAddingTrampoline;
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
        }

        partial void OnIsFittingPlaneChanged(bool value)
        {
            IsCalibrationToggleEnabled = IsRunning && !IsCalibrating && !IsFittingPlane;

            if (IsFittingPlane)
            {
                FloorLevelStatus = "Defining...";
                FloorLevelForeground = new SolidColorBrush(Colors.DarkGray);
            }
        }

        partial void OnIsAddingTrampolineChanged(bool value)
        {
            CanAddTrampolines = IsRunning && IsFloorLevelSet && !IsAddingTrampoline;
        }

        partial void OnIsFloorLevelSetChanged(bool value)
        {
            FloorLevelStatus = value ? "Set" : "Not Set";
            FloorLevelForeground = new SolidColorBrush(value ? Colors.Green : Colors.OrangeRed);
            CanAddTrampolines = IsRunning && IsFloorLevelSet && !IsAddingTrampoline;
        }

        partial void OnOffsetChanged(int value)
        {
            _ = SaveConfig();
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
}
