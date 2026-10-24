using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;

namespace ECSave.Tests.Runtime
{
    [TestFixture]
    public sealed class OperationExceptionTests
    {
        [Test]
        public void DriveThrows_StateCompleted_PendingErrorSet()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var op = new Operation(
                    OperationKind.Serialize,
                    default,
                    _ => throw new InvalidOperationException("drive boom"));

                bool done = op.MoveNext();

                Assert.That(done, Is.True);
                Assert.That(op.IsComplete, Is.True);
                Assert.That(op.HasPendingError, Is.True);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void OnCompleteThrows_LoggedButStateCompleted()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var op = new Operation(
                    OperationKind.Serialize,
                    default,
                    _ => true);
                op.OnComplete = _ => throw new InvalidOperationException("complete boom");

                Assert.DoesNotThrow(() => op.MoveNext());
                Assert.That(op.IsComplete, Is.True);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void SetPendingError_FirstWins()
        {
            var op = new Operation(OperationKind.Serialize, default);
            var first = new ErrorInfo(SaveLoadErrorCode.CorruptedData, "first");
            var second = new ErrorInfo(SaveLoadErrorCode.ValidationFailed, "second");

            op.SetPendingError(first);
            op.SetPendingError(second);

            Assert.That(op.HasPendingError, Is.True);
            Assert.That(op.PendingError.Code, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
        }

        [Test]
        public void MoveNext_AfterComplete_ReturnsTrueImmediately()
        {
            var op = new Operation(
                OperationKind.Serialize,
                default,
                _ => true);

            op.MoveNext();
            Assert.That(op.IsComplete, Is.True);

            // 2回目は _drive を呼ばず true
            bool second = op.MoveNext();
            Assert.That(second, Is.True);
        }

        [Test]
        public void MarkWorldDestroyed_AfterComplete_SetsFlag()
        {
            var op = new Operation(
                OperationKind.Serialize,
                default,
                _ => true);
            op.MoveNext();
            Assert.That(op.IsComplete, Is.True);

            op.MarkWorldDestroyed();

            Assert.That(op.WorldDestroyed, Is.True);
            Assert.That(op.IsComplete, Is.True);
        }

        [Test]
        public void MoveNext_PendingToRunning_Transitions()
        {
            var op = new Operation(
                OperationKind.Serialize,
                default,
                _ => false);

            Assert.That(op.State, Is.EqualTo(OperationState.Pending));
            op.MoveNext();
            Assert.That(op.State, Is.EqualTo(OperationState.Running));
            Assert.That(op.IsComplete, Is.False);
        }

        [Test]
        public void OnComplete_NullIsSafe()
        {
            var op = new Operation(
                OperationKind.Serialize,
                default,
                _ => true);

            Assert.DoesNotThrow(() => op.MoveNext());
        }
    }
}
