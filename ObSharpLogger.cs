using System;
using System.Runtime.CompilerServices;

namespace CPRTouchVision
{
    public delegate void DetailedLogDelegate(string message, string caller, string file, int line);
    public class ObSharpLogger
    {
        public static DetailedLogDelegate? LogAction { get; set; }

        public static void Log(
            string message,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0
        )
        {
#if DEBUG
            LogAction?.Invoke(message, caller, file, line);
#endif
        }
    }
}
