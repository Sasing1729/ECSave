using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using NUnit.Framework;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// 未登録コンポーネント。どのモジュールにもマッチしない Entity を作るために使う。
    /// </summary>
    public struct UnregisteredProbe : IComponentData
    {
        public int Value;
    }

    /// <summary>
    /// Selection なし。ReferenceHolder.Target の pid は UnresolvedRef になる。
    /// </summary>
    public sealed class ReferenceHolderNoSelectionScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<ReferenceHolder>();
            b.WorldId("SelectionDiscriminationWorld");
        }
    }

    /// <summary>
    /// Selection あり。Target が _idOf に載り、pid が書かれる。
    /// </summary>
    public sealed class ReferenceHolderWithSelectionScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<ReferenceHolder>();
            b.SelectEntityReference<ReferenceHolder>(r => r.Target);
            b.WorldId("SelectionDiscriminationWorld");
        }
    }

    [TestFixture]
    public sealed class SelectionDiscriminationTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("SelectionDiscriminationWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        // ── 生データ検査ヘルパ ────────────────────────────────

        /// <summary>
        /// Serialize 済みデータを展開し、ReferenceHolder セクションの
        /// 最初のレコードの Target フィールドを u32 で返す。
        /// セクションが見つからなければ -1。
        /// </summary>
        private static unsafe uint ReadReferenceHolderTargetPid(byte[] serialized)
        {
            var err = SaveCompression.TryDecompress(serialized, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);

            fixed (byte* p = raw)
            {
                var view = new ByteView(p, raw.Length);
                var reader = new FormatReader(view);
                reader.ReadHeader();
                var table = reader.ReadSectionTable();

                ulong targetNameHash = SchemaHashCore.HashUtf8(
                    typeof(ReferenceHolder).FullName);

                for (int i = 0; i < table.Count; i++)
                {
                    ref var e = ref table.Entries[i];
                    if (e.NameHash != targetNameHash) continue;

                    long offset = (long)e.Offset;
                    long length = (long)e.Length;
                    var sectionView = new ByteView(p + offset, (int)length);

                    // 型記述子ブロックをスキップ（SectionBodyHelper.SliceBody と同じ処理）
                    var bodyReader = new ByteReader(sectionView);
                    uint tdSize = bodyReader.ReadU32();
                    var bodyView = sectionView.Slice(
                        (int)tdSize, sectionView.Length - (int)tdSize);

                    var v = CommonSectionReader.Read(bodyView);
                    Assert.GreaterOrEqual((int)v.Header.Count, 1,
                        "ReferenceHolder section should have at least one record");

                    // dataStride レイアウトの先頭 4B が Target の pid
                    uint pid = *(uint*)v.Records;
                    return pid;
                }
            }
            Assert.Fail("ReferenceHolder section not found");
            return 0xFFFFFFFF;
        }

        private byte[] SerializeWith(SaveScope scope, out Entity holderEntity)
        {
            var sys = scope.GetSaveSystem();
            var em = _world.EntityManager;

            var referenced = em.CreateEntity();
            em.AddComponentData(referenced, new UnregisteredProbe { Value = 42 });

            holderEntity = em.CreateEntity();
            em.AddComponentData(holderEntity, new ReferenceHolder
            {
                Target = referenced,
                Value = 7,
            });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");
            return save.Data;
        }

        // ── 生データ検査 ──────────────────────────────────────

        [Test]
        public void WithoutSelection_UnresolvedRefWritten()
        {
            var scope = ScriptableObject.CreateInstance<ReferenceHolderNoSelectionScope>();
            try
            {
                var data = SerializeWith(scope, out _);
                uint pid = ReadReferenceHolderTargetPid(data);

                Assert.AreEqual(EntityRefConstants.UnresolvedRef, pid,
                    "Without Selection, Target should be encoded as UnresolvedRef (0xFFFFFFFE)");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void WithSelection_PidWritten()
        {
            var scope = ScriptableObject.CreateInstance<ReferenceHolderWithSelectionScope>();
            try
            {
                var data = SerializeWith(scope, out _);
                uint pid = ReadReferenceHolderTargetPid(data);

                Assert.AreNotEqual(EntityRefConstants.UnresolvedRef, pid,
                    "With Selection, Target should be encoded as a valid pid");
                Assert.AreNotEqual(EntityRefConstants.NullRef, pid,
                    "With Selection, Target should not be encoded as NullRef");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        // ── ロード時の警告 ────────────────────────────────────

        [Test]
        public void WithoutSelection_LoadEmitsUnresolvedWarning()
        {
            var scope = ScriptableObject.CreateInstance<ReferenceHolderNoSelectionScope>();
            try
            {
                var data = SerializeWith(scope, out _);

                _world.Dispose();
                _world = new World("SelectionDiscriminationWorld");

                var sys = scope.GetSaveSystem();
                var load = sys.DeserializeAsync(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.IsTrue(load.IsSuccess, $"Load: {load.ErrorCode}");

                bool hasUnresolved = false;
                for (int i = 0; i < load.Warnings.Count; i++)
                    if (load.Warnings[i].Kind == LoadWarningKind.EntityReferenceUnresolved)
                        hasUnresolved = true;

                Assert.IsTrue(hasUnresolved,
                    "Without Selection, EntityReferenceUnresolved warning must be emitted");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void WithSelection_LoadDoesNotEmitUnresolvedWarning()
        {
            var scope = ScriptableObject.CreateInstance<ReferenceHolderWithSelectionScope>();
            try
            {
                var data = SerializeWith(scope, out _);

                _world.Dispose();
                _world = new World("SelectionDiscriminationWorld");

                var sys = scope.GetSaveSystem();
                var load = sys.DeserializeAsync(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.IsTrue(load.IsSuccess, $"Load: {load.ErrorCode}");

                bool hasUnresolved = false;
                for (int i = 0; i < load.Warnings.Count; i++)
                    if (load.Warnings[i].Kind == LoadWarningKind.EntityReferenceUnresolved)
                        hasUnresolved = true;

                Assert.IsFalse(hasUnresolved,
                    "With Selection, EntityReferenceUnresolved warning must NOT be emitted " +
                    "(Target pid is written)");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        // ── 既存テストとの整合（Selection なしでも保存されるケース） ──

        [Test]
        public void WithoutSelection_ReferencedEntityWithHealth_StillResolved()
        {
            // 参照先が Health を持つ場合、Selection なしでも _idOf に載るため
            // UnresolvedRef にならない。これは Selection の有無を弁別できないケース。
            var sys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthModule(), new ReferenceHolderModule() },
                1, "SelectionDiscriminationWorld"));

            var em = _world.EntityManager;
            var referenced = em.CreateEntity();
            em.AddComponentData(referenced, new Health { Current = 42, Max = 100 });

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new ReferenceHolder
            {
                Target = referenced,
                Value = 7,
            });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            uint pid = ReadReferenceHolderTargetPid(save.Data);
            Assert.AreNotEqual(EntityRefConstants.UnresolvedRef, pid,
                "Referenced entity with a registered component is on _idOf " +
                "even without Selection");
        }

        // ReferenceHolderModule は既存（Tests/MinimalE2E/ReferenceHolderModule.cs）
    }
}
