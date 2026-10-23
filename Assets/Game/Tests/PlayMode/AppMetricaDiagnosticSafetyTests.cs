using System;
using System.Reflection;
using CatMiners.Runtime.Integrations;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public sealed class AppMetricaDiagnosticSafetyTests
    {
        static void RecordError(Exception exception)
        {
            var type = typeof(AppMetricaDiagnostics);
            var stage = Enum.Parse(type.GetNestedType("Stage", BindingFlags.NonPublic), "EventError");
            type.GetMethod("Record", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { stage, GameEvent.TimeSpent, exception, null });
        }
        [Test]
        public void DiagnosticExceptionCannotLeakMessagePayloadOrStack()
        {
            RecordError(new InvalidOperationException("private-token-and-purchase-receipt"));
            var lines = AppMetricaDiagnostics.Snapshot();
            string last = lines[lines.Length - 1];
            StringAssert.Contains("InvalidOperationException", last);
            StringAssert.Contains("time_spent", last);
            StringAssert.DoesNotContain("private-token", string.Join("\n", lines));
            StringAssert.DoesNotContain("purchase-receipt", string.Join("\n", lines));
        }
        [Test]
        public void KeyFingerprintUsesExactInputWithoutRetainingKeyText()
        {
            const string fake = "01234567-abcd-1234-abcd-0123456789ab";
            string hash = AppMetricaDiagnostics.KeyFingerprint(fake);
            Assert.AreEqual(64, hash.Length);
            Assert.AreNotEqual(hash, AppMetricaDiagnostics.KeyFingerprint(fake.ToUpperInvariant()));
            StringAssert.DoesNotContain(fake, hash);
            Assert.AreEqual("missing", AppMetricaDiagnostics.KeyFingerprint(null));
        }
        [Test]
        public void BurstIsBoundedAndSnapshotCannotModifyStoredLog()
        {
            for (int i = 0; i < 510; i++) RecordError(null);
            var lines = AppMetricaDiagnostics.Snapshot();
            Assert.LessOrEqual(lines.Length, 500);
            string original = lines[0];
            lines[0] = "injected payload";
            Assert.AreEqual(original, AppMetricaDiagnostics.Snapshot()[0]);
        }
    }
}
