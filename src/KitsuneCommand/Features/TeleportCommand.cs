using System;
using System.Globalization;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Builds the vanilla "teleportplayer" console command.
    /// </summary>
    public static class TeleportCommand
    {
        /// <summary>
        /// The game parses the coordinates with int.TryParse and silently refuses
        /// the teleport when any of them has a decimal point, so saved positions
        /// ("123.4 61.0 -55.2") must be sent as whole numbers. X/Z go to the
        /// block the position is in; Y rounds up so the player lands on top of
        /// the surface rather than inside it.
        /// </summary>
        public static string Build(int entityId, float x, float y, float z)
        {
            var bx = (int)Math.Floor(x);
            var by = (int)Math.Ceiling(y);
            var bz = (int)Math.Floor(z);
            return string.Format(CultureInfo.InvariantCulture, "teleportplayer {0} {1} {2} {3}", entityId, bx, by, bz);
        }
    }
}
