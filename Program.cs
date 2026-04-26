using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using System;
using System.Diagnostics;

#if DISABLE_XAML_GENERATED_MAIN
namespace CPRTouchVision
{
    class Startup
    {
        [STAThread]
        static void Main(string[] args)
        {

            var file = "C:\\Users\\CPR-PC\\source\\repos\\Projects\\bootstrap-test.txt";

            try
            {
                /// Check if self - contained WinUI DLLs are present
                var dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                var muiXaml = System.IO.Path.Combine(dir, "Microsoft.ui.xaml.dll");
                Debug.WriteLine($"Microsoft.ui.xaml.dll exists: {System.IO.File.Exists(muiXaml)}");
                Debug.WriteLine($"\nOutput dir: {dir}");
                Debug.WriteLine("\nBefore Bootstrap init");
                Bootstrap.Initialize(0x00010006, "", new PackageVersion(0, 0, 0, 0));
                Debug.WriteLine("\nAfter Bootstrap init");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("\nBootstrap FAILED: " + ex.ToString());
            }

            Application.Start((p) =>
            {
                Debug.WriteLine("\nBefore App init");
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    new CPRTouchVision.App();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("\nCRASH: " + ex.ToString());
                }
            });

            Bootstrap.Shutdown();


        }
    }
}
#endif
