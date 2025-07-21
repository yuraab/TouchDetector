using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CPRTouchVision
{
    internal class TrackerInitHelper
    {
        public static Func<(bool HasGpu, string? GpuName)>? GpuDetectionFunc { get; set; }

        public static bool HasCompatibleGpu(out string? gpuName)
        {
            if (GpuDetectionFunc != null)
            {
                var result = GpuDetectionFunc();
                
                gpuName = result.GpuName;
                App.Log($"GPU detected: {gpuName}");
                return result.HasGpu;
            }

            gpuName = null;
            App.Log($"GPU not detected");
            return false;
        }
    }
}
