using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Features;
using KitsuneCommand.Features.Suspicion;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class InventoryDetectorTests
    {
        private InventoryDetector _detector;
        private InventoryDetectorSettings _settings;
        private Dictionary<string, ItemMeta> _items;

        [SetUp]
        public void SetUp()
        {
            _detector = new InventoryDetector();
            _settings = new InventoryDetectorSettings();
            _items = new Dictionary<string, ItemMeta>
            {
                ["resourceWood"] = new ItemMeta { CreativeMode = "Player", StackLimit = 6000 },
                ["gunPistol"] = new ItemMeta { CreativeMode = "Player", StackLimit = 1, HasQuality = true },
                ["meleeToolHammerOfGodAdmin"] = new ItemMeta { CreativeMode = "Dev", StackLimit = 1 },
                ["TEST_ITEM_1"] = new ItemMeta { CreativeMode = "Test", StackLimit = 1 },
                ["decoCookingPot"] = new ItemMeta { CreativeMode = "Dev", StackLimit = 10, IsBlock = true },
                ["decoDevBlock"] = new ItemMeta { CreativeMode = "Dev", StackLimit = 10, IsBlock = true },
                ["schematicGunPistol"] = new ItemMeta { CreativeMode = "None", StackLimit = 1 },
                // A modded item that never sets CreativeMode reads as All.
                ["modKitsuneTool"] = new ItemMeta { CreativeMode = "All", StackLimit = 1, HasQuality = true },
            };
        }

        private List<SuspicionFlag> Check(int entityId, params InventoryItemSample[] slots) =>
            _detector.Check(entityId, slots, name => _items.TryGetValue(name, out var m) ? m : null, 0, _settings);

        private static InventoryItemSample Slot(string name, int count = 1, int quality = 0) =>
            new InventoryItemSample { ItemName = name, Count = count, Quality = quality };

        [Test]
        public void DevItem_IsHighSeverity()
        {
            var flags = Check(1, Slot("meleeToolHammerOfGodAdmin"));

            var flag = flags.Single();
            Assert.That(flag.Rule, Is.EqualTo("creative_item"));
            Assert.That(flag.Severity, Is.EqualTo(SuspicionSeverity.High));
            Assert.That(flag.Details["creativeMode"], Is.EqualTo("Dev"));
        }

        [Test]
        public void TestItem_IsFlagged()
        {
            Assert.That(Check(1, Slot("TEST_ITEM_1")).Single().Rule, Is.EqualTo("creative_item"));
        }

        [Test]
        public void DevBlock_IsMediumSeverity()
        {
            Assert.That(Check(1, Slot("decoDevBlock")).Single().Severity, Is.EqualTo(SuspicionSeverity.Medium));
        }

        [Test]
        public void DefaultAllowList_CoversPickupableDevBlocks()
        {
            Assert.That(Check(1, Slot("decoCookingPot")), Is.Empty);
        }

        [Test]
        public void HiddenItems_AreIgnoredByDefault_AndLowWhenEnabled()
        {
            Assert.That(Check(1, Slot("schematicGunPistol")), Is.Empty);

            _settings.FlagHiddenItems = true;
            var flag = Check(2, Slot("schematicGunPistol")).Single();
            Assert.That(flag.Rule, Is.EqualTo("hidden_item"));
            Assert.That(flag.Severity, Is.EqualTo(SuspicionSeverity.Low));
        }

        [Test]
        public void ModdedItemWithoutCreativeMode_IsNotFlagged()
        {
            Assert.That(Check(1, Slot("modKitsuneTool", 1, 6)), Is.Empty);
        }

        [Test]
        public void UnknownItem_IsSkipped()
        {
            Assert.That(Check(1, Slot("notInTheGame", 999999)), Is.Empty);
        }

        [Test]
        public void DevItems_CanBeSwitchedOff()
        {
            _settings.FlagDevItems = false;
            Assert.That(Check(1, Slot("meleeToolHammerOfGodAdmin")), Is.Empty);
        }

        [Test]
        public void OverQuality_UsesGameCapThenDefault_AndSettingsOverride()
        {
            // No game cap → vanilla 6.
            Assert.That(Check(1, Slot("gunPistol", 1, 6)), Is.Empty);
            Assert.That(Check(1, Slot("gunPistol", 1, 7)).Single().Rule, Is.EqualTo("over_quality"));

            // Game reports a higher cap (modded tiers).
            var withGameCap = _detector.Check(2, new[] { Slot("gunPistol", 1, 7) }, n => _items[n], 10, _settings);
            Assert.That(withGameCap, Is.Empty);

            // Settings override beats the game cap.
            _settings.MaxQuality = 5;
            var overridden = _detector.Check(3, new[] { Slot("gunPistol", 1, 6) }, n => _items[n], 10, _settings);
            Assert.That(overridden.Single().Details["maxQuality"], Is.EqualTo(5));
        }

        [Test]
        public void OverQuality_IgnoresItemsWithoutQuality()
        {
            Assert.That(Check(1, Slot("resourceWood", 10, 9)), Is.Empty);
        }

        [Test]
        public void OverStack_IsPerSlot()
        {
            Assert.That(Check(1, Slot("resourceWood", 6000), Slot("resourceWood", 6000)), Is.Empty);

            var flag = Check(2, Slot("resourceWood", 6001)).Single();
            Assert.That(flag.Rule, Is.EqualTo("over_stack"));
            Assert.That(flag.Details["stackLimit"], Is.EqualTo(6000));
        }

        [Test]
        public void SameFinding_IsReportedOncePerSession_UntilForgotten()
        {
            Assert.That(Check(1, Slot("meleeToolHammerOfGodAdmin")), Has.Count.EqualTo(1));
            Assert.That(Check(1, Slot("meleeToolHammerOfGodAdmin")), Is.Empty);

            _detector.Forget(1);
            Assert.That(Check(1, Slot("meleeToolHammerOfGodAdmin")), Has.Count.EqualTo(1));
        }

        [Test]
        public void CountJump_NeedsABaseline_ThenFlagsLargeGains()
        {
            _settings.CountJumpThreshold = 1000;
            Assert.That(Check(1, Slot("resourceWood", 5000)), Is.Empty, "first scan is the baseline");

            Assert.That(Check(1, Slot("resourceWood", 5900)), Is.Empty, "a gain under the threshold");

            var flag = Check(1, Slot("resourceWood", 6000), Slot("resourceWood", 2000)).Single();
            Assert.That(flag.Rule, Is.EqualTo("count_jump"));
            Assert.That(flag.Severity, Is.EqualTo(SuspicionSeverity.Low));
            Assert.That(flag.Details["before"], Is.EqualTo(5900));
            Assert.That(flag.Details["after"], Is.EqualTo(8000));
        }

        [Test]
        public void CountJump_TreatsANewItemAsGainedFromZero()
        {
            _settings.CountJumpThreshold = 1000;
            Check(1, Slot("gunPistol"));
            Assert.That(Check(1, Slot("gunPistol"), Slot("resourceWood", 1500)).Single().Details["before"], Is.EqualTo(0));
        }

        [Test]
        public void CountJump_MovingStacksBetweenSlots_IsNotAGain()
        {
            _settings.CountJumpThreshold = 1000;
            Check(1, Slot("resourceWood", 3000), Slot("resourceWood", 3000));
            Assert.That(Check(1, Slot("resourceWood", 6000)), Is.Empty);
        }

        [Test]
        public void CountJump_CanBeSwitchedOff()
        {
            _settings.CountJumpThreshold = 1000;
            _settings.FlagCountJumps = false;
            Check(1, Slot("resourceWood", 10));
            Assert.That(Check(1, Slot("resourceWood", 5000)), Is.Empty);
        }

        [Test]
        public void AllowList_ExcludesEveryRule_IncludingCountJumps()
        {
            _settings.CountJumpThreshold = 1000;
            _settings.AllowList = new List<string> { "resourceWood", "meleeTool*" };
            Check(1, Slot("resourceWood", 10));

            var flags = Check(1, Slot("resourceWood", 9000), Slot("meleeToolHammerOfGodAdmin"));
            Assert.That(flags, Is.Empty);
        }

        [TestCase("resourceWood", "resourceWood", true)]
        [TestCase("RESOURCEWOOD", "resourceWood", true)]
        [TestCase("resourceWood", "resource*", true)]
        [TestCase("resourceWood", "resourceWoo", false)]
        [TestCase("resourceWood", "  resourceWood  ", true)]
        [TestCase("resourceWood", "*", true)]
        [TestCase("resourceWood", "", false)]
        public void IsAllowed_MatchesExactOrPrefix(string item, string entry, bool expected)
        {
            Assert.That(InventoryDetector.IsAllowed(item, new[] { entry }), Is.EqualTo(expected));
        }

        [Test]
        public void EmptyAndNullSlots_AreIgnored()
        {
            var flags = _detector.Check(1, new[] { null, Slot("", 5), Slot("meleeToolHammerOfGodAdmin", 0) },
                n => _items[n], 0, _settings);
            Assert.That(flags, Is.Empty);
        }
    }
}
