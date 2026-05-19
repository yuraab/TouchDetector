using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public static class Constants
    {
        public const bool InitialIsAutoMode = true;
        public const bool InitialStartInAutoMode = true;

        public const int MaxROIAttempts = 5;

        public const string TopLeft = "TopLeft";
        public const string TopRight = "TopRight";
        public const string BottomRight = "BottomRight";
        public const string BottomLeft = "BottomLeft";
        public static string[] QRLabels = new[] { TopLeft, TopRight, BottomRight, BottomLeft };
        public const int ProjectorWidth = 1920;
        public const int ProjectorHeight = 1080;
       
        public const string ProjectorsFile = "projectors.ini";
        public const string HomographyFile = "homography.yml";

        // interface channels
        public const string StatusChannel = "status_channel";
        public const string ProgressChannel = "progress_channel";
        public const string CompletionChannel = "completion_channel";
        public const string FailureChannel = "failure_channel";
        public const string PlaneFittingFailed = "Plane fitting failed. Not enough inliers or points.";
        public const string WallPlaneNormalZero = "The wall plane normal cannot be a zero vector.";

        //Messages

        //Failures
        public const string ZoneDetectionFailed = "Failed to detect touch zone.";
        public const string ZoneDetectionUnexpected = "Detected zone does not meet expectations";
        public const string CalibrationFailedCameraPosition = "Calibration failed: Camera position is not defined. Please ensure the camera is properly mounted and try again.";
        public const string FrameCollectionFailed = "Frame collection failed.";
        public const string ROIForPlaneFittingFailed = "ROI for plane fitting failed.";

        //Info
        public const string CollectingFramesForPlaneFitting = "Collecting frames for plane fitting... Please wait and do not move.";
        public const string FittingPlane = "Fitting plane... Please wait.";
        public const string WallPlaneDetected = "Wall plane detected.";

        [Obfuscation(Exclude = true, Feature = "string encryption")]
        public static string CONFIG_SUB_FOLDER = "CPR Touch Vision";
        public const string LOG_FOLDER = "C:\\Users\\CPR-PC\\CPRTouchVisionLogs";
        public static string CONFIG_FOLDER => System.IO.Path.Combine(CommonFolderPath, CONFIG_SUB_FOLDER);
        [Obfuscation(Exclude = true, Feature = "string encryption")]
        public static string CONFIG_PATH => System.IO.Path.Combine(CONFIG_FOLDER, "config.json");
        public const string CalibrationImageFile = "calibration_image.png";
        public static string HomeDir => CONFIG_FOLDER;
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

        public static string FailureIndicatorMessage => "Failure";

        public static double TouchZoneDistanceThreshold = 10;
        
        public const int CalibrationAttemptsDelay = 2000; // in ms
        
    }

    public class Utilities
    {
        public static string GetHomeDirectory()
        {
            if (!Directory.Exists(Constants.HomeDir))
            {
                Directory.CreateDirectory(Constants.HomeDir);
            }
            return Constants.HomeDir;
#if DEBUG
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#else
            return AppDomain.CurrentDomain.BaseDirectory;
#endif
        }

        public static void SaveCapturedFrame(ushort[] frameData, string filePath)
        {
            // ushort is 2 bytes, so multiply length by 2
            byte[] byteArray = new byte[frameData.Length * 2];
            Buffer.BlockCopy(frameData, 0, byteArray, 0, byteArray.Length);

            // Write all bytes to disk instantly
            File.WriteAllBytes(filePath, byteArray);
        }

    }

    public interface ICalibrationProgress
    {
        void OnStatus(string message);
        void OnProgress(double value); // 0..1
        void OnCompleted(Point[] points);
        void OnFailed(string reason);
    }
}
