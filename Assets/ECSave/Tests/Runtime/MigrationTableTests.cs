// Tests/Runtime.Tests/Unit/MigrationTableTests.cs
using NUnit.Framework;
using ECSave.Format;
using ECSave.Internal;

namespace ECSave.Tests.Runtime
{
    public class MigrationTableTests
    {
        private sealed class FakeStep : MigrationStepBase
        {
            private readonly ulong _newTypeNameHash;
            private readonly ulong _old;
            private readonly ulong _new;

            public FakeStep(ulong newTypeNameHash, ulong oldHash, ulong newHash)
            {
                _newTypeNameHash = newTypeNameHash;
                _old = oldHash;
                _new = newHash;
            }

            // ▼ 修正：メソッド → プロパティ override
            public override ulong NewTypeNameHash => _newTypeNameHash;
            public override ulong OldSchemaHash => _old;
            public override ulong NewSchemaHash => _new;
            public override void Migrate(ref ByteReader reader, ref ByteWriter writer) { }
        }

        private const ulong NewType = 0x1234;

        [Test]
        public void Register_ThenResolve()
        {
            var table = new MigrationTable();
            var step = new FakeStep(NewType, 0xAA, 0xBB);
            table.Register(step);

            var resolved = table.Resolve(NewType, 0xAA);
            Assert.AreSame(step, resolved);
        }

        [Test]
        public void Resolve_Unknown_ReturnsNull()
        {
            var table = new MigrationTable();
            Assert.IsNull(table.Resolve(NewType, 0xAA));
        }

        [Test]
        public void Register_Duplicate_Throws()
        {
            var table = new MigrationTable();
            table.Register(new FakeStep(NewType, 0xAA, 0xBB));

            // 同じ (NewTypeNameHash, OldSchemaHash) を再登録
            Assert.Throws<System.InvalidOperationException>(() =>
                table.Register(new FakeStep(NewType, 0xAA, 0xCC)));
        }

        [Test]
        public void Register_DifferentOld_Allowed()
        {
            var table = new MigrationTable();
            table.Register(new FakeStep(NewType, 0xAA, 0xBB));
            table.Register(new FakeStep(NewType, 0xCC, 0xDD)); // 旧ハッシュが違えばOK
            Assert.AreEqual(2, table.Count);
        }

        [Test]
        public void Register_DifferentNewType_Allowed()
        {
            var table = new MigrationTable();
            table.Register(new FakeStep(0x1111, 0xAA, 0xBB));
            table.Register(new FakeStep(0x2222, 0xAA, 0xCC)); // 新型名が違えばOK
            Assert.AreEqual(2, table.Count);
        }

        [Test]
        public void RenamedFrom_Resolve()
        {
            var table = new MigrationTable();
            table.RegisterRenamed(0x01D, 0x0E7);
            Assert.IsTrue(table.TryResolveRenamed(0x01D, out ulong resolved));
            Assert.AreEqual(0x0E7, resolved);
        }

        [Test]
        public void RenamedFrom_Duplicate_Throws()
        {
            var table = new MigrationTable();
            table.RegisterRenamed(0x01D, 0x0E7);
            Assert.Throws<System.InvalidOperationException>(() =>
                table.RegisterRenamed(0x01D, 0x0FF));
        }
    }
}
