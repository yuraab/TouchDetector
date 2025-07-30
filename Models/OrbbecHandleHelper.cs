using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using OB = OBSharp.Sensor;

namespace CPRTouchVision.Models
{
    public static class OrbbecHandleHelper
    {
        public static IntPtr GetNativeHandle(OB.Image image)
        {
            // Step 1: Access the private "handle" field in OB.Image
            var handleWrapperField = typeof(OB.Image).GetField("handle", BindingFlags.NonPublic | BindingFlags.Instance);
            var handleWrapper = handleWrapperField?.GetValue(image);
            if (handleWrapper == null) return IntPtr.Zero;

            // Step 2: Access the public .Value property (returns ImageHandle struct)
            var valueProp = handleWrapper.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            var imageHandle = valueProp?.GetValue(handleWrapper);
            if (imageHandle == null) return IntPtr.Zero;

            // Step 3: Access the private "value" field inside ImageHandle
            var rawHandleField = imageHandle.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
            var rawIntPtr = rawHandleField?.GetValue(imageHandle);
            return rawIntPtr is IntPtr ptr ? ptr : IntPtr.Zero;
        }
    }
}
