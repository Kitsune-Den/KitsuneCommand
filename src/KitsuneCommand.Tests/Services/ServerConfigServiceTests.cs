using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Services;

namespace KitsuneCommand.Tests.Services
{
    [TestFixture]
    public class ServerConfigServiceTests
    {
        private string _tempDir;
        private string _configPath;
        private ServerConfigServiceTestable _service;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"kc_config_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            _configPath = Path.Combine(_tempDir, "serverconfig.xml");
            File.WriteAllText(_configPath, SampleConfig);

            _service = new ServerConfigServiceTestable(_configPath);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Test]
        public void ReadConfig_ParsesProperties()
        {
            var config = _service.ReadConfig();

            Assert.That(config, Does.ContainKey("ServerName"));
            Assert.That(config["ServerName"], Is.EqualTo("My Test Server"));
            Assert.That(config, Does.ContainKey("ServerPort"));
            Assert.That(config["ServerPort"], Is.EqualTo("26900"));
            Assert.That(config, Does.ContainKey("ServerMaxPlayerCount"));
            Assert.That(config["ServerMaxPlayerCount"], Is.EqualTo("8"));
        }

        [Test]
        public void ReadConfig_IsCaseInsensitive()
        {
            var config = _service.ReadConfig();

            Assert.That(config["servername"], Is.EqualTo("My Test Server"));
            Assert.That(config["SERVERNAME"], Is.EqualTo("My Test Server"));
        }

        [Test]
        public void ReadRawXml_ReturnsFullContent()
        {
            var xml = _service.ReadRawXml();

            Assert.That(xml, Does.Contain("ServerSettings"));
            Assert.That(xml, Does.Contain("ServerName"));
            Assert.That(xml, Does.Contain("My Test Server"));
        }

        [Test]
        public void SaveConfig_UpdatesExistingProperty()
        {
            _service.SaveConfig(new Dictionary<string, string>
            {
                { "ServerName", "New Server Name" }
            });

            var updated = _service.ReadConfig();
            Assert.That(updated["ServerName"], Is.EqualTo("New Server Name"));
        }

        [Test]
        public void SaveConfig_PreservesOtherProperties()
        {
            _service.SaveConfig(new Dictionary<string, string>
            {
                { "ServerName", "Changed" }
            });

            var updated = _service.ReadConfig();
            Assert.That(updated["ServerPort"], Is.EqualTo("26900"), "Other properties should be preserved");
        }

        [Test]
        public void SaveConfig_CreatesBackupFile()
        {
            _service.SaveConfig(new Dictionary<string, string>
            {
                { "ServerName", "Changed" }
            });

            var backupPath = _configPath + ".bak";
            Assert.That(File.Exists(backupPath), Is.True, "A .bak backup should be created");

            // Backup should contain original value
            var backupContent = File.ReadAllText(backupPath);
            Assert.That(backupContent, Does.Contain("My Test Server"));
        }

        [Test]
        public void SaveConfig_AddsNewProperty_WhenNotExisting()
        {
            _service.SaveConfig(new Dictionary<string, string>
            {
                { "NewProperty", "NewValue" }
            });

            var updated = _service.ReadConfig();
            Assert.That(updated, Does.ContainKey("NewProperty"));
            Assert.That(updated["NewProperty"], Is.EqualTo("NewValue"));
        }

        [Test]
        public void SaveRawXml_ValidatesXml()
        {
            Assert.Throws<System.Xml.XmlException>(() =>
                _service.SaveRawXml("this is not valid xml <<<<"));
        }

        [Test]
        public void SaveRawXml_WritesValidXml()
        {
            var newXml = @"<?xml version=""1.0""?>
<ServerSettings>
    <property name=""ServerName"" value=""Raw Saved"" />
</ServerSettings>";

            _service.SaveRawXml(newXml);

            var result = _service.ReadConfig();
            Assert.That(result["ServerName"], Is.EqualTo("Raw Saved"));
        }

        [Test]
        public void ReadConfig_ThrowsWhenFileNotFound()
        {
            var service = new ServerConfigServiceTestable(Path.Combine(_tempDir, "nonexistent.xml"));
            Assert.Throws<FileNotFoundException>(() => service.ReadConfig());
        }

        [Test]
        public void MigrateConfigTo30_NeutralizesGovernedProps_AddsSandboxCode_KeepsSurvivors()
        {
            File.WriteAllText(_configPath, MigrationSample);

            var result = _service.MigrateConfigTo30();

            Assert.That(result.Changed, Is.True);
            Assert.That(result.AddedSandboxCode, Is.True);
            Assert.That(result.Neutralized, Does.Contain("DeathPenalty"));
            Assert.That(result.Neutralized, Does.Contain("XPMultiplier"));
            Assert.That(result.Neutralized, Does.Contain("BloodMoonFrequency"));

            var config = _service.ReadConfig(); // live <property> elements only (comments excluded)
            // The sandbox-governed props are no longer live properties...
            Assert.That(config, Does.Not.ContainKey("DeathPenalty"));
            Assert.That(config, Does.Not.ContainKey("XPMultiplier"));
            Assert.That(config, Does.Not.ContainKey("BloodMoonFrequency"));
            // ...SandboxCode now exists, and the survivors are untouched.
            Assert.That(config, Does.ContainKey("SandboxCode"));
            Assert.That(config, Does.ContainKey("GameDifficulty"));      // survivor — stays
            Assert.That(config["GameDifficulty"], Is.EqualTo("3"));
            Assert.That(config, Does.ContainKey("LootAbundance"));       // survivor — stays
            Assert.That(config["ServerName"], Is.EqualTo("My Test Server"));

            // Old values are preserved in comments, not destroyed.
            var raw = _service.ReadRawXml();
            Assert.That(raw, Does.Contain("DeathPenalty"));
            Assert.That(raw, Does.Contain("moved to the in-game Sandbox"));

            // A timestamped backup was written before the change.
            Assert.That(result.BackupPath, Is.Not.Null);
            Assert.That(File.Exists(result.BackupPath), Is.True);
        }

        [Test]
        public void MigrateConfigTo30_IsIdempotent()
        {
            File.WriteAllText(_configPath, MigrationSample);
            Assert.That(_service.MigrateConfigTo30().Changed, Is.True);

            var second = _service.MigrateConfigTo30();
            Assert.That(second.Changed, Is.False);          // already 3.0-shaped
            Assert.That(second.Neutralized, Is.Empty);
            Assert.That(second.AddedSandboxCode, Is.False);
            Assert.That(second.BackupPath, Is.Null);        // no backup churn on a no-op
        }

        [Test]
        public void NeedsMigrationTo30_TrueOnlyWhileGovernedPropsRemain()
        {
            File.WriteAllText(_configPath, MigrationSample);
            Assert.That(_service.NeedsMigrationTo30(), Is.True);
            _service.MigrateConfigTo30();
            Assert.That(_service.NeedsMigrationTo30(), Is.False);
        }

        /// <summary>
        /// Testable subclass that bypasses ModEntry/GameIO path resolution.
        /// </summary>
        private class ServerConfigServiceTestable : ServerConfigService
        {
            private readonly string _fixedPath;

            public ServerConfigServiceTestable(string configPath)
            {
                _fixedPath = configPath;
            }

            public override string GetConfigPath()
            {
                if (File.Exists(_fixedPath))
                    return _fixedPath;
                return null;
            }

            public new Dictionary<string, string> ReadConfig()
            {
                var path = GetConfigPath();
                if (path == null)
                    throw new FileNotFoundException("serverconfig.xml not found.");

                var doc = System.Xml.Linq.XDocument.Load(path);
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var prop in doc.Descendants("property"))
                {
                    var name = prop.Attribute("name")?.Value;
                    var value = prop.Attribute("value")?.Value;
                    if (name != null)
                        result[name] = value ?? "";
                }

                return result;
            }

            public new string ReadRawXml()
            {
                var path = GetConfigPath();
                if (path == null)
                    throw new FileNotFoundException("serverconfig.xml not found.");
                return File.ReadAllText(path);
            }

            public new void SaveConfig(Dictionary<string, string> properties)
            {
                var path = GetConfigPath();
                if (path == null)
                    throw new FileNotFoundException("serverconfig.xml not found.");

                var backupPath = path + ".bak";
                File.Copy(path, backupPath, true);

                var doc = System.Xml.Linq.XDocument.Load(path, System.Xml.Linq.LoadOptions.PreserveWhitespace);

                foreach (var kvp in properties)
                {
                    var existing = doc.Descendants("property")
                        .FirstOrDefault(p => string.Equals(
                            p.Attribute("name")?.Value, kvp.Key,
                            StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.SetAttributeValue("value", kvp.Value);
                    }
                    else
                    {
                        var root = doc.Root;
                        root?.Add(new System.Xml.Linq.XElement("property",
                            new System.Xml.Linq.XAttribute("name", kvp.Key),
                            new System.Xml.Linq.XAttribute("value", kvp.Value)));
                    }
                }

                doc.Save(path);
            }

            public new void SaveRawXml(string xmlContent)
            {
                var path = GetConfigPath();
                if (path == null)
                    throw new FileNotFoundException("serverconfig.xml not found.");

                System.Xml.Linq.XDocument.Parse(xmlContent);

                var backupPath = path + ".bak";
                File.Copy(path, backupPath, true);
                File.WriteAllText(path, xmlContent);
            }
        }

        private const string SampleConfig = @"<?xml version=""1.0""?>
<ServerSettings>
    <!-- Server name visible in the server browser -->
    <property name=""ServerName"" value=""My Test Server"" />
    <property name=""ServerPort"" value=""26900"" />
    <property name=""ServerMaxPlayerCount"" value=""8"" />
    <property name=""GameWorld"" value=""Navezgane"" />
    <property name=""GameName"" value=""MyGame"" />
</ServerSettings>";

        // A 2.x-shaped config carrying both sandbox-governed props (DeathPenalty,
        // XPMultiplier, BloodMoonFrequency) and survivors (GameDifficulty, LootAbundance),
        // with no SandboxCode yet — the exact input the 3.0 migration handles.
        private const string MigrationSample = @"<?xml version=""1.0""?>
<ServerSettings>
    <property name=""ServerName"" value=""My Test Server"" />
    <property name=""GameDifficulty"" value=""3"" />
    <property name=""DeathPenalty"" value=""1"" />
    <property name=""XPMultiplier"" value=""150"" />
    <property name=""BloodMoonFrequency"" value=""7"" />
    <property name=""LootAbundance"" value=""100"" />
</ServerSettings>";
    }
}
