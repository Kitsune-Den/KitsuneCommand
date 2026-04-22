using System.Collections.Generic;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Commands
{
    /// <summary>
    /// Console command for toggling trader zone protection.
    /// Usage:
    ///   ktrader             — show current status
    ///   ktrader on          — enable protection (default)
    ///   ktrader off         — disable protection (allow editing)
    ///   ktrader log on/off  — toggle bypass logging
    /// </summary>
    public class TraderProtectionCommand : ConsoleCmdAbstract
    {
        // Admin-only. 7D2D permission levels: 0 = highest (server owner), 1000 = anyone.
        // Default to 0 so only top-level admins can toggle trader protection.
        // Server owners can lower the requirement via serveradmin.xml's <permission cmd="ktrader" permission_level="N"/>.
        public override int DefaultPermissionLevel => 0;

        public override string[] getCommands()
        {
            return new[] { "ktrader" };
        }

        public override string getDescription()
        {
            return "Toggle trader zone block protection (admin only). Usage: ktrader [on|off|log on|log off]";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            if (_params.Count == 0)
            {
                ShowStatus();
                return;
            }

            var cmd = _params[0].ToLowerInvariant();

            switch (cmd)
            {
                case "on":
                    TraderProtectionConfig.ProtectionEnabled = true;
                    SdtdConsole.Instance.Output("[KitsuneCommand] Trader protection ENABLED — zones are now protected.");
                    break;

                case "off":
                    TraderProtectionConfig.ProtectionEnabled = false;
                    SdtdConsole.Instance.Output("[KitsuneCommand] Trader protection DISABLED — you can now edit blocks in trader zones.");
                    SdtdConsole.Instance.Output("  Remember to run 'ktrader on' when you're done cleaning up!");
                    break;

                case "log":
                    if (_params.Count >= 2)
                    {
                        var logVal = _params[1].ToLowerInvariant();
                        TraderProtectionConfig.LogBypasses = logVal == "on" || logVal == "true";
                        SdtdConsole.Instance.Output($"[KitsuneCommand] Bypass logging {(TraderProtectionConfig.LogBypasses ? "enabled" : "disabled")}");
                    }
                    else
                    {
                        SdtdConsole.Instance.Output("Usage: ktrader log on|off");
                    }
                    break;

                default:
                    SdtdConsole.Instance.Output("Usage: ktrader [on|off|log on|log off]");
                    break;
            }
        }

        private void ShowStatus()
        {
            var status = TraderProtectionConfig.ProtectionEnabled ? "ON (protected)" : "OFF (editable)";
            SdtdConsole.Instance.Output("=== Trader Zone Protection ===");
            SdtdConsole.Instance.Output($"  Feature:    {(TraderProtectionConfig.FeatureEnabled ? "Active" : "Inactive")}");
            SdtdConsole.Instance.Output($"  Protection: {status}");
            SdtdConsole.Instance.Output($"  Logging:    {(TraderProtectionConfig.LogBypasses ? "ON" : "OFF")}");
            SdtdConsole.Instance.Output("");
            SdtdConsole.Instance.Output("  ktrader off  — disable protection to clean up trader areas");
            SdtdConsole.Instance.Output("  ktrader on   — re-enable protection when done");
        }
    }
}
