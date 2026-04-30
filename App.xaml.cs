#if !DISABLE_XAML_GENERATED_MAIN
using CPRLib;
using CPRTouchVision.AppWindows;
#endif
using CPRTouchVision.Models;
using Emgu.CV.Structure;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using OBSharp;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Devices.Display.Core;
using WinUIEx;
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
        public static MainWindow Main { get; private set; } = null!;
        public DispatcherQueue DispatcherQueue { get => Main.DispatcherQueue; }
        public bool StartMinimized { get; set; } = false;
        public bool AutoTrack { get; set; } = false;

        new static public App Current => (App)Application.Current;


        public App()
        {
            this.UnhandledException += (s, e) =>
            {
                Log("Unhandled: " + e.Exception.ToString());
                e.Handled = true;
            };
            //ObSharpLogger.LogAction = Log;
#if DEBUG || DISABLE_XAML_GENERATED_MAIN
            AttachConsole(); // Optional debug console
#endif
            string? userHome = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(userHome))
            {
                userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); // default fallback
            }

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
                //Log($"[UnhandledException] {ex?.Message}\n{ex?.StackTrace}");
                var msg = $"[UnhandledException] {ex?.Message}\n{ex?.StackTrace}";
                Console.WriteLine(msg); // Always works
                File.AppendAllText("startup-errors.log", msg + Environment.NewLine);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log($"[UnobservedTaskException] {e.Exception.Message}\n{e.Exception.StackTrace}");
                e.SetObserved();
            };

            // Assign GPU detection delegate early
            /*
            TrackerInitHelper.GpuDetectionFunc = () =>
            {
                try
                {
                    bool hasGpu = GpuDetector.HasNvidiaGpu(out var name);
                    return (hasGpu, name);
                }
                catch (Exception ex)
                {
                    Log("GpuDetectionFunc error: " + ex.Message);
                    return (false, null);
                }
            };
            */
            try
            {
                Log("Before InitializeComponent");
                InitializeComponent();
                Log("After InitializeComponent");
            }
            catch (Exception ex)
            {
                Log("InitializeComponent error: " + ex.ToString());
                File.AppendAllText("startup-errors.log", ex.ToString() + Environment.NewLine);
            }
        }

#if DISABLE_XAML_GENERATED_MAIN
        private Microsoft.UI.Xaml.Window? m_window;

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                Log("OnLaunched started");
                m_window = new MainWindow();
                m_window.CenterOnScreen();
                m_window.SetIcon("Assets/favicon.ico");
                m_window.Activate();
                Log("OnLaunched completed");
            }
            catch (Exception ex)
            {
                Log("OnLaunched error: " + ex.Message);
                File.AppendAllText("startup-errors.log", ex.ToString() + Environment.NewLine);
            }
        }
#endif

#if !DISABLE_XAML_GENERATED_MAIN
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {


            string? startCommand = null;

            if (AppInstance.GetActivatedEventArgs() is IActivatedEventArgs activatedArgs)
            {
                if (activatedArgs.Kind == ActivationKind.Protocol && activatedArgs is ProtocolActivatedEventArgs protocolArgs)
                {
                    StartMinimized = true;
                    startCommand = protocolArgs.Uri.Host;

                    switch (startCommand)
                    {
                        case "start":
                            AutoTrack = true;
                            break;
                        default:
                            break;
                    }
                }
            }

            if (!IsFirstInstance())
            {
                ForwardStartCommand(startCommand);
                return;
            }

            var init = new InitWindow();
            init.CenterOnScreen();

            if (StartMinimized)
                init.ActivateMinimized();
            else
                init.Activate();     
          
        }

        public void Setup()
        {
            Main = new MainWindow();
            Main.CenterOnScreen();
            Main.SetIcon("Assets/favicon.ico");

            if (StartMinimized)
                Main.ActivateMinimized();
            else
                Main.Activate();
        }

        private bool IsFirstInstance()
        {
            return Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length == 1;
        }

        private async void ForwardStartCommand(string? startCommand)
        {
            if (startCommand != null)
            {
                var client = new PipeClient(PipeName.TouchVision);
                await client.Connect();
                await client.SendMessage(startCommand);
            }

            Environment.Exit(0);
        }

        private async Task<bool> RedirectToMainInstance()
        {
            var main = AppInstance.FindOrRegisterInstanceForKey("cpr-touch-vision");
            
            

            return false;
        }
#endif
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
            Console.WriteLine("Console attached.");
            Console.WriteLine("Calling Log now...");
            Log("Debug console attached.");
            Console.WriteLine("Returned from Log.");
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
#if DEBUG || TEST || DISABLE_XAML_GENERATED_MAIN
            try
            {
                // Get stack trace to find parent method
                var stackTrace = new StackTrace();
                string parentMethod = "";
                // stackTrace.GetFrame(1) is the direct caller
                // stackTrace.GetFrame(2) is the parent of the caller
                if (stackTrace.FrameCount > 2)
                {
                    var parentFrame = stackTrace.GetFrame(2);
                    var method = parentFrame.GetMethod();
                    parentMethod = $"[{method.DeclaringType?.FullName}]:[{method.Name}]";
                }
                var info = parentMethod;
                if (file != "")
                    info += $"[{Path.GetFileName(file)}]";

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
#else
            Console.WriteLine("[RELEASE LOG] " + message);
#endif

        }
    }

}
