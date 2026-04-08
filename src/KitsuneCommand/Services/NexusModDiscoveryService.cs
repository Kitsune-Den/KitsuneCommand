using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KitsuneCommand.Services
{
    public class NexusModDiscoveryService
    {
        private const string SettingsKey = "NexusModDiscovery";
        private const string GraphQlUrl = "https://api.nexusmods.com/v2/graphql";
        private const string V1BaseUrl = "https://api.nexusmods.com/v1";
        private const string GameDomain = "7daystodie";

        private readonly ISettingsRepository _settingsRepo;
        private readonly ConcurrentDictionary<string, CachedResponse> _cache = new ConcurrentDictionary<string, CachedResponse>();

        public NexusModDiscoveryService(ISettingsRepository settingsRepo)
        {
            _settingsRepo = settingsRepo;
        }

        public NexusModDiscoverySettings GetSettings()
        {
            var json = _settingsRepo.Get(SettingsKey);
            if (string.IsNullOrEmpty(json))
                return new NexusModDiscoverySettings();
            return JsonConvert.DeserializeObject<NexusModDiscoverySettings>(json) ?? new NexusModDiscoverySettings();
        }

        public void SaveSettings(NexusModDiscoverySettings settings)
        {
            _settingsRepo.Set(SettingsKey, JsonConvert.SerializeObject(settings));
            ClearCache();
        }

        /// <summary>
        /// Search mods via GraphQL v2 API (no API key required).
        /// </summary>
        public NexusSearchResult SearchMods(string searchTerm, string sortBy, int offset, int count)
        {
            var settings = GetSettings();
            var cacheKey = $"search_{searchTerm}_{sortBy}_{offset}_{count}";
            if (TryGetCached<NexusSearchResult>(cacheKey, settings.CacheDurationMinutes, out var cached))
                return cached;

            try
            {
                var nameFilter = "";
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var escaped = searchTerm.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    nameFilter = ", name: [{ value: \\\"" + escaped + "\\\", op: WILDCARD }]";
                }

                var sortField = GetSortField(sortBy);

                var graphql = "{ mods(filter: { gameDomainName: [{ value: \\\"" + GameDomain + "\\\" }]" + nameFilter + " }, sort: [{ " + sortField + ": { direction: DESC } }], offset: " + offset + ", count: " + count + ") { nodes { modId name version author summary endorsements downloads pictureUrl updatedAt } totalCount } }";
                var query = "{\"query\": \"" + graphql + "\"}";

                var json = GraphQlPost(query);
                var response = JObject.Parse(json);
                var modsData = response["data"]?["mods"];

                if (modsData == null)
                {
                    Log.Warning($"[KitsuneCommand] GraphQL response missing mods data: {json.Substring(0, Math.Min(200, json.Length))}");
                    return new NexusSearchResult();
                }

                var nodes = modsData["nodes"];
                var totalCount = modsData["totalCount"]?.Value<int>() ?? 0;

                var mods = new List<NexusModInfo>();
                foreach (var node in nodes)
                {
                    mods.Add(new NexusModInfo
                    {
                        ModId = node["modId"]?.Value<int>() ?? 0,
                        Name = node["name"]?.Value<string>(),
                        Version = node["version"]?.Value<string>(),
                        Author = node["author"]?.Value<string>(),
                        Summary = node["summary"]?.Value<string>(),
                        EndorsementCount = node["endorsements"]?.Value<int>() ?? 0,
                        ModDownloads = node["downloads"]?.Value<int>() ?? 0,
                        PictureUrl = node["pictureUrl"]?.Value<string>(),
                        UpdatedAt = node["updatedAt"]?.Value<string>(),
                    });
                }

                var result = new NexusSearchResult { Mods = mods, TotalCount = totalCount };
                SetCache(cacheKey, result);
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Nexus GraphQL search error: {ex.Message}");
                return new NexusSearchResult();
            }
        }

        /// <summary>
        /// Validate an API key via v1 REST API.
        /// </summary>
        public NexusValidationResult ValidateKey(string apiKey)
        {
            try
            {
                var json = V1Get($"{V1BaseUrl}/users/validate.json", apiKey);
                var result = JsonConvert.DeserializeObject<NexusValidationResult>(json);
                result.IsValid = true;
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Nexus API key validation failed: {ex.Message}");
                return new NexusValidationResult { IsValid = false };
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        private static string GetSortField(string sortBy)
        {
            switch (sortBy?.ToLowerInvariant())
            {
                case "latest": return "createdAt";
                case "updated": return "updatedAt";
                case "downloads": return "downloads";
                case "name": return "name";
                default: return "endorsements"; // "trending" / default
            }
        }

        private string GraphQlPost(string jsonBody)
        {
            var previousCallback = ServicePointManager.ServerCertificateValidationCallback;
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";
                    client.Headers[HttpRequestHeader.Accept] = "application/json";
                    return client.UploadString(GraphQlUrl, jsonBody);
                }
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = previousCallback;
            }
        }

        private string V1Get(string url, string apiKey)
        {
            var previousCallback = ServicePointManager.ServerCertificateValidationCallback;
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    client.Headers["apikey"] = apiKey;
                    client.Headers[HttpRequestHeader.Accept] = "application/json";
                    return client.DownloadString(url);
                }
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = previousCallback;
            }
        }

        private bool TryGetCached<T>(string key, int durationMinutes, out T value)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if ((DateTime.UtcNow - entry.Timestamp).TotalMinutes < durationMinutes)
                {
                    value = (T)entry.Data;
                    return true;
                }
                _cache.TryRemove(key, out _);
            }
            value = default;
            return false;
        }

        private void SetCache(string key, object data)
        {
            _cache[key] = new CachedResponse { Data = data, Timestamp = DateTime.UtcNow };
        }

        private class CachedResponse
        {
            public object Data { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }

    public class NexusSearchResult
    {
        public List<NexusModInfo> Mods { get; set; } = new List<NexusModInfo>();
        public int TotalCount { get; set; }
    }

    public class NexusModInfo
    {
        [JsonProperty("modId")]
        public int ModId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("endorsements")]
        public int EndorsementCount { get; set; }

        [JsonProperty("downloads")]
        public int ModDownloads { get; set; }

        [JsonProperty("pictureUrl")]
        public string PictureUrl { get; set; }

        [JsonProperty("updatedAt")]
        public string UpdatedAt { get; set; }
    }

    public class NexusValidationResult
    {
        [JsonProperty("user_id")]
        public int UserId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("is_premium")]
        public bool IsPremium { get; set; }

        [JsonProperty("is_supporter")]
        public bool IsSupporter { get; set; }

        [JsonProperty("isValid")]
        public bool IsValid { get; set; }
    }
}
