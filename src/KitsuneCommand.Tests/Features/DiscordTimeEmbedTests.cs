using System.Linq;
using Discord;
using NUnit.Framework;
using KitsuneCommand.Features;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Tests.Features
{
    /// <summary>
    /// The card the Discord /time command posts. It used to be a placeholder
    /// that only said "Use /status for server details".
    /// </summary>
    [TestFixture]
    public class DiscordTimeEmbedTests
    {
        private const int DefaultDayLight = 18;

        private static GameClock At(int day, int hour, int minute, int bloodMoonDay) =>
            GameClock.FromWorldTime(
                (ulong)((day - 1) * 24000 + hour * 1000) + (ulong)(minute * 1000 + 59) / 60,
                bloodMoonDay, DefaultDayLight);

        private static string Field(Embed embed, string name) =>
            embed.Fields.Single(f => f.Name == name).Value;

        [Test]
        public void ShowsTheDayTimeAndNextBloodMoon()
        {
            var embed = DiscordBotFeature.BuildTimeEmbed(At(10, 21, 45, bloodMoonDay: 14), "Kitsune Den");

            Assert.That(embed.Title, Is.EqualTo("In-Game Time"));
            Assert.That(Field(embed, "Day"), Is.EqualTo("10"));
            Assert.That(Field(embed, "Time"), Is.EqualTo("21:45"));
            Assert.That(Field(embed, "Blood Moon"), Is.EqualTo("Day 14, in 4 days"));
            Assert.That(embed.Footer?.Text, Is.EqualTo("Kitsune Den"));
            Assert.That(embed.Color, Is.EqualTo(Color.Blue));
        }

        [Test]
        public void DayAndTimeSitSideBySide()
        {
            var embed = DiscordBotFeature.BuildTimeEmbed(At(10, 8, 0, bloodMoonDay: 14), "Den");

            Assert.That(embed.Fields.Single(f => f.Name == "Day").Inline, Is.True);
            Assert.That(embed.Fields.Single(f => f.Name == "Time").Inline, Is.True);
            Assert.That(embed.Fields.Single(f => f.Name == "Blood Moon").Inline, Is.False);
        }

        [Test]
        public void TurnsRedDuringABloodMoon()
        {
            var embed = DiscordBotFeature.BuildTimeEmbed(At(14, 23, 0, bloodMoonDay: 14), "Den");

            Assert.That(embed.Color, Is.EqualTo(Color.DarkRed));
            Assert.That(Field(embed, "Blood Moon"), Is.EqualTo("Happening now! It ends at 04:00."));
        }

        [Test]
        public void NoLongerPointsAtStatus()
        {
            var embed = DiscordBotFeature.BuildTimeEmbed(At(1, 7, 0, bloodMoonDay: 7), "Den");

            Assert.That(embed.Fields.Select(f => f.Value), Has.None.Contains("/status"));
        }

        [Test]
        public void TheNotLoadedReplyAvoidsEmDashes()
        {
            Assert.That(DiscordBotFeature.WorldNotLoadedReply, Does.Not.Contain("—"));
        }
    }
}
