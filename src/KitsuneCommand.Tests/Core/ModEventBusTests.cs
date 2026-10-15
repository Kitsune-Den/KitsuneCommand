using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using KitsuneCommand.Core;

namespace KitsuneCommand.Tests.Core
{
    [TestFixture]
    public class ModEventBusTests
    {
        private ModEventBus _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new ModEventBus();
        }

        // Simple event types for testing
        private class TestEvent
        {
            public string Message { get; set; }
        }

        private class OtherEvent
        {
            public int Value { get; set; }
        }

        [Test]
        public void Subscribe_And_Publish_ReceivesEvent()
        {
            TestEvent received = null;
            _bus.Subscribe<TestEvent>(e => received = e);

            _bus.Publish(new TestEvent { Message = "hello" });

            Assert.That(received, Is.Not.Null);
            Assert.That(received.Message, Is.EqualTo("hello"));
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                _bus.Publish(new TestEvent { Message = "nobody listening" }));
        }

        [Test]
        public void MultipleSubscribers_AllReceiveEvent()
        {
            var received = new List<string>();

            _bus.Subscribe<TestEvent>(e => received.Add("sub1:" + e.Message));
            _bus.Subscribe<TestEvent>(e => received.Add("sub2:" + e.Message));

            _bus.Publish(new TestEvent { Message = "hi" });

            Assert.That(received, Has.Count.EqualTo(2));
            Assert.That(received, Does.Contain("sub1:hi"));
            Assert.That(received, Does.Contain("sub2:hi"));
        }

        [Test]
        public void Unsubscribe_StopsReceivingEvents()
        {
            int callCount = 0;
            Action<TestEvent> handler = e => callCount++;

            _bus.Subscribe(handler);
            _bus.Publish(new TestEvent { Message = "1" });
            Assert.That(callCount, Is.EqualTo(1));

            _bus.Unsubscribe(handler);
            _bus.Publish(new TestEvent { Message = "2" });
            Assert.That(callCount, Is.EqualTo(1), "Should not receive events after unsubscribe");
        }

        [Test]
        public void DifferentEventTypes_AreIsolated()
        {
            TestEvent testReceived = null;
            OtherEvent otherReceived = null;

            _bus.Subscribe<TestEvent>(e => testReceived = e);
            _bus.Subscribe<OtherEvent>(e => otherReceived = e);

            _bus.Publish(new TestEvent { Message = "test" });

            Assert.That(testReceived, Is.Not.Null);
            Assert.That(otherReceived, Is.Null, "Other event type should not receive TestEvent");
        }

        [Test]
        public void HandlerException_DoesNotCrashOtherHandlers()
        {
            var received = new List<string>();

            _bus.Subscribe<TestEvent>(e => received.Add("before"));
            _bus.Subscribe<TestEvent>(e => throw new InvalidOperationException("boom"));
            _bus.Subscribe<TestEvent>(e => received.Add("after"));

            Assert.DoesNotThrow(() => _bus.Publish(new TestEvent { Message = "test" }));
            Assert.That(received, Does.Contain("before"));
            Assert.That(received, Does.Contain("after"));
        }

        [Test]
        public void Subscribe_ThrowsOnNullHandler()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _bus.Subscribe<TestEvent>(null));
        }

        [Test]
        public void Unsubscribe_ThrowsOnNullHandler()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _bus.Unsubscribe<TestEvent>(null));
        }

        [Test]
        public void Unsubscribe_WhenNotSubscribed_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                _bus.Unsubscribe<TestEvent>(e => { }));
        }

        [Test]
        public void ConcurrentPublish_IsThreadSafe()
        {
            int counter = 0;
            _bus.Subscribe<TestEvent>(e => Interlocked.Increment(ref counter));

            var tasks = new Task[10];
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        _bus.Publish(new TestEvent { Message = "concurrent" });
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.That(counter, Is.EqualTo(1000));
        }

        [Test]
        public void ConcurrentSubscribeAndPublish_DoesNotThrow()
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var publishTask = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    _bus.Publish(new TestEvent { Message = "go" });
                }
            });

            var subscribeTask = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    Action<TestEvent> handler = e => { };
                    _bus.Subscribe(handler);
                    Thread.Sleep(1);
                    _bus.Unsubscribe(handler);
                }
            });

            Assert.DoesNotThrow(() =>
            {
                cts.CancelAfter(TimeSpan.FromSeconds(1));
                Task.WaitAll(new[] { publishTask, subscribeTask }, TimeSpan.FromSeconds(3));
            });
        }
    }
}
