using System;
using NUnit.Framework;
using KitsuneCommand.Features;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class StorePurchasePolicyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 4, 15, 30, 0, DateTimeKind.Utc);

        // ── EffectivePrice ──────────────────────────────────────

        [Test]
        public void EffectivePrice_DefaultSettings_IsBasePrice()
        {
            Assert.That(StorePurchasePolicy.EffectivePrice(250, new StoreSettings()), Is.EqualTo(250));
        }

        [Test]
        public void EffectivePrice_NullSettings_IsBasePrice()
        {
            Assert.That(StorePurchasePolicy.EffectivePrice(250, null), Is.EqualTo(250));
        }

        [TestCase(100, 1.5, 150)]
        [TestCase(100, 0.5, 50)]
        [TestCase(5, 0.5, 3)]    // 2.5 rounds up
        [TestCase(3, 0.5, 2)]    // 1.5 rounds up
        [TestCase(10, 0.33, 3)]  // 3.3 rounds down
        [TestCase(0, 2.0, 0)]
        public void EffectivePrice_AppliesMultiplier_RoundingHalvesUp(int basePrice, double multiplier, int expected)
        {
            var settings = new StoreSettings { PriceMultiplier = multiplier };
            Assert.That(StorePurchasePolicy.EffectivePrice(basePrice, settings), Is.EqualTo(expected));
        }

        [TestCase(0.0, 10)]    // below range: held at 0.1, so a free store needs price 0, not multiplier 0
        [TestCase(-3.0, 10)]
        [TestCase(50.0, 1000)] // above range: held at 10
        [TestCase(double.NaN, 100)]
        public void EffectivePrice_MultiplierOutsideRange_IsHeldToRange(double multiplier, int expected)
        {
            var settings = new StoreSettings { PriceMultiplier = multiplier };
            Assert.That(StorePurchasePolicy.EffectivePrice(100, settings), Is.EqualTo(expected));
        }

        [Test]
        public void EffectivePrice_HugePrice_DoesNotOverflow()
        {
            var settings = new StoreSettings { PriceMultiplier = 10.0 };
            Assert.That(StorePurchasePolicy.EffectivePrice(int.MaxValue, settings), Is.EqualTo(int.MaxValue));
        }

        // ── LimitRefusal: defaults ──────────────────────────────

        [Test]
        public void LimitRefusal_DefaultSettings_AllowsEverything()
        {
            var refusal = StorePurchasePolicy.LimitRefusal(new StoreSettings(), Now, Now.AddSeconds(-1), 500);
            Assert.That(refusal, Is.Null);
        }

        // ── LimitRefusal: cooldown ──────────────────────────────

        [Test]
        public void LimitRefusal_Cooldown_RefusesInsideWindow()
        {
            var settings = new StoreSettings { PurchaseCooldownSeconds = 60 };
            var refusal = StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddSeconds(-15), 1);
            Assert.That(refusal, Is.EqualTo("Purchase cooldown: the next purchase is allowed in 45s."));
        }

        [Test]
        public void LimitRefusal_Cooldown_AllowsOnceWindowHasPassed()
        {
            var settings = new StoreSettings { PurchaseCooldownSeconds = 60 };
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddSeconds(-60), 1), Is.Null);
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddMinutes(-5), 1), Is.Null);
        }

        [Test]
        public void LimitRefusal_Cooldown_AllowsFirstEverPurchase()
        {
            var settings = new StoreSettings { PurchaseCooldownSeconds = 60 };
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, null, 0), Is.Null);
        }

        [Test]
        public void LimitRefusal_Cooldown_RoundsWaitUpAndFormatsMinutes()
        {
            var settings = new StoreSettings { PurchaseCooldownSeconds = 300 };
            var refusal = StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddSeconds(-174.5), 1);
            Assert.That(refusal, Does.EndWith("in 2m 6s."));
        }

        [Test]
        public void LimitRefusal_Cooldown_FormatsWholeMinutesAndHours()
        {
            var settings = new StoreSettings { PurchaseCooldownSeconds = 86400 };
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddSeconds(-86400 + 120), 1), Does.EndWith("in 2m."));
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddHours(-21), 1), Does.EndWith("in 3h."));
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddMinutes(-80), 1), Does.EndWith("in 22h 40m."));
        }

        // ── LimitRefusal: daily cap ─────────────────────────────

        [Test]
        public void LimitRefusal_DailyCap_AllowsBelowCap()
        {
            var settings = new StoreSettings { MaxDailyPurchases = 3 };
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddHours(-1), 2), Is.Null);
        }

        [Test]
        public void LimitRefusal_DailyCap_RefusesAtCap_WithTimeUntilUtcMidnight()
        {
            var settings = new StoreSettings { MaxDailyPurchases = 3 };
            var refusal = StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddHours(-1), 3);
            Assert.That(refusal, Is.EqualTo("Daily purchase limit reached (3 per day). It resets in 8h 30m."));
        }

        [Test]
        public void LimitRefusal_DailyCapZero_IsUnlimited()
        {
            var settings = new StoreSettings { MaxDailyPurchases = 0 };
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddHours(-1), 10_000), Is.Null);
        }

        [Test]
        public void LimitRefusal_BothLimitsHit_ReportsDailyCap()
        {
            // The cap is the longer wait, so it's the useful one to report.
            var settings = new StoreSettings { MaxDailyPurchases = 1, PurchaseCooldownSeconds = 60 };
            var refusal = StorePurchasePolicy.LimitRefusal(settings, Now, Now.AddSeconds(-10), 1);
            Assert.That(refusal, Does.StartWith("Daily purchase limit reached"));
        }
    }
}
