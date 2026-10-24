using System;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class PrefabSchemaRegistryTests
    {
        [SetUp]
        public void SetUp() => PrefabSchemaRegistry.Clear();

        [TearDown]
        public void TearDown() => PrefabSchemaRegistry.Clear();

        [Test]
        public void Register_Get_Works()
        {
            PrefabSchemaRegistry.Register(0xAAAA, 0xBBBB);
            Assert.That(PrefabSchemaRegistry.TryGet(0xAAAA, out var hash), Is.True);
            Assert.That(hash, Is.EqualTo(0xBBBBUL));
        }

        [Test]
        public void Register_ZeroGuid_Ignored()
        {
            PrefabSchemaRegistry.Register(0UL, 0xBBBB);
            Assert.That(PrefabSchemaRegistry.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryGet_Unknown_ReturnsFalse()
        {
            Assert.That(PrefabSchemaRegistry.TryGet(0xCCCC, out _), Is.False);
        }

        [Test]
        public void Register_Overwrites()
        {
            PrefabSchemaRegistry.Register(0xAAAA, 0xBBBB);
            PrefabSchemaRegistry.Register(0xAAAA, 0xCCCC);
            Assert.That(PrefabSchemaRegistry.TryGet(0xAAAA, out var hash), Is.True);
            Assert.That(hash, Is.EqualTo(0xCCCCUL));
        }

        [Test]
        public void Clear_RemovesAll()
        {
            PrefabSchemaRegistry.Register(0xAAAA, 0xBBBB);
            PrefabSchemaRegistry.Clear();
            Assert.That(PrefabSchemaRegistry.Count, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public sealed class PrefabMigrationTableTests
    {
        private sealed class TestStep : PrefabMigrationStepBase
        {
            private readonly ulong _old;
            private readonly ulong _new;
            public override ulong OldPrefabSchemaHash => _old;
            public override ulong NewPrefabSchemaHash => _new;
            public override void Migrate(ref PrefabMigrationContext context) { }

            public TestStep(ulong oldHash, ulong newHash)
            {
                _old = oldHash;
                _new = newHash;
            }
        }

        [Test]
        public void From_Null_ReturnsEmpty()
        {
            var table = PrefabMigrationTable.From(null);
            Assert.That(table.Count, Is.EqualTo(0));
        }

        [Test]
        public void ResolveChain_SingleStep_ReturnsArray()
        {
            var table = PrefabMigrationTable.From(new PrefabMigrationStepBase[]
            {
                new TestStep(0x1UL, 0x2UL),
            });

            var chain = table.ResolveChain(0x1UL, 0x2UL);
            Assert.That(chain, Is.Not.Null);
            Assert.That(chain.Length, Is.EqualTo(1));
        }

        [Test]
        public void ResolveChain_MultiStep_ReturnsChain()
        {
            var table = PrefabMigrationTable.From(new PrefabMigrationStepBase[]
            {
                new TestStep(0x1UL, 0x2UL),
                new TestStep(0x2UL, 0x3UL),
                new TestStep(0x3UL, 0x4UL),
            });

            var chain = table.ResolveChain(0x1UL, 0x4UL);
            Assert.That(chain, Is.Not.Null);
            Assert.That(chain.Length, Is.EqualTo(3));
        }

        [Test]
        public void ResolveChain_SameHash_ReturnsEmpty()
        {
            var table = PrefabMigrationTable.From(Array.Empty<PrefabMigrationStepBase>());
            var chain = table.ResolveChain(0x1UL, 0x1UL);
            Assert.That(chain, Is.Empty);
        }

        [Test]
        public void ResolveChain_NoPath_ReturnsNull()
        {
            var table = PrefabMigrationTable.From(new PrefabMigrationStepBase[]
            {
                new TestStep(0x1UL, 0x2UL),
            });
            var chain = table.ResolveChain(0x1UL, 0x99UL);
            Assert.That(chain, Is.Null);
        }

        [Test]
        public void Register_DuplicateOldHash_Throws()
        {
            var table = new PrefabMigrationTable();
            table.Register(new TestStep(0x1UL, 0x2UL));
            Assert.Throws<InvalidOperationException>(
                () => table.Register(new TestStep(0x1UL, 0x3UL)));
        }

        [Test]
        public void ResolveChain_Cycle_ReturnsNull()
        {
            var table = PrefabMigrationTable.From(new PrefabMigrationStepBase[]
            {
                new TestStep(0x1UL, 0x2UL),
                new TestStep(0x2UL, 0x1UL),
            });

            // 0x1 → 0x99 doesn't exist, chain loop hits cycle and returns null
            var chain = table.ResolveChain(0x1UL, 0x99UL);
            Assert.That(chain, Is.Null);
        }

        [Test]
        public void Register_Null_Throws()
        {
            var table = new PrefabMigrationTable();
            Assert.Throws<ArgumentNullException>(() => table.Register(null));
        }

        [Test]
        public void From_ContainsNull_Ignored()
        {
            var table = PrefabMigrationTable.From(new PrefabMigrationStepBase[]
            {
                new TestStep(0x1UL, 0x2UL),
                null,
                new TestStep(0x3UL, 0x4UL),
            });
            Assert.That(table.Count, Is.EqualTo(2));
        }

        [Test]
        public void ResolveChain_ExceedsMaxChainLength_ReturnsNull()
        {
            // 33-step chain (MaxChain=32) should fail to resolve
            var steps = new PrefabMigrationStepBase[33];
            for (int i = 0; i < 33; i++)
                steps[i] = new TestStep((ulong)(i + 1), (ulong)(i + 2));

            var table = PrefabMigrationTable.From(steps);
            var chain = table.ResolveChain(1UL, 34UL);
            Assert.That(chain, Is.Null);
        }
    }
}
