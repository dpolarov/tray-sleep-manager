using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SleepMngr
{
    public class LidActionManager
    {
        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SleepMngr");

        private static readonly string PersistedOriginalsFile = Path.Combine(
            StateDirectory,
            "lid_original.txt");

        private readonly Func<string, PowerCfgResult> runPowerCfg;
        private string? originalLidActionAC;
        private string? originalLidActionDC;
        private bool isModified;
        private bool originalsSaved;

        public LidActionManager() : this(PowerCfgRunner.Run, loadPersistedState: true)
        {
        }

        internal LidActionManager(Func<string, PowerCfgResult> powerCfgRunner)
            : this(powerCfgRunner, loadPersistedState: false)
        {
        }

        private LidActionManager(Func<string, PowerCfgResult> powerCfgRunner, bool loadPersistedState)
        {
            runPowerCfg = powerCfgRunner;

            if (loadPersistedState)
                LoadPersistedOriginals();
        }

        public string? LastError { get; private set; }

        public bool SetLidActionDoNothing()
        {
            LastError = null;

            if (!EnsureOriginalsSaved())
                return false;

            if (ApplyLidActions("0", "0", out string applyError))
            {
                isModified = true;
                return true;
            }

            // A command may have changed only AC or DC before another command failed.
            // Restore the saved settings best-effort so lack of elevation cannot leave
            // an unnoticed partial configuration behind.
            bool rollbackOk = ApplyLidActions(
                originalLidActionAC!,
                originalLidActionDC!,
                out string rollbackError);

            if (rollbackOk)
            {
                ClearSavedOriginals();
            }
            else
            {
                // Keep the persisted originals. A later application start can then
                // recover them even if this process terminates before restoration.
                isModified = true;
            }

            string message = $"Could not set lid action to Do nothing: {applyError}";
            if (!rollbackOk)
                message += $" | Rollback also failed: {rollbackError}";

            SetLastError(message);
            return false;
        }

        public bool RestoreLidAction()
        {
            LastError = null;

            if (!isModified)
                return true;

            if (!originalsSaved || originalLidActionAC == null || originalLidActionDC == null)
            {
                SetLastError("Original lid settings are unavailable; refusing to guess a restore value");
                return false;
            }

            if (!ApplyLidActions(originalLidActionAC, originalLidActionDC, out string error))
            {
                SetLastError($"Could not restore original lid settings: {error}");
                return false;
            }

            AppLog.Write(
                "LidActionManager",
                $"Restored original lid settings: AC={originalLidActionAC}, DC={originalLidActionDC}");
            ClearSavedOriginals();
            return true;
        }

        private bool EnsureOriginalsSaved()
        {
            if (originalsSaved)
                return true;

            if (!TryGetCurrentLidActions(out string ac, out string dc, out string error))
            {
                SetLastError($"Could not read original lid settings: {error}");
                return false;
            }

            // Persist before changing Windows. If the process is killed after the
            // powercfg write, the next instance must still know what to restore.
            if (!TryPersistOriginals(ac, dc, out string persistError))
            {
                SetLastError($"Could not persist original lid settings: {persistError}");
                return false;
            }

            originalLidActionAC = ac;
            originalLidActionDC = dc;
            originalsSaved = true;
            AppLog.Write("LidActionManager", $"Saved original lid settings: AC={ac}, DC={dc}");
            return true;
        }

        private void LoadPersistedOriginals()
        {
            try
            {
                if (!File.Exists(PersistedOriginalsFile))
                    return;

                string[] parts = File.ReadAllText(PersistedOriginalsFile)
                    .Trim()
                    .Split('|', StringSplitOptions.TrimEntries);

                if (parts.Length != 2 ||
                    !uint.TryParse(parts[0], out _) ||
                    !uint.TryParse(parts[1], out _))
                {
                    AppLog.Write(
                        "LidActionManager",
                        $"Ignoring invalid persisted lid settings in {PersistedOriginalsFile}");
                    return;
                }

                originalLidActionAC = parts[0];
                originalLidActionDC = parts[1];
                originalsSaved = true;

                // File existence means a previous instance had started changing the
                // lid action and had not confirmed restoration. Treat it as modified
                // conservatively so Auto/no-monitor startup restores the saved values.
                isModified = true;
                AppLog.Write(
                    "LidActionManager",
                    $"Recovered persisted original lid settings: AC={originalLidActionAC}, DC={originalLidActionDC}");
            }
            catch (Exception ex)
            {
                AppLog.Write("LidActionManager", $"Could not load persisted lid settings: {ex.Message}");
            }
        }

        private static bool TryPersistOriginals(string acValue, string dcValue, out string error)
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                File.WriteAllText(PersistedOriginalsFile, $"{acValue}|{dcValue}");
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void ClearSavedOriginals()
        {
            try
            {
                if (File.Exists(PersistedOriginalsFile))
                    File.Delete(PersistedOriginalsFile);
            }
            catch (Exception ex)
            {
                // Restoration itself already succeeded. Keep running, but make the
                // stale recovery-file failure visible for diagnostics.
                AppLog.Write(
                    "LidActionManager",
                    $"Could not delete persisted lid settings after restore: {ex.Message}");
            }

            isModified = false;
            originalsSaved = false;
            originalLidActionAC = null;
            originalLidActionDC = null;
        }

        private bool ApplyLidActions(string acValue, string dcValue, out string error)
        {
            var failures = new List<string>();

            var acResult = runPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {acValue}");
            if (!acResult.Success)
                failures.Add($"AC: {acResult.DescribeFailure()}");

            var dcResult = runPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {dcValue}");
            if (!dcResult.Success)
                failures.Add($"DC: {dcResult.DescribeFailure()}");

            var activateResult = runPowerCfg("/setactive SCHEME_CURRENT");
            if (!activateResult.Success)
                failures.Add($"activate: {activateResult.DescribeFailure()}");

            if (failures.Count > 0)
            {
                error = string.Join(" | ", failures);
                return false;
            }

            if (!TryGetCurrentLidActions(out string actualAc, out string actualDc, out string queryError))
            {
                error = $"commands returned success but verification failed: {queryError}";
                return false;
            }

            if (actualAc != acValue || actualDc != dcValue)
            {
                error = $"verification mismatch: expected AC={acValue}, DC={dcValue}; actual AC={actualAc}, DC={actualDc}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool TryGetCurrentLidActions(out string acValue, out string dcValue, out string error)
        {
            // Prefer the native PowrProf API. It returns numeric indexes directly and
            // is independent of the Windows display language and console encoding.
            if (LidPowerSettings.TryReadCurrent(out acValue, out dcValue, out string nativeError))
            {
                error = string.Empty;
                return true;
            }

            // Keep powercfg parsing only as a compatibility fallback. Some unusual
            // systems may reject the PowrProf read even though powercfg can query it.
            acValue = string.Empty;
            dcValue = string.Empty;
            var result = runPowerCfg("/query SCHEME_CURRENT SUB_BUTTONS LIDACTION");
            if (!result.Success)
            {
                error = $"PowrProf: {nativeError}; powercfg: {result.DescribeFailure()}";
                return false;
            }

            MatchCollection matches = Regex.Matches(result.Output, @"0x([0-9a-fA-F]+)");
            if (matches.Count < 2)
            {
                error = $"PowrProf: {nativeError}; powercfg fallback could not find AC/DC setting indexes";
                return false;
            }

            try
            {
                int ac = Convert.ToInt32(matches[matches.Count - 2].Groups[1].Value, 16);
                int dc = Convert.ToInt32(matches[matches.Count - 1].Groups[1].Value, 16);
                acValue = ac.ToString();
                dcValue = dc.ToString();
                error = string.Empty;
                AppLog.Write("LidActionManager", $"PowrProf read failed; used powercfg fallback: {nativeError}");
                return true;
            }
            catch (Exception ex)
            {
                error = $"PowrProf: {nativeError}; powercfg fallback parse failed: {ex.Message}";
                return false;
            }
        }

        private void SetLastError(string message)
        {
            LastError = message;
            AppLog.Write("LidActionManager", message);
        }

        public bool IsModified => isModified;

        public bool ForceRestoreToSleep()
        {
            LastError = null;

            if (!ApplyLidActions("1", "1", out string error))
            {
                SetLastError($"Could not force lid action to Sleep: {error}");
                return false;
            }

            ClearSavedOriginals();
            return true;
        }
    }
}
