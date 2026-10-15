using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Mod discovery against 7daystodiemods.com, alongside Nexus.
    ///
    /// The site's public API (https://api.7daystodiemods.com/v1, no key) backs
    /// the Discover tab's second source and the "Check for updates" sweep. It is
    /// read-only and links out: downloads go through the site's own download
    /// page, never through its API, so the site keeps its download flow. Its
    /// robots.txt allows search use (listings, links, short excerpts), which is
    /// exactly what this is.
    ///
    ///   GET /v1/mods?q=&amp;title_only=&amp;category=&amp;game_version=&amp;server_side=&amp;sort=&amp;page=&amp;limit=
    ///       sort: newest | updated | likes | downloads | views
    ///   GET /v1/metadata  → categories, game_versions, server_sides (slug + name)
    /// </summary>
    public class SevenDaysModsService
    {
        public const string SiteUrl = "https://7daystodiemods.com";
        private const string ApiUrl = "https://api.7daystodiemods.com";
        private const int CacheMinutes = 30;
        private const int MetadataCacheMinutes = 24 * 60;

        public static readonly string[] SortValues = { "newest", "updated", "likes", "downloads", "views" };

        private readonly Func<string, string> _get;
        private readonly Action<string> _warn;
        private readonly ConcurrentDictionary<string, CachedResponse> _cache = new ConcurrentDictionary<string, CachedResponse>();

        public SevenDaysModsService() : this(null) { }

        /// <summary>
        /// Takes the HTTP GET (url → body) and the warning sink so tests can feed
        /// recorded responses without the game's logger, which can't load outside it.
        /// </summary>
        public SevenDaysModsService(Func<string, string> httpGet, Action<string> warn = null)
        {
            _get = httpGet ?? HttpGet;
            _warn = warn ?? GameLogWarning;
        }

        private static void GameLogWarning(string message) => Log.Warning(message);

        /// <summary>One page of mods. Unknown sort values fall back to the site's default (newest).</summary>
        public SevenDaysModsSearchResult Search(string q, string sort, int page, int limit,
            string category = null, string gameVersion = null, string serverSide = null)
        {
            page = Math.Max(1, page);
            limit = Math.Max(1, Math.Min(50, limit));
            var query = new List<string> { "page=" + page, "limit=" + limit };
            if (!string.IsNullOrWhiteSpace(q)) query.Add("q=" + Uri.EscapeDataString(q.Trim()));
            if (SortValues.Contains(sort)) query.Add("sort=" + sort);
            if (!string.IsNullOrWhiteSpace(category)) query.Add("category=" + Uri.EscapeDataString(category));
            if (!string.IsNullOrWhiteSpace(gameVersion)) query.Add("game_version=" + Uri.EscapeDataString(gameVersion));
            if (!string.IsNullOrWhiteSpace(serverSide)) query.Add("server_side=" + Uri.EscapeDataString(serverSide));

            var url = ApiUrl + "/v1/mods?" + string.Join("&", query);
            if (TryGetCached(url, CacheMinutes, out SevenDaysModsSearchResult cached))
                return cached;

            try
            {
                var result = ParseSearch(_get(url));
                SetCache(url, result);
                return result;
            }
            catch (Exception ex)
            {
                _warn($"[KitsuneCommand] 7daystodiemods.com search error: {ex.Message}");
                return new SevenDaysModsSearchResult { Page = page };
            }
        }

        /// <summary>Categories, game versions and server-side values for the filters.</summary>
        public SevenDaysModsMetadata GetMetadata()
        {
            var url = ApiUrl + "/v1/metadata";
            if (TryGetCached(url, MetadataCacheMinutes, out SevenDaysModsMetadata cached))
                return cached;

            try
            {
                var result = ParseMetadata(_get(url));
                SetCache(url, result);
                return result;
            }
            catch (Exception ex)
            {
                _warn($"[KitsuneCommand] 7daystodiemods.com metadata error: {ex.Message}");
                return new SevenDaysModsMetadata();
            }
        }

        /// <summary>
        /// The listing for an installed mod, matched exactly: same title (case-
        /// insensitive), or the listing's slug is the name slugified
        /// ("KitsuneCommand" → "kitsunecommand"). Null when nothing matches — a
        /// near miss would send the admin to the wrong mod's update.
        /// </summary>
        public SevenDaysModInfo FindExact(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var url = ApiUrl + "/v1/mods?title_only=true&limit=20&q=" + Uri.EscapeDataString(name.Trim());
            SevenDaysModsSearchResult result;
            if (!TryGetCached(url, CacheMinutes, out result))
            {
                try
                {
                    result = ParseSearch(_get(url));
                    SetCache(url, result);
                }
                catch (Exception ex)
                {
                    _warn($"[KitsuneCommand] 7daystodiemods.com lookup error for '{name}': {ex.Message}");
                    return null;
                }
            }

            var slug = Slugify(name);
            return result.Mods.FirstOrDefault(m => string.Equals(m.Title?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? result.Mods.FirstOrDefault(m => slug.Length > 0 && m.Slug == slug);
        }

        public void ClearCache() => _cache.Clear();

        public static string ModUrl(string slug) => SiteUrl + "/mods/" + slug;

        /// <summary>Lowercase, runs of anything that isn't a letter or digit become one hyphen.</summary>
        public static string Slugify(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in (name ?? "").Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        // --- parsing (public for tests) ---

        public static SevenDaysModsSearchResult ParseSearch(string json)
        {
            var root = JObject.Parse(json);
            var result = new SevenDaysModsSearchResult
            {
                TotalCount = root["total"]?.Value<int>() ?? 0,
                Page = root["page"]?.Value<int>() ?? 1,
                TotalPages = root["total_pages"]?.Value<int>() ?? 0,
            };
            foreach (var item in root["items"] as JArray ?? new JArray())
            {
                var slug = item["slug"]?.Value<string>();
                if (string.IsNullOrEmpty(slug)) continue;
                result.Mods.Add(new SevenDaysModInfo
                {
                    Slug = slug,
                    Title = item["title"]?.Value<string>(),
                    Summary = item["summary"]?.Value<string>(),
                    Version = item["current_version"]?.Type == JTokenType.String
                        ? item["current_version"].Value<string>()
                        : item["current_version"]?["version"]?.Value<string>(),
                    Author = item["author"]?["display_name"]?.Value<string>() ?? item["author"]?["username"]?.Value<string>(),
                    AuthorVerified = item["author"]?["is_verified"]?.Value<bool>() ?? false,
                    Likes = item["favorite_count"]?.Value<int>() ?? 0,
                    Downloads = item["download_count"]?.Value<int>() ?? 0,
                    ThumbnailUrl = item["thumbnail"]?["url"]?.Value<string>(),
                    UpdatedAt = item["updated_at"]?.Value<string>(),
                    Categories = Names(item["categories"]),
                    GameVersions = Names(item["game_versions"]),
                    ServerSide = item["server_side"]?["slug"]?.Value<string>(),
                    ServerSideName = item["server_side"]?["name"]?.Value<string>(),
                    IsAdult = item["is_adult"]?.Value<bool>() ?? false,
                    Url = ModUrl(slug),
                });
            }
            return result;
        }

        public static SevenDaysModsMetadata ParseMetadata(string json)
        {
            var root = JObject.Parse(json);
            return new SevenDaysModsMetadata
            {
                Categories = Options(root["categories"]),
                GameVersions = Options(root["game_versions"]),
                ServerSides = Options(root["server_sides"]),
            };
        }

        private static List<string> Names(JToken array) =>
            (array as JArray ?? new JArray()).Select(t => t["name"]?.Value<string>()).Where(n => !string.IsNullOrEmpty(n)).ToList();

        private static List<SevenDaysModsOption> Options(JToken array) =>
            (array as JArray ?? new JArray())
                .Select(t => new SevenDaysModsOption { Slug = t["slug"]?.Value<string>(), Name = t["name"]?.Value<string>() })
                .Where(o => !string.IsNullOrEmpty(o.Slug))
                .ToList();

        // --- HTTP + cache ---

        private static string HttpGet(string url)
        {
            // Same TLS setup as the Nexus client: the game's Mono runtime often
            // has no CA store, so chain errors are tolerated — but only for this
            // API's own host, not for every request in the process.
            var previousCallback = ServicePointManager.ServerCertificateValidationCallback;
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
                    errors == SslPolicyErrors.None
                    || (sender is HttpWebRequest req && req.RequestUri.Host == "api.7daystodiemods.com");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var client = new WebClient { Encoding = Encoding.UTF8 })
                {
                    client.Headers[HttpRequestHeader.Accept] = "application/json";
                    client.Headers[HttpRequestHeader.UserAgent] =
                        $"KitsuneCommand/{Core.ModEntry.ModVersion} (+https://github.com/Kitsune-Den/KitsuneCommand)";
                    return client.DownloadString(url);
                }
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = previousCallback;
            }
        }

        private bool TryGetCached<T>(string key, int minutes, out T value)
        {
            if (_cache.TryGetValue(key, out var entry) && (DateTime.UtcNow - entry.Timestamp).TotalMinutes < minutes)
            {
                value = (T)entry.Data;
                return true;
            }
            value = default;
            return false;
        }

        private void SetCache(string key, object data) =>
            _cache[key] = new CachedResponse { Data = data, Timestamp = DateTime.UtcNow };

        private class CachedResponse
        {
            public object Data { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }

    public class SevenDaysModsSearchResult
    {
        public List<SevenDaysModInfo> Mods { get; set; } = new List<SevenDaysModInfo>();
        public int TotalCount { get; set; }
        public int Page { get; set; } = 1;
        public int TotalPages { get; set; }
    }

    public class SevenDaysModInfo
    {
        public string Slug { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public bool AuthorVerified { get; set; }
        /// <summary>The site's favorite count, shown as "likes" (its sort calls it that).</summary>
        public int Likes { get; set; }
        public int Downloads { get; set; }
        public string ThumbnailUrl { get; set; }
        public string UpdatedAt { get; set; }
        public List<string> Categories { get; set; } = new List<string>();
        public List<string> GameVersions { get; set; } = new List<string>();
        /// <summary>"server-only" | "client-only" | "server-and-client".</summary>
        public string ServerSide { get; set; }
        public string ServerSideName { get; set; }
        public bool IsAdult { get; set; }
        public string Url { get; set; }
    }

    public class SevenDaysModsMetadata
    {
        public List<SevenDaysModsOption> Categories { get; set; } = new List<SevenDaysModsOption>();
        public List<SevenDaysModsOption> GameVersions { get; set; } = new List<SevenDaysModsOption>();
        public List<SevenDaysModsOption> ServerSides { get; set; } = new List<SevenDaysModsOption>();
    }

    public class SevenDaysModsOption
    {
        public string Slug { get; set; }
        public string Name { get; set; }
    }
}
