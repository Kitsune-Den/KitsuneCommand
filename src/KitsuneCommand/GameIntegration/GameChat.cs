using KitsuneCommand.Core;

namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Private messages from the server to one player.
    ///
    /// KC used to send these through the <c>pm</c> console command. That command isn't
    /// part of the game: it comes from TFP_CommandExtensions, a mod the dedicated server
    /// ships in its <c>Mods/</c> folder. On a server without it (an overhaul install that
    /// brings its own Mods folder, or one someone cleaned out) <c>pm</c> is an unknown
    /// command, and every chat-command reply, ticket answer and delivery notice went
    /// nowhere without an error. This sends the same chat package <c>pm</c> does, through
    /// game API that is unchanged from V2.5 through V3.4.
    /// </summary>
    public static class GameChat
    {
        /// <summary>Whisper to the player with this entity id. False when they aren't connected.</summary>
        public static bool Whisper(int entityId, string message)
            => OnMainThread(() => Send(ClientByEntityId(entityId), message));

        /// <summary>Whisper to a player by name, entity id or platform id, as <c>pm</c> accepted.</summary>
        public static bool Whisper(string nameOrId, string message)
            => OnMainThread(() => Send(ConsoleHelper.ParseParamIdOrName(nameOrId), message));

        private static ClientInfo ClientByEntityId(int entityId)
            => SingletonMonoBehaviour<ConnectionManager>.Instance?.Clients?.ForEntityId(entityId);

        private static bool Send(ClientInfo client, string message)
        {
            if (client == null || string.IsNullOrEmpty(message)) return false;

            client.SendPackage(NetPackageManager.GetPackage<NetPackageChat>().Setup(
                EChatType.Whisper, -1, message, null, EMessageSender.Server,
                GeneratedTextManager.BbCodeSupportMode.Supported));
            return true;
        }

        // Network sends belong on the game thread. Chat handlers are already there;
        // web controllers and delayed deliveries are not.
        private static bool OnMainThread(Func<bool> send)
        {
            var main = ModEntry.MainThreadContext;
            if (main == null || SynchronizationContext.Current == main)
                return send();

            // Not disposed: after a timeout the posted callback still runs and sets it.
            var sent = false;
            var done = new ManualResetEventSlim(false);
            main.Post(_ =>
            {
                try { sent = send(); }
                catch (Exception ex) { Log.Warning($"[KitsuneCommand] Whisper failed: {ex.Message}"); }
                finally { done.Set(); }
            }, null);

            if (!done.Wait(TimeSpan.FromSeconds(5)))
            {
                Log.Warning("[KitsuneCommand] Whisper timed out waiting for the main thread.");
                return false;
            }
            return sent;
        }
    }
}
