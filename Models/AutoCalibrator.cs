
#if !DISABLE_XAML_GENERATED_MAIN
using CPRTouchVision.AppWindows;
#endif
//using CPRTouchVision.Projector;
//using Microsoft.UI.Dispatching;
//using Microsoft.UI.Windowing;
//using Microsoft.UI.Xaml.Media;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using OpenCvSharp.Extensions;
using CPRProjectorShared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
//using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
//using Windows.Graphics;
using Point = OpenCvSharp.Point;
using ProjectorManager = CPRTouchVision.Projector.ProjectorManager;

namespace CPRTouchVision.Models
{
    public interface IProjectorDisplay
    {
        void ShowImage(string path); 
        void ShowMat(Mat mat);
        void Hide();
    }

    public interface ICalibrationProgress
    {
        void OnStatus(string message); 
        void OnProgress(double value); // 0..1
        void OnCompleted(Point[] points); 
        void OnFailed(string reason); 
    }

    public class OpenCVPointConverter : JsonConverter<Point>
    {
        public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            int x = 0;
            int y = 0;

            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                if (doc.RootElement.TryGetProperty("X", out var xProp)) x = xProp.GetInt32();
                if (doc.RootElement.TryGetProperty("Y", out var yProp)) y = yProp.GetInt32();
            }

            return new Point(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteEndObject();
        }
    }


    public class CalibrationData
    {
        public int Width { get; set; }
        public int Height { get; set; }


        public Dictionary<int, Point> Centers { get; set; }

        public CalibrationData(int width, int height, Dictionary<int, Point> centers)
        {
            // Assignment is critical
            Width = width;
            Height = height;
            Centers = centers;
        }

        /// <summary>
        /// Converts the entire object to a JSON string, handling OpenCV Point fields manually.
        /// </summary>
        public string ToSerializedString()
        {
            var anonymousData = new
            {
                this.Width,
                this.Height,
                // Projecting to anonymous objects fixes the "empty {}" issue
                Centers = this.Centers.OrderBy(kvp => kvp.Key)
                                      .ToDictionary(k => k.Key.ToString(), v => new { v.Value.X, v.Value.Y })
            };

            return JsonSerializer.Serialize(anonymousData);
        }

        /// <summary>
        /// Restores a CalibrationData object from the serialized string.
        /// </summary>
        public static CalibrationData FromSerializedString(string json)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int w = root.GetProperty("Width").GetInt32();
            int h = root.GetProperty("Height").GetInt32();

            var centers = new Dictionary<int, Point>();
            foreach (var prop in root.GetProperty("Centers").EnumerateObject())
            {
                int key = int.Parse(prop.Name);
                int x = prop.Value.GetProperty("X").GetInt32();
                int y = prop.Value.GetProperty("Y").GetInt32();
                centers.Add(key, new Point(x, y));
            }

            return new CalibrationData(w, h, centers);
        }
    }

    internal class AutoCalibrationService
    {
        //private readonly IProjectorDisplay _projector;
        private readonly ICalibrationProgress _progress; // This will be the manager
        private readonly TouchManager _touchManager;
        private readonly CalibrationManager _calibrator = new CalibrationManager();

        public AutoCalibrationService(TouchManager manager)
        {
            _touchManager = manager;
            _progress = manager; // It implements the interface, so this works!
        }

        public async Task RunDetectionAsync(CancellationToken token = default)
        {
            try
            {
                App.Log("Starting auto calibration...");
                _progress.OnStatus("Starting auto calibration..."); 
                _progress.OnProgress(0.05);

                var image = _calibrator.GenerateCalibrationImageDiag(
                            Constants.ProjectorWidth,
                            Constants.ProjectorHeight);
                
                _progress.OnStatus("Projecting pattern...");
                
                var _projector = new ProjectorManager();

                // Start projector EXE
                // Connect to projector TCP server
                try
                {
                    await _projector.StartProjectorAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error for StartProjectorAsync: {ex.Message}");
                    _progress.OnFailed($"Failed to start projector: {ex.Message}");
                    return;
                }

                // Send show command with the generated image path
                await _projector.SendShowAsync(image);

                // Wait for 
                await _projector.WaitForAsync(Events.ImageDisplayed, token);
                App.Log($"Projector confirms the image is displayed");


                // 2. Capture + detect 
                App.Log("Waiting for camera frame...");
                _progress.OnStatus("Waiting for camera frame...");
                _progress.OnProgress(0.3);

                // Add timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                Point[]? points = null;
                try
                {
                    points = await DetectTouchZonePointsAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (timeoutCts.IsCancellationRequested)
                    {
                        _progress.OnFailed("Timeout waiting for camera frame. To Retry click 'Start Auto Calibration' button.");
                        return;
                    }
                }

                _projector.StopProjector();

                if (points == null || points.Length < 4) 
                {
                    _progress.OnFailed($"Failed to detect touch zone. Detected points => {points}");
                    return;
                }

                App.Log("Fitting plane and finalizing...");
                _progress.OnStatus("Fitting plane and finalizing..."); 
                _progress.OnProgress(0.8); 

                _progress.OnCompleted(points); 
                _progress.OnProgress(1.0);

                App.Log("Fitting plane and finalizing...");
                _progress.OnStatus("Auto calibration completed"); 
            } catch (OperationCanceledException) 
            {
                App.Log("Auto calibration canceled");
                _progress.OnFailed("Auto calibration canceled"); 
            } catch (Exception ex) 
            { 
                App.Log($"Auto calibration error: {ex.Message}");
                _progress.OnFailed($"Auto calibration error: {ex.Message}"); 
            } 
        } 
        private async Task<Point[]?> DetectTouchZonePointsAsync(CancellationToken token)
        {
            var frame = await _touchManager.GetNextCalibrationFrameAsync(token);
            return _calibrator.GetProjectionZone(frame);
        }
    }

    public class CalibrationManager
    {
        private readonly Dictionary _dictionary;
        private readonly DetectorParameters _detectorParams;

        double markerSizeRatio = 0.2;
        double marginRatio = 0.08;
        // Ratio of marker centers from borders
        private readonly float _markerOffsetRatio;
        public string CalibrationImagePath => GetCalibrationImagePath();
        public string CalibrationJsonPath => Path.ChangeExtension(GetCalibrationImagePath(), ".json");
        public CalibrationData? _markerCenters { get; private set; }
        private readonly ICalibrationProgress? _progress;
        public CalibrationManager(ICalibrationProgress? progress = null, float markerOffsetRatio = 0.15f)
        {
            _progress = progress;
            _dictionary = CvAruco.GetPredefinedDictionary(
                PredefinedDictionaryType.Dict4X4_50);

            _detectorParams = new DetectorParameters();

            _markerOffsetRatio = markerOffsetRatio;
        }
        public string GetCalibrationImagePath()
        {
            string homeDirectory = Utilities.GetHomeDirectory();

            return Path.Combine(homeDirectory, Constants.CalibrationImageFile);
        }

        private void Notify(
            string channel,
            Point[]? points = null,
            string message = "",
            double percent = 0
            )
        {
            if (_progress == null) return;
            switch (channel)
            {
                case Constants.StatusChannel:
                    _progress?.OnStatus(message);
                    break;
                case Constants.ProgressChannel:
                    _progress?.OnProgress(percent);
                    break;
                case Constants.CompletionChannel:
                    if (points != null)
                        _progress?.OnCompleted(points);
                    else
                        _progress?.OnFailed("Cannot detect projection zone.");
                    break;
                case Constants.FailureChannel:
                    _progress?.OnFailed(message);
                    break;

            }
            _progress?.OnStatus(message);
            _progress?.OnProgress(percent);
        }
        // -------------------------------------------------------
        // 1. Generate calibration image
        // -------------------------------------------------------

        public string GenerateCalibrationImage(int width, int height, bool useExisting = false, string? path = null)
        {
            path ??= GetCalibrationImagePath();
            string jsonPath = Path.ChangeExtension(path, ".json");

            if (useExisting && File.Exists(path) && File.Exists(jsonPath))
            {
                string readData = File.ReadAllText(jsonPath);
                var restoredData = CalibrationData.FromSerializedString(readData);
                if (restoredData != null)
                {
                    _markerCenters = restoredData;
                    Notify(Constants.StatusChannel, message: "Loaded existing calibration image.");
                    return path;
                }
            }

            Mat img = new Mat(height, width, MatType.CV_8UC3, Scalar.White);

            int markerSize = (int)(Math.Min(width, height) * markerSizeRatio);
            int margin = (int)(Math.Min(width, height) * marginRatio);

            void DrawMarker(int id, Point c)
            {
                Mat marker = new Mat();
                _dictionary.GenerateImageMarker(id, markerSize, marker);

                var roi = new Mat(img, new Rect(c.X, c.Y, markerSize, markerSize));
                Cv2.CvtColor(marker, roi, ColorConversionCodes.GRAY2BGR);
            }

            // layout:
            // 0 = TL, 1 = TR, 2 = BR, 3 = BL
            var centers = new Dictionary<int, Point> {
                { 0, new Point(margin, margin) },
                { 1, new Point(width - margin - markerSize, margin) },
                { 2, new Point(width - margin - markerSize, height - margin - markerSize) },
                { 3, new Point(margin, height - margin - markerSize) }
            };

            foreach (var center in centers)
            {
                DrawMarker(center.Key, center.Value);
            }

            _markerCenters = new CalibrationData(width, height, centers);
            string jsonString = _markerCenters.ToSerializedString();
            File.WriteAllText(jsonPath, jsonString);
            Cv2.ImWrite(path, img);
            Notify(Constants.StatusChannel, message: "Calibration image is generated and saved.");
            return path;
        }

        public Mat GenerateCalibrationMatDiag(int width, int height, out Dictionary<int, Point> centers)
        {
            Mat img = new Mat(height, width, MatType.CV_8UC3, Scalar.White);

            int size = Math.Min(width, height);

            int markerSize = (int)(size * markerSizeRatio);

            // prevent markers overlapping borders
            int maxMarker = (int)(Math.Min(width, height) * _markerOffsetRatio * 2);
            markerSize = Math.Min(markerSize, maxMarker);

            void DrawMarkerCentered(int id, Point center)
            {
                Mat marker = new Mat();
                _dictionary.GenerateImageMarker(id, markerSize, marker);

                int x = center.X - markerSize / 2;
                int y = center.Y - markerSize / 2;

                // safety clamp
                x = Math.Clamp(x, 0, width - markerSize);
                y = Math.Clamp(y, 0, height - markerSize);

                var roi = new Mat(img, new Rect(x, y, markerSize, markerSize));
                Cv2.CvtColor(marker, roi, ColorConversionCodes.GRAY2BGR);
            }

            // marker centers on diagonals
            int centerOffsetX = (int)(width * _markerOffsetRatio);
            int centerOffsetY = (int)(height * _markerOffsetRatio);

            centers = new Dictionary<int, Point> {
                { 0, new Point(centerOffsetX, centerOffsetY) },
                { 1, new Point(width - centerOffsetX, centerOffsetY) },
                { 2, new Point(width - centerOffsetX, height - centerOffsetY) },
                { 3, new Point(centerOffsetX, height - centerOffsetY) }
            };

            foreach (var center in centers)
            {
                DrawMarkerCentered(center.Key, center.Value);
            }
            return img;
        }

        public string GenerateCalibrationImageDiag(int width, int height, bool useExisting = false, string? path = null)
        {
            path ??= GetCalibrationImagePath();
            string jsonPath = Path.ChangeExtension(path, ".json");

            if (useExisting && File.Exists(path) && File.Exists(jsonPath))
            {
                string readData = File.ReadAllText(jsonPath);
                var restoredData = CalibrationData.FromSerializedString(readData);
                if (restoredData != null)
                {
                    _markerCenters = restoredData;
                    Notify(Constants.StatusChannel, message: "Loaded existing calibration image.");
                    return path;
                }
            }

            Dictionary<int, Point> centers;

            var img = GenerateCalibrationMatDiag(width, height, out centers);

            _markerCenters = new CalibrationData(width, height, centers);
            string jsonString = _markerCenters.ToSerializedString();
            File.WriteAllText(jsonPath, jsonString);
            // optional border (reduce thickness to avoid covering markers)
            //Cv2.Rectangle(img, new Rect(0, 0, width - 1, height - 1), Scalar.Black, 10);

            Cv2.ImWrite(path, img);
            Notify(Constants.StatusChannel, message: "Calibration image is generated and saved.");
            return path;
        }

        // -------------------------------------------------------
        // 2. Detect markers
        // -------------------------------------------------------
        public Dictionary<int, Point2f> DetectMarkers(Mat frame)
        {
            Notify(Constants.StatusChannel, message: "Start detecting markers.");
            Point2f[][] corners;
            int[] ids;
            Point2f[][] rejected;

            CvAruco.DetectMarkers(frame, _dictionary, out corners, out ids, _detectorParams, out rejected);

            var result = new Dictionary<int, Point2f>();

            if (ids == null || ids.Length == 0)
            {
                Notify(Constants.FailureChannel, message: "Markers are not detected.");
                return result;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                // center of marker
                var center = new Point2f(
                    corners[i].Average(p => p.X),
                    corners[i].Average(p => p.Y));

                result[ids[i]] = center;
            }
            Notify(Constants.StatusChannel, message: "Markers are detected.");
            return result;
        }

        public Point[] DetectProjectionCountour(Mat frame)
        {

            Mat gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

            Mat blur = new Mat();
            Cv2.GaussianBlur(gray, blur, new OpenCvSharp.Size(9, 9), 0);

            Mat thresh = new Mat();
            Cv2.Threshold(blur, thresh, 200, 255, ThresholdTypes.Binary);

            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5));
            Cv2.MorphologyEx(thresh, thresh, MorphTypes.Open, kernel);

            Point[][] contours;
            HierarchyIndex[] hierarchy;

            Cv2.FindContours(
                thresh,
                out contours,
                out hierarchy,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple
            );

            if (contours.Length == 0)
                return null;
            Debug.WriteLine($"Found {contours.Length} contours.");
            foreach (var c in contours)
            {
                Debug.WriteLine($"Area: {Cv2.ContourArea(c)}. Amount of nodes: {c.Length}");
            }
            var projection = contours
                .OrderByDescending(c => Cv2.ContourArea(c))
                .First();
            Debug.WriteLine($"Largest contour: {projection}");
            // largest contour = projection
            return projection;
        }

        public Point2f[]? DetectProjectionQuad(Mat frame)
        {
            Notify(Constants.StatusChannel, message: "Start to detect projection.");
            var largest = DetectProjectionCountour(frame);
            if (largest == null) return null;

            // FIX: Generate the outer convex boundary to ignore the "muted" icon notch
            Point[] hull = Cv2.ConvexHull(largest);

            // Approximate the hull instead of the raw contour
            double epsilon = 0.02 * Cv2.ArcLength(hull, true);
            Point[] approx = Cv2.ApproxPolyDP(hull, epsilon, true);

            if (approx.Length != 4) return null;

            return approx.Select(p => new Point2f(p.X, p.Y)).ToArray();
        }

        public Rect? DetectProjectionROI(Mat frame)
        {
            var largest = DetectProjectionCountour(frame);
            return Cv2.BoundingRect(largest);
        }

        public Point2f[] OrderQuadUsingMarkers(
            Point2f[] contourQuad,
            Dictionary<int, Point2f> markerCenters)
        {
            if (contourQuad == null || contourQuad.Length != 4)
                return null;
            var ordered = new Point2f[4];


            var remaining = contourQuad.ToList();
            foreach (var kv in markerCenters)
            {
                int id = kv.Key;
                var marker = kv.Value;

                var closest = remaining
                    .OrderBy(p => Distance(p, marker))
                    .First();

                ordered[id] = closest;
                // This prevents duplicate assignments
                remaining.Remove(closest);
            }

            return ordered;
        }

        private double Distance(Point2f a, Point2f b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public Point[]? GetProjectionZone(Mat imageMat)
        {
            var corners = GetOrderedCorners(imageMat);
            if (corners == null)
            {
                Debug.WriteLine("Failed to get ordered corners.");
                return null;
            }
            return Array.ConvertAll(corners, p => p.ToPoint());
        }


        public Point[]? GetProjectionZone(Image img)
        {
            Bitmap frame = new Bitmap(img);
            Mat imageMat = BitmapConverter.ToMat(frame);

            return GetProjectionZone(imageMat);
        }

        public Point[]? GetProjectionZone(CalibrationFrame frame)
        {
            // 1. Initialize Mat with the source format (assume RGBA from frame)
            using Mat rgbaMat = new Mat(frame.Height, frame.Width, MatType.CV_8UC4);

            // 2. Copy the raw data
            Marshal.Copy(frame.ColorData, 0, rgbaMat.Data, frame.ColorData.Length);

            // 3. Convert RGBA to BGR (OpenCV's preferred saving format)
            // This correctly maps the colors and flattens the alpha if needed
            Mat finalMat = new Mat();
            Cv2.CvtColor(rgbaMat, finalMat, ColorConversionCodes.RGBA2BGR);
#if DEBUG
            string debugPath = Path.Combine(Utilities.GetHomeDirectory(), "debug_frame.png");
            finalMat.ImWrite(debugPath);
            Debug.WriteLine($"Saved debug frame to {debugPath}");
#endif

            return GetProjectionZone(finalMat);
        }

        public Point2f[]? GetOrderedCorners(Mat frame)
        {
            var corners = DetectProjectionQuad(frame);
            if (corners == null)
            {
                Debug.WriteLine("No corners detected");
                return null;
            }
            var markers = DetectMarkers(frame);

            return OrderQuadUsingMarkers(corners, markers);
        }
    }

}
