using System;
using NUnit.Framework;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    [TestFixture]
    public sealed class SaveRuntimeSystemExceptionTests
    {
        private World _world;
        private SaveRuntimeSystem _sys;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("SaveRuntimeSystemExceptionTestWorld");
            var group = _world.CreateSystemManaged<ECSaveSnapshotGroup>();
            _sys = _world.CreateSystemManaged<SaveRuntimeSystem>();
            group.AddSystemToUpdateList(_sys);
            group.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void OnUpdate_DriveThrows_LoggedAndCompleted()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var op = new Operation(
                    OperationKind.Serialize,
                    default,
                    _ => throw new InvalidOperationException("drive boom"));
                _sys.SetCurrent(op);

                Assert.DoesNotThrow(() => _sys.Update());

                Assert.That(op.IsComplete, Is.True);
                Assert.That(op.HasPendingError, Is.True);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void OnDestroy_PendingOperation_AbortInvoked()
        {
            var op = new Operation(
                OperationKind.Serialize,
                default,
                _ => false);  // 未完了のまま
            SaveLoadErrorCode abortedCode = SaveLoadErrorCode.None;
            op.OnAbort = code => abortedCode = code;
            _sys.SetCurrent(op);

            _world.Dispose();

            Assert.That(op.WorldDestroyed, Is.True);
            Assert.That(op.IsComplete, Is.True);
            Assert.That(abortedCode, Is.EqualTo(SaveLoadErrorCode.SerializationFailed));

            _world = null;
        }

        [Test]
        public void OnDestroy_DeserializeOperation_AbortReturnsWorldModificationFailed()
        {
            var op = new Operation(
                OperationKind.Deserialize,
                default,
                _ => false);
            SaveLoadErrorCode abortedCode = SaveLoadErrorCode.None;
            op.OnAbort = code => abortedCode = code;
            _sys.SetCurrent(op);

            _world.Dispose();

            Assert.That(abortedCode, Is.EqualTo(SaveLoadErrorCode.WorldModificationFailed));

            _world = null;
        }

        [Test]
        public void OnDestroy_AbortThrows_LoggedButCompletes()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var op = new Operation(
                    OperationKind.Serialize,
                    default,
                    _ => false);
                op.OnAbort = _ => throw new InvalidOperationException("abort boom");
                _sys.SetCurrent(op);

                _world.Dispose();

                Assert.That(op.IsComplete, Is.True);

                _world = null;
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void OnUpdate_AfterComplete_IsNoOp()
        {
            var op = new Operation(
                OperationKind.Serialize,
                default,
                _ => true);
            _sys.SetCurrent(op);
            _sys.Update();
            Assert.That(op.IsComplete, Is.True);

            Assert.DoesNotThrow(() => _sys.Update());
        }
    }
}
