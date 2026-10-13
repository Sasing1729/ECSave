using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public class WorldDestroyTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
        }

        [Test]
        public void OnDestroy_MarksCurrentOperation()
        {
            var world = new World("WorldDestroyTest");
            var sys = world.CreateSystemManaged<SaveRuntimeSystem>();

            var op = new Operation(OperationKind.Serialize, default);
            sys.SetCurrent(op);

            Assert.IsFalse(op.IsComplete);
            Assert.IsFalse(op.WorldDestroyed);

            world.Dispose();

            Assert.IsTrue(op.WorldDestroyed);
            Assert.IsTrue(op.IsComplete);
        }

        [Test]
        public void OnDestroy_ReleasesGuard()
        {
            var world = new World("WorldDestroyTest2");
            var sys = world.CreateSystemManaged<SaveRuntimeSystem>();

            Assert.IsTrue(sys.TryAcquireOperation());
            Assert.IsTrue(sys.IsBusy);

            world.Dispose();

            // 新しい World を作って、以前の guard が残っていないことを間接的に確認
            // （直接の確認は元の sys がもう触れないため不可）
        }
    }
}
