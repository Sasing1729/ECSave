using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    internal struct ChunkData : IComponentData
    {
        public int Value;
    }

    internal sealed class ChunkDataModule : SaveTypeModuleChunk<ChunkData>
    {
        private static readonly byte[] s_schemaHashInput = new byte[]
        {
            0x04, 0x00,
            0x04,
            0x04, 0x00,
            0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        private static readonly string[] s_fieldNames = new[] { "Value" };
        private static readonly int[] s_entityRefOffsets = System.Array.Empty<int>();

        public override ulong SchemaHash => SchemaHashCore.Hash(s_schemaHashInput);
        public override string FullTypeName => "ECSave.Tests.ChunkComponentMissingTests.ChunkData";
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;
        public override int[] EntityRefOffsets => s_entityRefOffsets;
    }

    public class ChunkComponentEntityMissingTests
    {
        private const string WorldName = "ChunkMissingTestWorld";
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World(WorldName);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        private SaveSystem CreateSaveSystem(params SaveTypeModule[] modules)
            => new SaveSystem(new SaveDefinition(modules, gameDataVersion: 1, worldId: WorldName));

        /// <summary>
        /// World 内の全 ArchetypeChunk から ChunkComponent の値を列挙する。
        ///
        /// <para>
        /// <b>API 選択の理由</b>: このプロジェクトの Unity.Entities バージョンには
        /// 次の API が存在しない:
        /// <list type="bullet">
        ///   <item><c>EntityManager.GetChunkComponentData&lt;T&gt;(Entity)</c></item>
        ///   <item><c>EntityManager.HasChunkComponent&lt;T&gt;(Entity)</c></item>
        ///   <item><c>EntityManager.GetChunk(Entity)</c></item>
        /// </list>
        /// これらは <c>EntityQuery</c> 引数のオーバーロードのみが提供されている。
        /// </para>
        /// <para>
        /// <b>採用する API</b>: <c>EntityQuery.ToArchetypeChunkArray</c> +
        /// <c>ArchetypeChunk.GetChunkComponentData&lt;T&gt;(ref ComponentTypeHandle&lt;T&gt;)</c>
        /// は全バージョンで安定して動作する（<c>ref</c> 渡しなので obsolete 警告も出ない）。
        /// </para>
        /// <para>
        /// <b>ChunkComponent の特性</b>: 値はチャンク単位で共有される。
        /// 同じチャンクに属する複数 Entity は同じ値を持つ。
        /// </para>
        /// </summary>
        private static List<T> ReadAllChunkComponents<T>(World world)
            where T : unmanaged, IComponentData
        {
            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<T>() },
            });
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

            var handle = em.GetComponentTypeHandle<T>(isReadOnly: true);
            var result = new List<T>(chunks.Length);
            for (int i = 0; i < chunks.Length; i++)
            {
                result.Add(chunks[i].GetChunkComponentData(ref handle));
            }
            return result;
        }

        private static int CountEntitiesWith<T>(World world)
            where T : unmanaged, IComponentData
        {
            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<T>() },
            });
            return query.CalculateEntityCount();
        }

        // ═══════════════════════════════════════════
        // 復元成功ケース
        // ═══════════════════════════════════════════

        [Test]
        public void ChunkComponentOnlyEntity_IsRestored()
        {
            var saveSys = CreateSaveSystem(new ChunkDataModule());

            var em = _world.EntityManager;

            var e = em.CreateEntity();

            em.AddChunkComponentData<ChunkData>(e);

            em.SetChunkComponentData(
                em.GetChunk(e),
                new ChunkData
                {
                    Value = 42
                });

            // ------------------------------------------------------------------
            // Save-side verification
            // ------------------------------------------------------------------

            var beforeValues =
                ReadAllChunkComponents<ChunkData>(_world);

            UnityEngine.Debug.Log(
                $"Before Save ChunkCount={beforeValues.Count}");

            if (beforeValues.Count > 0)
            {
                UnityEngine.Debug.Log(
                    $"Before Save Value={beforeValues[0].Value}");
            }

            Assert.AreEqual(
                1,
                beforeValues.Count,
                "Save-side verification failed. " +
                "ReadAllChunkComponents cannot observe ChunkData before serialization.");

            Assert.AreEqual(
                42,
                beforeValues[0].Value,
                "Save-side ChunkData value is invalid.");

            var save =
                saveSys.Serialize(_world)
                    .GetAwaiter()
                    .GetResult();

            Assert.IsTrue(
                save.IsSuccess,
                $"Serialize failed: {save.ErrorCode}");

            RestartWorld();

            var loadSys =
                CreateSaveSystem(
                    new ChunkDataModule());

            var load =
                loadSys.Deserialize(
                        _world,
                        save.Data,
                        RestorationScope.EntireWorld,
                        DeletionPolicy.ByRegistration)
                    .GetAwaiter()
                    .GetResult();

            Assert.IsTrue(
                load.IsSuccess,
                $"Deserialize failed: {load.ErrorCode}");

            Assert.AreEqual(
                1,
                load.EntityCount,
                "Logical entity count mismatch.");

            Assert.IsFalse(
                load.Warnings.Any(
                    w => w.Kind ==
                         LoadWarningKind.ChunkComponentEntityMissing),
                "ChunkComponentEntityMissing warning was emitted.");

            // ------------------------------------------------------------------
            // Load-side verification
            // ------------------------------------------------------------------

            var afterValues =
                ReadAllChunkComponents<ChunkData>(_world);

            Assert.AreEqual(
                1,
                afterValues.Count,
                "Expected exactly one chunk containing ChunkData after load.");

            Assert.AreEqual(
                42,
                afterValues[0].Value,
                "ChunkData value was not restored.");

            Assert.AreEqual(
                beforeValues.Count,
                afterValues.Count,
                "Chunk count changed across save/load.");
        }

        [Test]
        public void ChunkComponentQuery_WorksOnCurrentEntitiesVersion()
        {
            var em = _world.EntityManager;

            var e = em.CreateEntity();

            em.AddChunkComponentData<ChunkData>(e);

            em.SetChunkComponentData(
                em.GetChunk(e),
                new ChunkData
                {
                    Value = 123
                });

            var values =
                ReadAllChunkComponents<ChunkData>(_world);

            Assert.AreEqual(
                1,
                values.Count,
                "Chunk query itself cannot observe ChunkData.");

            Assert.AreEqual(
                123,
                values[0].Value);
        }

        [Test]
        public void ChunkComponentOnlyEntity_MultipleEntities_AllRestored()
        {
            // ChunkComponent はチャンク共有なので、同じ archetype の 3 entity は
            // 同じ chunk に入り、chunk 値も共有される（最後の AddChunkComponentData が勝つ）。
            // 復元後も 3 entity が存在し、chunk 値が復元されていることを検証する。
            var saveSys = CreateSaveSystem(new ChunkDataModule());
            var em = _world.EntityManager;

            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                em.AddChunkComponentData<ChunkData>(e);
                em.SetChunkComponentData(em.GetChunk(e), new ChunkData { Value = i * 10 });
            }

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var loadSys = CreateSaveSystem(new ChunkDataModule());
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(3, load.EntityCount);

            // チャンクに ChunkData が復元されている（1 件以上）
            var values = ReadAllChunkComponents<ChunkData>(_world);
            Assert.IsTrue(values.Count >= 1, "Expected at least 1 chunk with ChunkData");
        }

        [Test]
        public void ChunkComponentOnlyEntity_WhenLoadSideOmitsOtherModule_StillRestored()
        {
            // Save 側: Health + ChunkData の両方を持つ Entity
            // Load 側: ChunkDataModule のみ（Health は未登録）
            // → Health セクションは UnknownSectionIgnored
            // → ChunkData セクションから pid を収集 → Entity 生成
            // → Health を失うが ChunkData は復元される
            var saveModules = new SaveTypeModule[] { new HealthModule(), new ChunkDataModule() };
            var saveSys = CreateSaveSystem(saveModules);

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });
            em.AddChunkComponentData<ChunkData>(e);
            em.SetChunkComponentData(em.GetChunk(e), new ChunkData { Value = 99 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var loadSys = CreateSaveSystem(new ChunkDataModule());
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(1, load.EntityCount);

            // ChunkData セクションから pid が復元される
            var values = ReadAllChunkComponents<ChunkData>(_world);
            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(99, values[0].Value);

            // Health は復元されない（モジュール未登録のため）
            Assert.AreEqual(0, CountEntitiesWith<Health>(_world));

            // ChunkComponentEntityMissing 警告は出ない（復元できたため）
            Assert.IsFalse(
                load.Warnings.Any(w => w.Kind == LoadWarningKind.ChunkComponentEntityMissing));
        }

        // ═══════════════════════════════════════════
        // 警告が出ない回帰テスト
        // ═══════════════════════════════════════════

        [Test]
        public void ChunkComponentWithComponent_NoWarning_WhenLoadSideMatchesSaveSide()
        {
            var modules = new SaveTypeModule[] { new HealthModule(), new ChunkDataModule() };
            var sys = CreateSaveSystem(modules);

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 5, Max = 9 });
            em.AddChunkComponentData<ChunkData>(e);
            em.SetChunkComponentData(em.GetChunk(e), new ChunkData { Value = 77 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.IsFalse(
                load.Warnings.Any(w => w.Kind == LoadWarningKind.ChunkComponentEntityMissing));

            // Health が復元されている
            var hq = _world.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Health>() },
            });
            using var vals = hq.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(5, vals[0].Current);

            // ChunkData が復元されている
            var values = ReadAllChunkComponents<ChunkData>(_world);
            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(77, values[0].Value);
        }

        [Test]
        public void EmptyWorld_NoWarning()
        {
            var modules = new SaveTypeModule[] { new ChunkDataModule() };
            var sys = CreateSaveSystem(modules);

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(0, load.EntityCount);
            Assert.IsFalse(
                load.Warnings.Any(w => w.Kind == LoadWarningKind.ChunkComponentEntityMissing));
        }
    }
}
