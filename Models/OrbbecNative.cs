using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public static class OrbbecNative
    {
        const string Dll = "libobsensor"; // adjust as needed

        [DllImport(Dll)]
        public static extern IntPtr ob_create_noise_removal_filter(out IntPtr error);

        [DllImport(Dll)]
        public static extern void ob_noise_removal_filter_set_filter_params(IntPtr filter, ob_noise_removal_filter_params parameters, out IntPtr error);

        [DllImport(Dll)]
        public static extern IntPtr ob_filter_process(IntPtr filter, IntPtr frame, out IntPtr error);

        [DllImport(Dll)]
        public static extern void ob_delete_filter(IntPtr filter, out IntPtr error);

        [StructLayout(LayoutKind.Sequential)]
        public struct ob_noise_removal_filter_params
        {
            public ushort disp_diff;
            public int max_size;
        }
    }

}
