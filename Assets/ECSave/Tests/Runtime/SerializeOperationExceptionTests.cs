using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;
using FormatException = ECSave.Format.FormatException;
using FormatError = ECSave.Format.FormatError;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// ダミー Component。BuildQuery の型指定のみに使用され、
    /// 実際の Entity には付与されない。
    /// </summary>
    public struct DummyComponent : IComponentData { }

    /// <summary>
    /// Capture / ComputeBodySize / WriteSectionBody で例外を投げるモジュール。
    /// 例外注入点をフィールドで切り替える。
    /// </summary>
    internal sealed class ThrowingModule : SaveTypeModule
    {
        public enum ThrowAt { None, Capture, ComputeBodySize, WriteSectionBody }

        public ThrowAt Location = ThrowAt.None;
        public bool ThrowFormatException;

        public override ulong SchemaHash => 0x1234567890ABCDEFUL;
        public override string FullTypeName => "Test.ThrowingModule";
        public override SectionKind SectionKind => SectionKind.Component;
        public override int RawRecordStride => 4;
        public override int RecordStride => 4;
        public override int[] EntityRefOffsets => ModuleShared.EmptyInts;
        public override byte[] SchemaHashInput => new byte[] { 0 };
        public override string[] FieldNames => new[] { "Dummy" };
        public override bool HasEnabledBits => false;

        public override EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<DummyComponent>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        public override void Capture(EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            if (Location == ThrowAt.Capture) Throw();
        }

        public override int ComputeBodySize(in SnapshotBuffer buffer)
        {
            if (Location == ThrowAt.ComputeBodySize) Throw();
            return 12;
        }

        public override void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            if (Location == ThrowAt.WriteSectionBody) Throw();
            writer.WriteU32(0);
            writer.WriteU32(0);
            writer.WriteU32(0);
        }

        public override void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
        }

        public override void RemoveFromEntity(Entity entity, EntityManager em) { }

        public override ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator)
            => default;

        public override void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em)
        { }

        private void Throw()
        {
            if (ThrowFormatException)
                throw new FormatException(FormatError.CorruptedData, "injected format");
            throw new InvalidOperationException("injected");
        }
    }

    [TestFixture]
    public sealed class SerializeOperationExceptionTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("SerializeOpExcWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static SaveDefinition MakeDef(ThrowingModule module)
        {
            return new SaveDefinition(
                new[] { (SaveTypeModule)module },
                gameDataVersion: 1,
                worldId: "SerializeOpExcWorld");
        }

        [Test]
        public void Capture_Throws_InvalidOperation_ReturnsSerializationFailed()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var mod = new ThrowingModule { Location = ThrowingModule.ThrowAt.Capture };
                var sys = new SerializeOperation(MakeDef(mod), _world);

                var result = sys.Execute(default);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.SerializationFailed));
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void Capture_Throws_Format_ReturnsMappedErrorCode()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var mod = new ThrowingModule
                {
                    Location = ThrowingModule.ThrowAt.Capture,
                    ThrowFormatException = true,
                };
                var sys = new SerializeOperation(MakeDef(mod), _world);

                var result = sys.Execute(default);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void ComputeBodySize_Throws_ReturnsSerializationFailed()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var mod = new ThrowingModule { Location = ThrowingModule.ThrowAt.ComputeBodySize };
                var sys = new SerializeOperation(MakeDef(mod), _world);

                var result = sys.Execute(default);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.SerializationFailed));
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void WriteSectionBody_Throws_ReturnsSerializationFailed()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var mod = new ThrowingModule { Location = ThrowingModule.ThrowAt.WriteSectionBody };
                var sys = new SerializeOperation(MakeDef(mod), _world);

                var result = sys.Execute(default);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.SerializationFailed));
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void MoveNext_AfterDone_ReturnsTrueImmediately()
        {
            var mod = new ThrowingModule();
            var sys = new SerializeOperation(MakeDef(mod), _world);

            try
            {
                // 1回目: Stage.Start → DoScheduleAsync（同期 Capture、Async 未使用）
                Assert.That(sys.MoveNext(default), Is.False);
                // 2回目: Stage.AsyncScheduled → DoCompleteAsyncAndFilter
                Assert.That(sys.MoveNext(default), Is.False);
                // 3回目: Stage.W1W2Done → DoEncodeAndSeal
                Assert.That(sys.MoveNext(default), Is.True);
                // 4回目: Stage.Done → 即 true
                Assert.That(sys.MoveNext(default), Is.True);
                Assert.That(sys.IsComplete, Is.True);
            }
            finally
            {
                sys.Dispose();
            }
        }

        [Test]
        public void MoveNext_CancelledBetweenStages_ReturnsCancelled()
        {
            var mod = new ThrowingModule();
            var sys = new SerializeOperation(MakeDef(mod), _world);

            try
            {
                using var cts = new CancellationTokenSource();

                // 1回目: W1/W2
                Assert.That(sys.MoveNext(cts.Token), Is.False);

                // 2回目: キャンセル済みトークンで Encode/Seal
                cts.Cancel();
                Assert.That(sys.MoveNext(cts.Token), Is.True);
                Assert.That(sys.Result.IsSuccess, Is.False);
                Assert.That(sys.Result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.Cancelled));
            }
            finally
            {
                sys.Dispose();
            }
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var mod = new ThrowingModule();
            var sys = new SerializeOperation(MakeDef(mod), _world);
            sys.MoveNext(default);
            sys.MoveNext(default);

            Assert.DoesNotThrow(() => sys.Dispose());
            Assert.DoesNotThrow(() => sys.Dispose());
            Assert.DoesNotThrow(() => sys.Dispose());
        }

        [Test]
        public void Execute_AfterDispose_StillRuns()
        {
            var mod = new ThrowingModule();
            var sys = new SerializeOperation(MakeDef(mod), _world);

            var result = sys.Execute(default);

            // 成功 or 失敗は問わない。Dispose が Execute 後に呼ばれても
            // 二重解放例外が出ないことを確認
            Assert.DoesNotThrow(() => sys.Dispose());
        }
    }
}
