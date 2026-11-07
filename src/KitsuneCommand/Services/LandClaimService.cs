using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Services
{
    public class LandClaimInfo
    {
        public string OwnerId { get; set; }
        public string OwnerName { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    public class LandClaimSnapshot
    {
        /// <summary>Width of a claim in blocks (the LandClaimSize server setting), centred on the block.</summary>
        public int ClaimSize { get; set; }
        public List<LandClaimInfo> Claims { get; set; } = new List<LandClaimInfo>();
    }

    /// <summary>
    /// Reads every placed land claim block from the game's persistent player
    /// list, which holds offline players' claims as well as online ones.
    /// </summary>
    public class LandClaimService
    {
        public const int DefaultClaimSize = 41;

        public LandClaimSnapshot GetClaims()
        {
            return GameThread.Run("Reading land claims", ReadClaims, new LandClaimSnapshot { ClaimSize = DefaultClaimSize });
        }

        /// <summary>Main thread only.</summary>
        private static LandClaimSnapshot ReadClaims()
        {
            var snapshot = new LandClaimSnapshot { ClaimSize = ReadClaimSize() };
            var players = GameManager.Instance?.GetPersistentPlayerList()?.Players;
            if (players == null) return snapshot;

            foreach (var pair in players)
            {
                var data = pair.Value;
                if (data?.LPBlocks == null || data.LPBlocks.Count == 0) continue;

                var ownerId = pair.Key?.CombinedString;
                var ownerName = SafeName(data) ?? ownerId ?? "?";
                foreach (var pos in data.LPBlocks)
                {
                    snapshot.Claims.Add(new LandClaimInfo
                    {
                        OwnerId = ownerId,
                        OwnerName = ownerName,
                        X = pos.x,
                        Y = pos.y,
                        Z = pos.z,
                    });
                }
            }
            return snapshot;
        }

        private static int ReadClaimSize()
        {
            try
            {
                var size = GamePrefs.GetInt(GameCompat.Pref(nameof(EnumGamePrefs.LandClaimSize)));
                return size > 0 ? size : DefaultClaimSize;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Couldn't read LandClaimSize: {ex.Message}");
                return DefaultClaimSize;
            }
        }

        private static string SafeName(PersistentPlayerData data)
        {
            try
            {
                var name = data.PlayerName?.DisplayName;
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch
            {
                return null;
            }
        }
    }
}
