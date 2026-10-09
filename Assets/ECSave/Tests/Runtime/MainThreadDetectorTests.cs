// Tests/Runtime.Tests/Unit/MainThreadDetectorTests.cs
using NUnit.Framework;
using ECSave.Internal;

namespace ECSave.Tests.Runtime
{
    public class MainThreadDetectorTests
    {
        [SetUp]
        public void SetUp() => MainThreadDetector.ResetForTests();

        [TearDown]
        public void TearDown() => MainThreadDetector.ResetForTests();

        [Test]
        public void IsMainThread_AfterRecord_True()
        {
            MainThreadDetector.RecordMainThreadId();
            Assert.IsTrue(MainThreadDetector.IsMainThread());
        }

        [Test]
        public void IsMainThread_BeforeRecord_False()
        {
            Assert.IsFalse(MainThreadDetector.IsMainThread());
        }

        [Test]
        public void ThrowIfNotMainThread_AfterRecord_NoThrow()
        {
            MainThreadDetector.RecordMainThreadId();
            Assert.DoesNotThrow(() => MainThreadDetector.ThrowIfNotMainThread());
        }

        [Test]
        public void RecordMainThreadId_Twice_KeepsFirst()
        {
            MainThreadDetector.RecordMainThreadId();
            MainThreadDetector.RecordMainThreadId();
            Assert.IsTrue(MainThreadDetector.IsMainThread());
        }
    }
}
