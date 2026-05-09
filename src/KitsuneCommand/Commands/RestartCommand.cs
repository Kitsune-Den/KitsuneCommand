using System.Collections.Generic;
using System.Linq;
using Autofac;
using KitsuneCommand.Core;
using KitsuneCommand.Features;

namespace KitsuneCommand.Commands
{
    /// <summary>
    /// Console command for triggering a graceful restart on demand.
    ///   krestart            — restart with the configured default lead time
    ///   krestart 5          — restart in 5 minutes (custom lead)
    ///   krestart 0          — restart now (one final say + shutdown)
    /// Auto-restart on the daily schedule keeps running unless the feature
    /// itself is disabled in settings; this command is for one-offs.
    /// </summary>
    public class RestartCommand : ConsoleCmdAbstract
    {
        // Admin-only. Same default as other destructive admin commands.
        public override int DefaultPermissionLevel => 0;

        public override string[] getCommands()
        {
            return new[] { "krestart" };
        }

        public override string getDescription()
        {
            return "Trigger a graceful 7D2D restart with in-game warnings (admin). Usage: krestart [minutes]";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            // Resolve the feature out of the container. ConsoleCmdAbstract
            // instances aren't built by Autofac and can't be constructor-
            // injected, so we go through the ModLifecycle's exposed container
            // (same pattern as ResetPasswordCommand).
            var container = ModLifecycle.Container;
            if (container == null)
            {
                SdtdConsole.Instance.Output("KitsuneCommand isn't fully initialized yet. Wait for server boot to complete, then retry.");
                return;
            }

            var feature = container.Resolve<FeatureManager>()
                .GetAllFeatures()
                .OfType<GracefulRestartFeature>()
                .FirstOrDefault();

            if (feature == null)
            {
                SdtdConsole.Instance.Output("[KitsuneCommand] GracefulRestart feature not available.");
                return;
            }

            // Default lead matches the longest configured ladder step, falling
            // back to 10 minutes if no ladder is configured at all.
            int leadMinutes = 10;
            if (feature.Settings?.WarningLadder != null && feature.Settings.WarningLadder.Count > 0)
            {
                leadMinutes = feature.Settings.WarningLadder.Max(w => w.MinutesBefore);
            }

            if (_params.Count >= 1)
            {
                if (!int.TryParse(_params[0], out leadMinutes) || leadMinutes < 0 || leadMinutes > 1440)
                {
                    SdtdConsole.Instance.Output("Usage: krestart [minutes]   (0 = restart now, max 1440)");
                    return;
                }
            }

            var actor = string.IsNullOrEmpty(_senderInfo.RemoteClientInfo?.playerName)
                ? "console"
                : _senderInfo.RemoteClientInfo.playerName;

            var result = feature.TriggerNow(leadMinutes, actor);
            SdtdConsole.Instance.Output($"[KitsuneCommand] {result.Message}");
        }
    }
}
