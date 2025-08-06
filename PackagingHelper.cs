using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision
{
    public static class PackagingHelper
    {
        // P/Invoke declaration for GetCurrentPackageFullName from kernel32.dll
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder packageFullName);

        /// <summary>
        /// Returns true if the app is running as a packaged app (e.g. MSIX).
        /// Returns false if running unpackaged (standalone).
        /// </summary>
        public static bool IsRunningPackaged()
        {
            int length = 0;
            // Call with length=0 and null StringBuilder to get required length
            int result = GetCurrentPackageFullName(ref length, null);

            // APPMODEL_ERROR_NO_PACKAGE (15700) means no package identity (unpackaged)
            const int APPMODEL_ERROR_NO_PACKAGE = 15700;

            return result != APPMODEL_ERROR_NO_PACKAGE;
        }
    }
}
