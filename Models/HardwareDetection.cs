using System.Management;

namespace HardwareDetection;

public static class GpuDetector
{
    public static bool HasNvidiaGpu(out string? name)
    {
        name = null;

        try
        {
            using var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var gpuName = obj["Name"]?.ToString()?.ToLowerInvariant();
                if (gpuName != null && gpuName.Contains("nvidia"))
                {
                    name = gpuName;
                    return true;
                }
            }
        }
        catch { }

        return false;
    }
}

