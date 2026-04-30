using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KitsuneCommand.Features.VoteRewards.Providers
{
    /// <summary>
    /// Adapter for 7daystodie-servers.com.
    ///
    /// API surface (all GETs unless noted, plain HTTPS, identified by API key):
    ///
    ///   GET  /api/?object=votes&amp;element=claim&amp;key=KEY&amp;steamid=ID
    ///       → "0" not voted, "1" voted unclaimed, "2" voted claimed (raw text)
    ///
    ///   POST /api/?action=post&amp;object=votes&amp;element=claim&amp;key=KEY&amp;steamid=ID
    ///       → "1" on success
    ///
    ///   GET  /api/?object=servers&amp;element=voters&amp;key=KEY&amp;format=json
    ///       → JSON: { "voters": [ { "steamid": "...", "nickname": "...", "date": "...", "claimed": "0|1" }, ... ] }
    ///
    /// The API is stable and undocumented in any official spec — these shapes are
    /// derived from community examples (CSMM, Servertools) and direct probes. If
    /// the site changes them, this adapter is the only thing that needs updating.
    /// </summary>
    public class SevenDtdServersProvider : IVoteSiteProvider
    {
        public string Key => "7daystodie-servers";
        public string DisplayName => "7daystodie-servers.com";

        private const string BaseUrl = "https://7daystodie-servers.com/api/";

        private readonly HttpClient _http;

        public SevenDtdServersProvider()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("KitsuneCommand/1.0 (+https://kitsuneden.net)");
        }

        public async Task<IReadOnlyList<VoterInfo>> ListRecentVotersAsync(VoteProviderSettings cfg)
        {
            var url = $"{BaseUrl}?object=servers&element=voters&key={Uri.EscapeDataString(cfg.ApiKey)}&format=json";
            var raw = await GetStringAsync(url).ConfigureAwait(false);
            return ParseVotersJson(raw);
        }

        public async Task<VoteClaimStatus> GetClaimStatusAsync(VoteProviderSettings cfg, string steamId)
        {
            var url = $"{BaseUrl}?object=votes&element=claim&key={Uri.EscapeDataString(cfg.ApiKey)}&steamid={Uri.EscapeDataString(steamId)}";
            var raw = (await GetStringAsync(url).ConfigureAwait(false)).Trim();

            // The body is just "0", "1", or "2". Anything else means the API
            // returned an error envelope or a transient failure — treat as Unvoted
            // and let the next sweep retry rather than crash the loop.
            return raw switch
            {
                "0" => VoteClaimStatus.Unvoted,
                "1" => VoteClaimStatus.VotedUnclaimed,
                "2" => VoteClaimStatus.VotedClaimed,
                _ => VoteClaimStatus.Unvoted
            };
        }

        public async Task<bool> MarkClaimedAsync(VoteProviderSettings cfg, string steamId)
        {
            var url = $"{BaseUrl}?action=post&object=votes&element=claim&key={Uri.EscapeDataString(cfg.ApiKey)}&steamid={Uri.EscapeDataString(steamId)}";

            // The "POST" verb here is the API's convention; the body is empty —
            // all data rides in the query string. We honor the verb so the site
            // knows we mean "mark claimed" and not "check status."
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;

            var body = (await resp.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            return body == "1";
        }

        // ─── internals ───────────────────────────────────────────────────

        private async Task<string> GetStringAsync(string url)
        {
            using var resp = await _http.GetAsync(url).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// The voters payload has been served in two shapes over the years:
        /// a top-level array `[ {...}, {...} ]` and an envelope `{ "voters": [...] }`.
        /// Try both and prefer whichever actually parses; missing fields default
        /// to safe values rather than throwing.
        /// </summary>
        private static List<VoterInfo> ParseVotersJson(string raw)
        {
            var result = new List<VoterInfo>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            JToken root;
            try
            {
                root = JToken.Parse(raw);
            }
            catch (JsonException)
            {
                return result;
            }

            JArray arr = null;
            if (root.Type == JTokenType.Array)
            {
                arr = (JArray)root;
            }
            else if (root.Type == JTokenType.Object)
            {
                if (root["voters"] is JArray inner) arr = inner;
            }

            if (arr == null) return result;

            foreach (var item in arr)
            {
                var steam = (string)(item["steamid"] ?? item["steamId"] ?? item["SteamId"]);
                if (string.IsNullOrWhiteSpace(steam)) continue;

                result.Add(new VoterInfo
                {
                    SteamId = steam.Trim(),
                    Nickname = (string)(item["nickname"] ?? item["name"]),
                    VoteDate = NormalizeDate((string)(item["date"] ?? item["voted_at"]))
                });
            }

            return result;
        }

        /// <summary>
        /// Normalizes whatever date string the site returns to YYYY-MM-DD UTC.
        /// Falls back to today's UTC date if the input is missing or unparseable —
        /// sweeps are day-granularity-idempotent so a missing date just means
        /// "treat as today's vote."
        /// </summary>
        private static string NormalizeDate(string input)
        {
            if (!string.IsNullOrWhiteSpace(input)
                && DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
