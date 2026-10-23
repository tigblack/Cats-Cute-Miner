using CatMiners.Runtime.Integrations;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public sealed class AppMetricaNativeLogTests
    {
        const string Prefix = "I/AppMetrica: [com.multicastgames.catscuteminer] : [masked] ";
        [Test]
        public void AcceptedNativeEventReturnsOnlyApprovedNameNeverPayload()
        {
            Assert.IsTrue(AppMetricaNativeLog.TryGetAcceptedEvent(Prefix + "[42-AppMetrica-Reports] Event sent: time_spent with value {\"private\":\"secret\"}", out var name));
            Assert.AreEqual("time_spent", name);
        }
        [TestCase("Event received: time_spent")]
        [TestCase("Event sent: arbitrary_private_event")]
        [TestCase("Event sent: time_spent_suffix")]
        [TestCase("payload: Event sent: time_spent")]
        [TestCase("Event sent: Session start")]
        public void QueueReceiptPayloadTextAndUnknownNamesAreNotServerAcknowledgements(string message)
        {
            Assert.IsFalse(AppMetricaNativeLog.TryGetAcceptedEvent(Prefix + message, out var name));
            Assert.IsNull(name);
        }
        [Test]
        public void AnotherPackageAndUnitySpoofAreIgnored()
        {
            Assert.IsFalse(AppMetricaNativeLog.TryGetAcceptedEvent("I/AppMetrica: [other.package] : [masked] Event sent: time_spent", out _));
            Assert.IsFalse(AppMetricaNativeLog.TryGetAcceptedEvent("I/Unity: " + Prefix + "Event sent: time_spent", out _));
        }
    }
}
