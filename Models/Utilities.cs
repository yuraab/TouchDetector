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

        [Obfuscation(Exclude = true, Feature = "string encryption")]
        public static string CONFIG_SUB_FOLDER = "CPR Touch Vision";
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

        
    }
}
