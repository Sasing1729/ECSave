using System;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class PrefabMigrationContextTests
    {
        [Test]
        public void Default_HasZeroLength()
        {
            var ctx = default(PrefabMigrationContext);
            Assert.That(ctx.NewLength, Is.EqualTo(0));
            Assert.That(ctx.OldData.Length, Is.EqualTo(0));
            Assert.That(ctx.NewBuffer.Length, Is.EqualTo(0));
        }

        [Test]
        public void WithOldDataAndBuffer_ExposesSpans()
        {
            var old = new byte[] { 1, 2, 3 };
            var newBuf = new byte[8];
            var ctx = new PrefabMigrationContext
            {
                OldData = old,
                NewBuffer = newBuf,
                NewLength = 0,
            };
            Assert.That(ctx.OldData.Length, Is.EqualTo(3));
            Assert.That(ctx.NewBuffer.Length, Is.EqualTo(8));
        }

        [Test]
        public void MigrationCanWriteToNewBuffer()
        {
            var old = new byte[] { 1, 2 };
            var newBuf = new byte[4];
            var ctx = new PrefabMigrationContext
            {
                OldData = old,
                NewBuffer = newBuf,
                NewLength = 0,
            };
            ctx.OldData.CopyTo(ctx.NewBuffer);
            ctx.NewLength = 2;

            Assert.That(ctx.NewBuffer[0], Is.EqualTo(1));
            Assert.That(ctx.NewBuffer[1], Is.EqualTo(2));
            Assert.That(ctx.NewLength, Is.EqualTo(2));
        }
    }

    [TestFixture]
    public sealed class PrefabMigrationStepBaseTests
    {
        private sealed class ConcreteStep : PrefabMigrationStepBase
        {
            public override ulong OldPrefabSchemaHash => 0xAAAA;
            public override ulong NewPrefabSchemaHash => 0xBBBB;
            public override void Migrate(ref PrefabMigrationContext context)
            {
                context.NewLength = 0;
            }
        }

        [Test]
        public void DerivedClass_ImplementsAbstractMembers()
        {
            var step = new ConcreteStep();
            Assert.That(step.OldPrefabSchemaHash, Is.EqualTo(0xAAAAUL));
            Assert.That(step.NewPrefabSchemaHash, Is.EqualTo(0xBBBBUL));
        }

        [Test]
        public void Migrate_InvokesWithoutThrowing()
        {
            var step = new ConcreteStep();
            var ctx = new PrefabMigrationContext
            {
                OldData = new byte[0],
                NewBuffer = new byte[0],
                NewLength = 0,
            };

            // ref struct のため Assert.DoesNotThrow(ラムダ) は使えない。
            // 直接呼び出しで例外が発生しなければ成功。
            step.Migrate(ref ctx);

            Assert.That(ctx.NewLength, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public sealed class IPrefabMigrationTests
    {
        private sealed class UserMigration : IPrefabMigration
        {
            public ulong OldPrefabSchemaHash => 1;
            public ulong NewPrefabSchemaHash => 2;
            public void Migrate(ref PrefabMigrationContext context) { }
        }

        [Test]
        public void InterfaceCanBeImplemented()
        {
            IPrefabMigration m = new UserMigration();
            Assert.That(m.OldPrefabSchemaHash, Is.EqualTo(1UL));
            Assert.That(m.NewPrefabSchemaHash, Is.EqualTo(2UL));
        }
    }
}
