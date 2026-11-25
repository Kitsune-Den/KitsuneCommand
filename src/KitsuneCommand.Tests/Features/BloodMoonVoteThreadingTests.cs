using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Moq;
using NUnit.Framework;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;

namespace KitsuneCommand.Tests.Features
{
    /// <summary>
    /// The panel's force-skip and vote status arrive on web threads while chat votes
    /// change the vote on the main thread. The force-skip used to reset the vote right
    /// there, so the two could corrupt the voter set between them; it now hops to the
    /// main thread. The status used to walk the voter set mid-change; it now reads a
    /// finished copy the main thread saves after every change.
    ///
    /// The game's main thread (ModEntry.MainThreadContext) can't be loaded in tests,
    /// so a stand-in thread plays it through the feature's RunOnMainThread seam.
    /// </summary>
    [TestFixture]
    public class BloodMoonVoteThreadingTests
    {
        private ModEventBus _bus;
        private FakeGameVoteFeature _feature;

        [SetUp]
        public void SetUp()
        {
            _bus = new ModEventBus();
            _feature = new FakeGameVoteFeature(_bus);
            _feature.LoadSettings();
            _feature.Start();
        }

        [TearDown]
        public void TearDown() => _feature.Stop();

        [Test]
        public void ForceSkipRunsOnTheMainThread()
        {
            OpenVote();
            var publishedOn = new List<int>();
            _bus.Subscribe<BloodMoonVoteUpdateEvent>(_ => publishedOn.Add(Thread.CurrentThread.ManagedThreadId));

            using (var main = new MainThreadPump())
            {
                _feature.MainThread = main;

                Assert.That(_feature.ForceSkip(), Is.True);

                Assert.That(_feature.SkippedOn, Is.EqualTo(new[] { main.ThreadId }));
                Assert.That(publishedOn, Is.EqualTo(new[] { main.ThreadId }));
            }

            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);
        }

        [Test]
        public void ForceSkipThatTheMainThreadNeverRunsChangesNothing()
        {
            OpenVote();
            _feature.MainThreadStalled = true;

            Assert.That(_feature.ForceSkip(), Is.False);

            Assert.That(_feature.SkippedOn, Is.Empty);
            Assert.That(_feature.GetVoteStatus().IsActive, Is.True);
        }

        [Test]
        public void AVotePassedInChatSkipsWithItsVoteCount()
        {
            OpenVote();

            var result = _feature.CastVote("steam_1", "Fox", 171);

            Assert.That(result, Is.EqualTo(VoteResult.Passed));
            Assert.That(_feature.SkippedVoteCounts, Is.EqualTo(new[] { 1 }));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);
            Assert.That(_feature.GetVoteStatus().CurrentVotes, Is.EqualTo(0));
        }

        [Test]
        public void ForceSkipStartsTheCooldown()
        {
            _feature.UpdateSettings(new BloodMoonVoteSettings { CooldownMinutes = 30 });
            OpenVote();

            Assert.That(_feature.ForceSkip(), Is.True);
            OpenVote();

            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);
        }

        [Test]
        public void StatusReadOnAWebThreadIsNeverHalfAChange()
        {
            const int rounds = 20000;
            var problems = new List<string>();
            var reads = 0;

            using (var main = new MainThreadPump())
            using (var finished = new ManualResetEventSlim(false))
            {
                // The main thread opens the vote and a chat vote passes it, over and over.
                main.Post(_ =>
                {
                    for (var i = 0; i < rounds; i++)
                    {
                        OpenVote();
                        _feature.CastVote("steam_" + i, "Fox", 171);
                    }
                    finished.Set();
                }, null);

                // Meanwhile the panel polls the status.
                while (!finished.IsSet && problems.Count == 0)
                {
                    reads++;
                    try
                    {
                        var status = _feature.GetVoteStatus();
                        if (status.CurrentVotes != status.Voters.Count)
                            problems.Add($"{status.CurrentVotes} votes but {status.Voters.Count} voters");
                        else if (!status.IsActive && status.CurrentVotes > 0)
                            problems.Add($"closed with {status.CurrentVotes} votes");
                    }
                    catch (Exception ex)
                    {
                        problems.Add(ex.GetType().Name + ": " + ex.Message);
                    }
                }

                Assert.That(finished.Wait(TimeSpan.FromSeconds(30)), Is.True, "The main thread never finished its rounds.");
            }

            Assert.That(problems, Is.Empty, $"after {reads} reads");
            Assert.That(_feature.SkippedVoteCounts, Has.Count.EqualTo(rounds));
        }

        [Test]
        public void StatusFollowsTheVote()
        {
            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);

            OpenVote();
            var open = _feature.GetVoteStatus();
            Assert.That(open.IsActive, Is.True);
            Assert.That(open.BloodMoonDay, Is.EqualTo(9));
            Assert.That(open.Voters, Is.Empty);

            _feature.Stop();
            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);
        }

        [Test]
        public void StatusVotersAreACopy()
        {
            OpenVote();

            _feature.GetVoteStatus().Voters.Add("steam_9");

            Assert.That(_feature.GetVoteStatus().Voters, Is.Empty);
            Assert.That(_feature.CastVote("steam_9", "Fox", 171), Is.EqualTo(VoteResult.Passed));
        }

        private void OpenVote()
        {
            _bus.Publish(new SkyChangedEvent { Day = 9, Hour = 22, BloodMoonDay = 9, IsBloodMoon = true, DuskHour = 22, DawnHour = 4 });
        }

        /// <summary>
        /// Records skips instead of sending console commands to a game that isn't there,
        /// and runs main-thread work on <see cref="MainThread"/> when one is set.
        /// </summary>
        private sealed class FakeGameVoteFeature : BloodMoonVoteFeature
        {
            public readonly List<int> SkippedOn = new List<int>();
            public readonly List<int> SkippedVoteCounts = new List<int>();
            public SynchronizationContext MainThread;
            public bool MainThreadStalled;

            public FakeGameVoteFeature(ModEventBus bus)
                : base(bus, new ConfigManager(new AppSettings()), new Mock<ISettingsRepository>().Object, new LivePlayerManager(bus))
            {
            }

            protected override void SkipToMorning(int voteCount)
            {
                SkippedOn.Add(Thread.CurrentThread.ManagedThreadId);
                SkippedVoteCounts.Add(voteCount);
            }

            protected override bool RunOnMainThread(Func<bool> work)
            {
                if (MainThreadStalled) return false;
                if (MainThread == null) return work();

                var result = false;
                using (var done = new ManualResetEventSlim(false))
                {
                    MainThread.Post(_ =>
                    {
                        result = work();
                        done.Set();
                    }, null);
                    if (!done.Wait(TimeSpan.FromSeconds(5)))
                        Assert.Fail("The stand-in main thread never ran the work.");
                }
                return result;
            }
        }

        /// <summary>Stands in for the game's main thread: runs posted work in order on its own thread.</summary>
        private sealed class MainThreadPump : SynchronizationContext, IDisposable
        {
            private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
            private readonly Thread _thread;

            public MainThreadPump()
            {
                _thread = new Thread(() =>
                {
                    SetSynchronizationContext(this);
                    foreach (var work in _work.GetConsumingEnumerable()) work();
                }) { IsBackground = true };
                _thread.Start();
            }

            public int ThreadId => _thread.ManagedThreadId;

            public override void Post(SendOrPostCallback d, object state) => _work.Add(() => d(state));

            public void Dispose()
            {
                _work.CompleteAdding();
                _thread.Join(TimeSpan.FromSeconds(5));
            }
        }
    }
}
