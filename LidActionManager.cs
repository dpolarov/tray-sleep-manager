using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SleepMngr
{
    public class LidActionManager
    {
        private readonly Func<string, PowerCfgResult> runPowerCfg;
        private string? originalLidActionAC;
        private string? originalLidActionDC;
        private bool isModified;
        private bool originalsSaved;

        public LidActionManager() : this(PowerCfgRunner.Run)
        {
        }

        internal LidActionManager(Func<string, PowerCfgResult> powerCfgRunner)
        {
            runPowerCfg = powerCfgRunner;
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

            isModified = !rollbackOk;
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

            isModified = false;
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

            originalLidActionAC = ac;
            originalLidActionDC = dc;
            originalsSaved = true;
            AppLog.Write("LidActionManager", $"Saved original lid settings: AC={ac}, DC={dc}");
            return true;
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
            acValue = string.Empty;
            dcValue = string.Empty;

            var result = runPowerCfg("/query SCHEME_CURRENT SUB_BUTTONS LIDACTION");
            if (!result.Success)
            {
                error = result.DescribeFailure();
                return false;
            }

            // The labels in powercfg output are localized. Microsoft documents that
            // current setting indexes are emitted as hexadecimal 0x... values. In a
            // setting-specific query the current AC/DC indexes are the final two hex
            // values, so this does not depend on English/Russian/Slovak labels.
            MatchCollection matches = Regex.Matches(result.Output, @"0x([0-9a-fA-F]+)");
            if (matches.Count < 2)
            {
                error = "could not find AC/DC setting indexes in localized powercfg output";
                return false;
            }

            try
            {
                int ac = Convert.ToInt32(matches[matches.Count - 2].Groups[1].Value, 16);
                int dc = Convert.ToInt32(matches[matches.Count - 1].Groups[1].Value, 16);
                acValue = ac.ToString();
                dcValue = dc.ToString();
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = $"could not parse AC/DC setting indexes: {ex.Message}";
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

            isModified = false;
            originalsSaved = false;
            originalLidActionAC = null;
            originalLidActionDC = null;
            return true;
        }
    }
}
