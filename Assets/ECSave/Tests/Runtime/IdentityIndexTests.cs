// Tests/Runtime.Tests/Unit/IdentityIndexTests.cs
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using ECSave.Internal;

namespace ECSave.Tests.Runtime
{
    public unsafe class IdentityIndexTests
    {
        private IdentityIndex _index;

        [SetUp]
        public void SetUp()
        {
            _index = new IdentityIndex(16, typeCount: 4, Allocator.TempJob);
        }

        [TearDown]
        public void TearDown() => _index.Dispose();

        [Test]
        public void WorldAndSaveEntry_Match()
        {
            fixed (byte* k = new byte[] { 42, 0, 0, 0 })
            {
                _index.AddWorldEntry(0, k, 4, new Entity { Index = 100, Version = 1 });
                _index.AddSaveEntry(0, k, 4, persistentId: 7);

                Assert.IsTrue(_index.TryMatchSaveEntry(0, k, 4, out int saveIdx));
                Assert.AreNotEqual(-1, saveIdx);
            }
        }

        [Test]
        public void DifferentType_DoesNotMatch()
        {
            fixed (byte* k = new byte[] { 1, 0, 0, 0 })
            {
                _index.AddWorldEntry(0, k, 4, new Entity { Index = 1, Version = 1 });
                Assert.IsFalse(_index.TryMatchSaveEntry(1, k, 4, out _));
            }
        }

        [Test]
        public void SameKey_WorldConflict_Detected()
        {
            fixed (byte* k = new byte[] { 5, 0, 0, 0 })
            {
                _index.AddWorldEntry(0, k, 4, new Entity { Index = 1, Version = 1 });
                Assert.IsTrue(_index.HasWorldConflict(0, k, 4));
            }
        }

        [Test]
        public void SameKey_SaveConflict_Detected()
        {
            fixed (byte* k = new byte[] { 9, 0, 0, 0 })
            {
                _index.AddSaveEntry(0, k, 4, 1);
                Assert.IsTrue(_index.HasSaveConflict(0, k, 4));
            }
        }

        [Test]
        public void HashCollision_DifferentKeys_NotMatched()
        {
            // 異なるキーで同じバケットに入っても、バイト比較で区別される
            byte[] k1 = { 1, 2, 3, 4 };
            byte[] k2 = { 1, 2, 3, 5 };
            fixed (byte* p1 = k1)
            fixed (byte* p2 = k2)
            {
                _index.AddWorldEntry(0, p1, 4, new Entity { Index = 1, Version = 1 });
                Assert.IsFalse(_index.TryMatchSaveEntry(0, p2, 4, out _));
            }
        }
    }
}
