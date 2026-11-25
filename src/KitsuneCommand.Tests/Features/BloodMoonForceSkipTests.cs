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
    /// The panel's force-skip arrives on a web thread. It used to change the vote
    /// state right there while chat votes changed it on the main thread, so the two
    /// could corrupt the voter set between them. It now hops to the main thread.
    ///
    /// The game's main thread (ModEntry.MainThreadContext) can't be loaded in tests,
    /// so a stand-in thread plays it through the feature's RunOnMainThread seam.
    /// </summary>
    [TestFixture]
    public class BloodMoonForceSkipTests
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
