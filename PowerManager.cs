using System;
using System.Runtime.InteropServices;

namespace LidSleepManager
{
    public class PowerManager
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [FlagsAttribute]
        private enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        private static bool isPreventingSleep = false;

        public static void PreventSleep()
        {
            if (!isPreventingSleep)
            {
                SetThreadExecutionState(
                    EXECUTION_STATE.ES_CONTINUOUS |
                    EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
                isPreventingSleep = true;
            }
        }

        public static void AllowSleep()
        {
            if (isPreventingSleep)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                isPreventingSleep = false;
            }
        }

        public static bool IsPreventingSleep()
        {
            return isPreventingSleep;
        }
    }
}
