using System;
using System.Collections.Generic;

namespace KitsunePvPBalance.Commands
{
    /// <summary>
    /// Console command for managing PvP balance settings at runtime.
    /// Usage:
    ///   kpvp               — show current settings
    ///   kpvp set multiplier 0.3  — change damage multiplier
    ///   kpvp set headshot 1.5    — change headshot multiplier
    ///   kpvp set enabled true    — enable/disable
    ///   kpvp set log true        — toggle hit logging
    ///   kpvp reload              — reload from config file
    /// </summary>
    public class KpvpCommand : ConsoleCmdAbstract
    {
        public override string[] getCommands()
        {
            return new[] { "kpvp" };
        }

        public override string getDescription()
        {
            return "Manage PvP damage balance settings. Usage: kpvp [set <key> <value> | reload]";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            if (_params.Count == 0)
            {
                ShowStatus();
                return;
            }

            var subCmd = _params[0].ToLowerInvariant();

            switch (subCmd)
            {
                case "reload":
                    ModEntry.LoadConfig();
                    SdtdConsole.Instance.Output("[KitsunePvPBalance] Config reloaded from file.");
                    ShowStatus();
                    break;

                case "set":
                    if (_params.Count < 3)
                    {
                        SdtdConsole.Instance.Output("Usage: kpvp set <multiplier|headshot|enabled|log> <value>");
                        return;
                    }
                    HandleSet(_params[1].ToLowerInvariant(), _params[2]);
                    break;

                default:
                    SdtdConsole.Instance.Output("Unknown subcommand. Use: kpvp, kpvp set <key> <value>, kpvp reload");
                    break;
            }
        }

        private void HandleSet(string key, string value)
        {
            switch (key)
            {
                case "multiplier":
                    if (float.TryParse(value, out var mult))
                    {
                        PvPDamageConfig.DamageMultiplier = Math.Max(0f, Math.Min(10f, mult));
                        SdtdConsole.Instance.Output($"PvP damage multiplier set to {PvPDamageConfig.DamageMultiplier}");
                    }
                    else
                        SdtdConsole.Instance.Output("Invalid number.");
                    break;

                case "headshot":
                    if (float.TryParse(value, out var hs))
                    {
                        PvPDamageConfig.HeadshotMultiplier = Math.Max(0f, Math.Min(10f, hs));
                        SdtdConsole.Instance.Output($"Headshot multiplier set to {PvPDamageConfig.HeadshotMultiplier}");
                    }
                    else
                        SdtdConsole.Instance.Output("Invalid number.");
                    break;

                case "enabled":
                    if (bool.TryParse(value, out var en))
                    {
                        PvPDamageConfig.Enabled = en;
                        SdtdConsole.Instance.Output($"PvP balance {(en ? "enabled" : "disabled")}");
                    }
                    else
                        SdtdConsole.Instance.Output("Use 'true' or 'false'.");
                    break;

                case "log":
                    if (bool.TryParse(value, out var log))
                    {
                        PvPDamageConfig.LogPvPHits = log;
                        SdtdConsole.Instance.Output($"PvP hit logging {(log ? "enabled" : "disabled")}");
                    }
                    else
                        SdtdConsole.Instance.Output("Use 'true' or 'false'.");
                    break;

                default:
                    SdtdConsole.Instance.Output("Unknown setting. Use: multiplier, headshot, enabled, log");
                    break;
            }
        }

        private void ShowStatus()
        {
            SdtdConsole.Instance.Output("=== KitsunePvPBalance Settings ===");
            SdtdConsole.Instance.Output($"  Enabled:     {PvPDamageConfig.Enabled}");
            SdtdConsole.Instance.Output($"  Multiplier:  {PvPDamageConfig.DamageMultiplier} (0=disabled, 0.5=half, 1=normal, 2=double)");
            SdtdConsole.Instance.Output($"  Headshot:    {PvPDamageConfig.HeadshotMultiplier}");
            SdtdConsole.Instance.Output($"  Log Hits:    {PvPDamageConfig.LogPvPHits}");
            SdtdConsole.Instance.Output($"  Patched:     {PvPDamageConfig.IsPatched}");
        }
    }
}
