using System;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;


class Startup
{
    [STAThread]
    static void Main(string[] args)
    {
        var file = "C:\\Users\\CPR-PC\\source\\repos\\Projects\\bootstrap-test.txt";
        System.IO.File.WriteAllText(file, "Main started");
        try
        {
            Bootstrap.Initialize(0x00010006, "", new PackageVersion(0, 0, 0, 0));
            System.IO.File.AppendAllText(file, "\nBootstrap OK");
            //System.IO.File.AppendAllText(file, "\nMain started - no bootstrap");

        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(file, "\nBootstrap FAILED: " + ex.ToString());
        }

        Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                System.IO.File.AppendAllText(file, "\nBefore App()");
                new CPRTouchVision.App();
                System.IO.File.AppendAllText(file, "\nAfter App()");
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(file, "\nCRASH: " + ex.ToString());
            }
        });

        Bootstrap.Shutdown();
    }
}

