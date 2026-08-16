using System;
using System.Runtime.InteropServices;

namespace SleepMngr
{
    internal static class LidPowerSettings
    {
        private static readonly Guid SystemButtonSubgroup = new("4f971e89-eebd-4455-a8de-9e59040e7347");
        private static readonly Guid LidActionSetting = new("5ca83367-6e45-459f-a27b-476b1d01c936");

        [DllImport("PowrProf.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("PowrProf.dll")]
        private static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint acValueIndex);

        [DllImport("PowrProf.dll")]
        private static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint dcValueIndex);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static bool TryReadCurrent(out string acValue, out string dcValue, out string error)
        {
            acValue = string.Empty;
            dcValue = string.Empty;
            error = string.Empty;
            IntPtr schemePtr = IntPtr.Zero;

            try
            {
                uint getSchemeResult = PowerGetActiveScheme(IntPtr.Zero, out schemePtr);
                if (getSchemeResult != 0 || schemePtr == IntPtr.Zero)
                {
                    error = $"PowerGetActiveScheme failed with Win32 error {getSchemeResult}";
                    return false;
                }

                Guid schemeGuid = Marshal.PtrToStructure<Guid>(schemePtr);
                Guid subgroupGuid = SystemButtonSubgroup;
                Guid settingGuid = LidActionSetting;

                uint acResult = PowerReadACValueIndex(
                    IntPtr.Zero,
                    ref schemeGuid,
                    ref subgroupGuid,
                    ref settingGuid,
                    out uint acIndex);

                if (acResult != 0)
                {
                    error = $"PowerReadACValueIndex failed with Win32 error {acResult}";
                    return false;
                }

                subgroupGuid = SystemButtonSubgroup;
                settingGuid = LidActionSetting;
                uint dcResult = PowerReadDCValueIndex(
                    IntPtr.Zero,
                    ref schemeGuid,
                    ref subgroupGuid,
                    ref settingGuid,
                    out uint dcIndex);

                if (dcResult != 0)
                {
                    error = $"PowerReadDCValueIndex failed with Win32 error {dcResult}";
                    return false;
                }

                acValue = acIndex.ToString();
                dcValue = dcIndex.ToString();
                return true;
            }
            catch (Exception ex)
            {
                error = $"PowrProf read failed: {ex.Message}";
                return false;
            }
            finally
            {
                if (schemePtr != IntPtr.Zero)
                    LocalFree(schemePtr);
            }
        }
    }
}
