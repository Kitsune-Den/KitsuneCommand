using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Services;

namespace KitsuneCommand.Tests.Services
{
    [TestFixture]
    public class SevenDaysModsServiceTests
    {
        // Shaped like https://api.7daystodiemods.com/v1/mods responses (2026-10-12),
        // trimmed to the fields KC reads.
        private const string SearchJson = @"{
  ""items"": [
    {
      ""id"": ""01M4QJ1P1HQX19B6MKXS5NWTZX"",
      ""slug"": ""kitsunecommand"",
      ""title"": ""KitsuneCommand"",
      ""summary"": ""A web admin panel for your 7 Days to Die dedicated server."",
      ""thumbnail"": { ""url"": ""https://downloads.7daystodiemods.com/mods/01M4/thumb.webp"" },
      ""categories"": [ { ""slug"": ""application"", ""name"": ""Application"" } ],
      ""server_side"": { ""slug"": ""server-only"", ""name"": ""Server Side Only"" },
      ""game_versions"": [ { ""slug"": ""v3"", ""name"": ""V3 Mods"" }, { ""slug"": ""v2"", ""name"": ""V2 Mods"" } ],
      ""current_version"": ""2.9.0"",
      ""author"": { ""username"": ""adainthelab"", ""display_name"": ""AdaInTheLab"", ""is_verified"": true },
      ""is_adult"": false,
      ""favorite_count"": 3,
      ""download_count"": 41,
      ""updated_at"": ""2026-10-12T08:30:51.700Z""
    },
    {
      ""slug"": ""kitsune-backpack"",
      ""title"": ""Kitsune Backpack"",
      ""current_version"": ""1.4"",
      ""author"": { ""username"": ""someone"" },
      ""categories"": [],
      ""game_versions"": []
    }
  ],
  ""total"": 12, ""page"": 1, ""limit"": 2, ""total_pages"": 6
}";

        private const string MetadataJson = @"{
  ""games"": [ { ""slug"": ""7-days-to-die"", ""name"": ""7 Days to Die"" } ],
  ""categories"": [ { ""slug"": ""overhaul"", ""name"": ""Overhaul"" }, { ""slug"": ""ui"", ""name"": ""UI"" } ],
  ""game_versions"": [ { ""slug"": ""v3"", ""name"": ""V3 Mods"" } ],
  ""server_sides"": [ { ""slug"": ""server-only"", ""name"": ""Server Side Only"" } ]
}";

        private List<string> _requests;

        private SevenDaysModsService ServiceReturning(string body)
        {
            _requests = new List<string>();
            return new SevenDaysModsService(url => { _requests.Add(url); return body; }, warn: _ => { });
        }

        [Test]
        public void ParseSearch_ReadsTheFieldsTheDiscoverTabShows()
        {
            var result = SevenDaysModsService.ParseSearch(SearchJson);

            Assert.That(result.TotalCount, Is.EqualTo(12));
            Assert.That(result.TotalPages, Is.EqualTo(6));
            Assert.That(result.Mods, Has.Count.EqualTo(2));

            var kc = result.Mods[0];
            Assert.That(kc.Slug, Is.EqualTo("kitsunecommand"));
            Assert.That(kc.Title, Is.EqualTo("KitsuneCommand"));
            Assert.That(kc.Version, Is.EqualTo("2.9.0"));
            Assert.That(kc.Author, Is.EqualTo("AdaInTheLab"));
            Assert.That(kc.AuthorVerified, Is.True);
            Assert.That(kc.Likes, Is.EqualTo(3));
            Assert.That(kc.Downloads, Is.EqualTo(41));
            Assert.That(kc.ThumbnailUrl, Does.EndWith("thumb.webp"));
            Assert.That(kc.Categories, Is.EqualTo(new[] { "Application" }));
            Assert.That(kc.GameVersions, Is.EqualTo(new[] { "V3 Mods", "V2 Mods" }));
            Assert.That(kc.ServerSide, Is.EqualTo("server-only"));
            Assert.That(kc.Url, Is.EqualTo("https://7daystodiemods.com/mods/kitsunecommand"));
        }

        [Test]
        public void ParseSearch_TakesTheAuthorUsernameWhenThereIsNoDisplayName()
        {
            var result = SevenDaysModsService.ParseSearch(SearchJson);
            Assert.That(result.Mods[1].Author, Is.EqualTo("someone"));
            Assert.That(result.Mods[1].ThumbnailUrl, Is.Null);
        }

        [Test]
        public void ParseSearch_ReadsTheVersionFromTheDetailShapeToo()
        {
            // GET /v1/mods/{slug} nests current_version as an object.
            var json = @"{ ""items"": [ { ""slug"": ""x"", ""title"": ""X"", ""current_version"": { ""version"": ""3.1"" } } ], ""total"": 1 }";
            Assert.That(SevenDaysModsService.ParseSearch(json).Mods[0].Version, Is.EqualTo("3.1"));
        }

        [Test]
        public void ParseMetadata_ReadsTheFilterOptions()
        {
            var meta = SevenDaysModsService.ParseMetadata(MetadataJson);
            Assert.That(meta.Categories.Select(c => c.Slug), Is.EqualTo(new[] { "overhaul", "ui" }));
            Assert.That(meta.GameVersions.Single().Name, Is.EqualTo("V3 Mods"));
            Assert.That(meta.ServerSides.Single().Slug, Is.EqualTo("server-only"));
        }

        [Test]
        public void Search_BuildsTheQueryTheApiExpects()
        {
            var service = ServiceReturning(SearchJson);
            service.Search("zombie & co", "downloads", 2, 20, "overhaul", "v3", "server-only");

            var url = _requests.Single();
            Assert.That(url, Does.StartWith("https://api.7daystodiemods.com/v1/mods?"));
            Assert.That(url, Does.Contain("page=2"));
            Assert.That(url, Does.Contain("limit=20"));
            Assert.That(url, Does.Contain("q=zombie%20%26%20co"));
            Assert.That(url, Does.Contain("sort=downloads"));
            Assert.That(url, Does.Contain("category=overhaul"));
            Assert.That(url, Does.Contain("game_version=v3"));
            Assert.That(url, Does.Contain("server_side=server-only"));
        }

        [Test]
        public void Search_DropsSortValuesTheApiWouldReject()
        {
            // The API answers 400 to anything outside newest|updated|likes|downloads|views.
            var service = ServiceReturning(SearchJson);
            service.Search("", "trending", 1, 20);
            Assert.That(_requests.Single(), Does.Not.Contain("sort="));
        }

        [Test]
        public void Search_ClampsPageAndLimit()
        {
            var service = ServiceReturning(SearchJson);
            service.Search("", "newest", 0, 500);
            Assert.That(_requests.Single(), Does.Contain("page=1").And.Contain("limit=50"));
        }

        [Test]
        public void Search_CachesIdenticalQueries()
        {
            var service = ServiceReturning(SearchJson);
            service.Search("a", "newest", 1, 20);
            service.Search("a", "newest", 1, 20);
            service.Search("b", "newest", 1, 20);
            Assert.That(_requests, Has.Count.EqualTo(2));

            service.ClearCache();
            service.Search("a", "newest", 1, 20);
            Assert.That(_requests, Has.Count.EqualTo(3));
        }

        [Test]
        public void Search_ReturnsAnEmptyPageWhenTheSiteIsDown()
        {
            var warnings = new List<string>();
            var service = new SevenDaysModsService(url => throw new System.Net.WebException("offline"), warnings.Add);
            var result = service.Search("a", "newest", 3, 20);
            Assert.That(result.Mods, Is.Empty);
            Assert.That(result.Page, Is.EqualTo(3));
            Assert.That(warnings.Single(), Does.Contain("offline"));
        }

        [Test]
        public void FindExact_MatchesByTitleIgnoringCase()
        {
            var service = ServiceReturning(SearchJson);
            Assert.That(service.FindExact("kitsune backpack")?.Slug, Is.EqualTo("kitsune-backpack"));
            Assert.That(_requests.Single(), Does.Contain("title_only=true"));
        }

        [Test]
        public void FindExact_MatchesAFolderNameToTheSlug()
        {
            var service = ServiceReturning(SearchJson);
            Assert.That(service.FindExact("Kitsune_Backpack")?.Slug, Is.EqualTo("kitsune-backpack"));
        }

        [Test]
        public void FindExact_DoesNotFuzzyMatch()
        {
            var service = ServiceReturning(SearchJson);
            Assert.That(service.FindExact("Kitsune"), Is.Null);
            Assert.That(service.FindExact("KitsuneCommand2"), Is.Null);
        }

        [TestCase("KitsuneCommand", "kitsunecommand")]
        [TestCase("Kitsune Backpack", "kitsune-backpack")]
        [TestCase("  War3zuk AIO -- Overhaul! ", "war3zuk-aio-overhaul")]
        [TestCase("(KH) Turbo-Schmiede", "kh-turbo-schmiede")]
        public void Slugify(string name, string slug)
        {
            Assert.That(SevenDaysModsService.Slugify(name), Is.EqualTo(slug));
        }
    }

    [TestFixture]
    public class ModUpdatePickBestTests
    {
        private static ModUpdateCheckResult Row(string source, string status, string latest) =>
            new ModUpdateCheckResult { Source = source, Status = status, LatestVersion = latest };

        [Test]
        public void AnAvailableUpdateBeatsUpToDate()
        {
            var best = ModUpdateService.PickBest(new[]
            {
                Row("nexus", "up_to_date", "1.2"),
                Row("7daystodiemods", "update_available", "1.3"),
            });
            Assert.That(best.Source, Is.EqualTo("7daystodiemods"));
        }

        [Test]
        public void TheNewerUpdateWinsWhenBothSitesHaveOne()
        {
            var best = ModUpdateService.PickBest(new[]
            {
                Row("nexus", "update_available", "2.1.0"),
                Row("7daystodiemods", "update_available", "2.0.5"),
            });
            Assert.That(best.Source, Is.EqualTo("nexus"));
        }

        [Test]
        public void UpToDateBeatsAnUnparseableVersion()
        {
            var best = ModUpdateService.PickBest(new[]
            {
                Row("nexus", "version_differs", "beta-7"),
                Row("7daystodiemods", "up_to_date", "1.0"),
            });
            Assert.That(best.Status, Is.EqualTo("up_to_date"));
        }

        [Test]
        public void NoCandidatesMeansNoMatch()
        {
            Assert.That(ModUpdateService.PickBest(new ModUpdateCheckResult[0]), Is.Null);
        }

        [TestCase("1.0", "1.1", "update_available")]
        [TestCase("v2.9.0", "2.9.0", "up_to_date")]
        [TestCase("1.2", "1.1", "up_to_date")]
        [TestCase("1.0", "beta", "version_differs")]
        public void ClassifyVersion(string installed, string latest, string status)
        {
            Assert.That(ModUpdateService.ClassifyVersion(installed, latest), Is.EqualTo(status));
        }
    }
}
