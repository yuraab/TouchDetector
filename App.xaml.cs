using HardwareDetection;
using Microsoft.UI.Xaml;
using OBSharp;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using WinUIEx.Messaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CPRTouchVision
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {

        private Window? _window;
        public App()
        {
#if DEBUG
            ObSharpLogger.LogAction = Log;
            AttachConsole(); // Optional debug console
#endif
            string? userHome = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(userHome))
            {
                userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); // default fallback
            }

            //File.WriteAllText(LogFilePath, $"[LOG START] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            /// <summary>
            /// Initializes the singleton application object.  This is the first line of authored code
            /// executed, and as such is the logical equivalent of main() or WinMain().
            /// </summary>
            //// Register all thrid party dlls required to work with Orbbec Camera
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CPR Soft", "bin");
            Log($"Registering native DLL path: {path}");
            try
            {
                NativeLibLoader.RegisterAdditionalDllDirectory(path);
            }
            catch (DirectoryNotFoundException ex)
            {
                Log($"[ERROR] Native DLL directory not found: {ex.Message}");
                File.AppendAllText("startup-errors.log", ex.ToString() + Environment.NewLine);
            }

            // Global exception logging
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Log($"[UnhandledException] {ex?.Message}\n{ex?.StackTrace}");
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log($"[UnobservedTaskException] {e.Exception.Message}\n{e.Exception.StackTrace}");
                e.SetObserved();
            };

            // Assign GPU detection delegate early
            TrackerInitHelper.GpuDetectionFunc = () =>
            {
                bool hasGpu = GpuDetector.HasNvidiaGpu(out var name);
                return (hasGpu, name);
            };
            
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

        private void LogUnhandled(string source, Exception? ex)
        {
            string msg = $"[{source}] Unhandled Exception:\n{ex?.Message}\n{ex?.StackTrace}";
            Console.WriteLine(msg);
            File.AppendAllText("unhandled.log", msg + Environment.NewLine);
        }


        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        private static void AttachConsole()
        {
            AllocConsole();
            Console.WriteLine("Debug console attached.");
            Log("Console attached.");
        }

        private static readonly string LogFilePath = InitLogFilePath();

        private static string InitLogFilePath()
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            string? userHome = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(userHome))
            {
                userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            string logDir = Path.Combine(userHome, "CPRTouchVisionLogs");
            Directory.CreateDirectory(logDir);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return Path.Combine(logDir, $"app_{timestamp}.log");
        }
        public static void Log(
            string message,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0
            )
        {
#if DEBUG
            try
            {
                var info = "";
                if (file != "")
                    info = $"[{Path.GetFileName(file)}]";

                if (caller != "")
                    info += $"[{caller}]";

                if (line != 0)
                    info += $"[line:{line}]";


                string logEntry = $"[{DateTime.Now:HH:mm:ss}]{info} {message}\n";
                //Console.WriteLine("Preparing to write to log file: " + LogFilePath);
                Console.WriteLine(logEntry);

                File.AppendAllText(LogFilePath, logEntry);

                //Console.WriteLine("Log write successful.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logging failed: {ex.GetType().Name} - {ex.Message}");
            }
#endif

        }
    }
}
