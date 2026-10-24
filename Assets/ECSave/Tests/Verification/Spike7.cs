using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    // ─────────────────────────────────────────────
    // Spike 7 用の型
    // ─────────────────────────────────────────────
    public struct Spike7ChunkData : IComponentData
    {
        public int Value;
    }

    public struct Spike7Tag : IComponentData { }

    public struct Spike7UpdateTag : IComponentData { }

    /// <summary>
    /// 復元領域外 Entity を復元対象と別 Archetype（別 Chunk）にするためのマーカー。
    /// 同一 Archetype の場合、ChunkComponent は Chunk 単位で共有されるため
    /// 復元側の書込みが副作用として漏れる（これは ECS の仕様）。
    /// </summary>
    public struct Spike7OutsideMarker : IComponentData { }

    /// <summary>
    /// Spike 7: 実際の ECSave 復元経路を模擬し、
    /// 復元領域外 Entity の Chunk 所属・Chunk Component 値が変化するかを検証する。
    ///
    /// 判明した ECS の仕様:
    ///   - ChunkComponent は「Chunk 単位の値」である
    ///   - 復元領域外の Entity が復元対象と同一 Chunk に居ると、復元側の書込みで
    ///     その値も上書きされる（検証 2 で明示的に記録）
    ///   - 別 Archetype（別 Chunk）であれば復元処理の影響を受けない（検証 1/3/4/5）
    ///
    /// 注意:
    ///   SimulateP6 は本物の ECSave P6 (P6_ChunkComponentRestorePhase) の挙動を
    ///   模擬する。実装側の ApplyChunks は「復元対象 Entity が所属する Chunk のみ」に
    ///   書き込み、復元対象外の Chunk には触らない。よって SimulateP6 も
    ///   targets の Chunk のみを対象にする必要がある。
    /// </summary>
    [TestFixture]
    public class Spike7_実復元経路での外部Entity影響
    {
        private World _world;
        private EntityManager _em;
        private EntityArchetype _outsideArchetype;
        private EntityArchetype _targetArchetype;

        [SetUp]
        public void SetUp()
        {
            _world = new World("Spike7World");
            _em = _world.EntityManager;

            // 復元領域外 Entity は専用マーカー付きアーキタイプにして、
            // 復元対象と「別 Chunk」に配置されるようにする。
            _outsideArchetype = _em.CreateArchetype(
                typeof(Spike7Tag),
                typeof(Spike7UpdateTag),
                typeof(Spike7OutsideMarker),
                ComponentType.ChunkComponent<Spike7ChunkData>());

            // 復元対象用のアーキタイプ
            _targetArchetype = _em.CreateArchetype(
                typeof(Spike7Tag),
                typeof(Spike7UpdateTag),
                ComponentType.ChunkComponent<Spike7ChunkData>());
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated)
                _world.Dispose();
        }

        // ─────────────────────────────────────────────
        // ヘルパー
        // ─────────────────────────────────────────────

        private Entity CreateOutsideEntity(int chunkValue)
        {
            var entity = _em.CreateEntity(_outsideArchetype);
            _em.SetChunkComponentData(_em.GetChunk(entity),
                new Spike7ChunkData { Value = chunkValue });
            return entity;
        }

        private Entity CreateTargetEntity(int chunkValue)
        {
            var entity = _em.CreateEntity(_targetArchetype);
            _em.SetChunkComponentData(_em.GetChunk(entity),
                new Spike7ChunkData { Value = chunkValue });
            return entity;
        }

        private int ReadValue(Entity entity)
        {
            return _em.GetChunkComponentData<Spike7ChunkData>(_em.GetChunk(entity)).Value;
        }

        private bool SameChunk(Entity a, Entity b)
        {
            return _em.GetChunk(a).Equals(_em.GetChunk(b));
        }

        private void SimulateP4StructureChange(NativeArray<Entity> targets)
        {
            _em.RemoveComponent<Spike7UpdateTag>(targets);
        }

        private void SimulateP5ValueWrite(NativeArray<Entity> targets, int value)
        {
            foreach (var t in targets)
            {
                var chunk = _em.GetChunk(t);
                _em.SetChunkComponentData(chunk, new Spike7ChunkData { Value = value });
            }
        }

        // ★ 修正: 本物の ECSave P6 は「復元対象 Entity が所属する Chunk のみ」に
        //   書き込む。旧実装は Spike7Tag + Spike7ChunkData を持つ全 Chunk に
        //   書いてしまい、復元対象外の Chunk にも副作用が漏れていた。
        //   実際の ApplyChunks は groupTotal == 0 の Chunk をスキップする。
        private void SimulateP6ChunkComponentRestore(NativeArray<Entity> targets, int value)
        {
            foreach (var t in targets)
            {
                if (!_em.Exists(t)) continue;
                var chunk = _em.GetChunk(t);
                if (chunk.HasChunkComponent<Spike7ChunkData>())
                {
                    _em.SetChunkComponentData(chunk, new Spike7ChunkData { Value = value });
                }
            }
        }

        private void SimulateP7Destroy(NativeArray<Entity> targets)
        {
            _em.DestroyEntity(targets);
        }

        // ─────────────────────────────────────────────
        // 検証 1: 復元領域外 Entity が単独 Chunk の場合
        // ─────────────────────────────────────────────
        [Test]
        public void 検証1_復元領域外Entityが単独Chunkの場合()
        {
            var outside = CreateOutsideEntity(chunkValue: 999);
            var outsideChunkBefore = _em.GetChunk(outside);

            Debug.Log($"[7-1] 初期: outside={ReadValue(outside)}, " +
                      $"Chunk={outsideChunkBefore.GetHashCode()}");

            var target = CreateTargetEntity(chunkValue: 42);
            Debug.Log($"[7-1] target 作成後: outside と target の同一 Chunk? {SameChunk(outside, target)}");

            Assert.IsFalse(SameChunk(outside, target),
                "outside は専用マーカー付きアーキタイプなので target と別 Chunk であるべき（前提確認）");

            var targets = new NativeArray<Entity>(1, Allocator.Temp);
            try
            {
                targets[0] = target;
                SimulateP4StructureChange(targets);
                SimulateP5ValueWrite(targets, 42);
                SimulateP6ChunkComponentRestore(targets, 42);
            }
            finally
            {
                targets.Dispose();
            }

            var outsideChunkAfter = _em.GetChunk(outside);
            Debug.Log($"[7-1] 処理後: outside={ReadValue(outside)}, " +
                      $"Chunk={outsideChunkAfter.GetHashCode()}");

            Assert.IsTrue(_em.Exists(outside));
            Assert.AreEqual(999, ReadValue(outside),
                "別 Chunk の復元領域外 Entity の ChunkComponent 値は変化してはならない");
            Assert.AreEqual(outsideChunkBefore, outsideChunkAfter,
                "別 Chunk の復元領域外 Entity の Chunk 所属は変化してはならない");
        }

        // ─────────────────────────────────────────────
        // 検証 2: 復元領域外 Entity が復元対象と同一 Chunk の場合
        //
        // このテストは「ChunkComponent が Chunk 単位で共有される」という
        // ECS の仕様を記録する。ECSave 側で復元対象と同じ Archetype の Entity を
        // 復元領域外に置くケースは、P5 で副作用を受けることを前提に設計する必要がある。
        // ─────────────────────────────────────────────
        [Test]
        public void 検証2_復元領域外Entityが復元対象と同一Chunkの場合()
        {
            // 意図的に target と同じアーキタイプで outside を作る（同一 Chunk）
            var outside = CreateTargetEntity(chunkValue: 999);
            var target = CreateTargetEntity(chunkValue: 42);

            var sameBefore = SameChunk(outside, target);
            Debug.Log($"[7-2] 初期: outside={ReadValue(outside)}, target={ReadValue(target)}");
            Debug.Log($"[7-2] 初期 同一 Chunk? {sameBefore}");

            // 前提確認: 同一アーキタイプなので同一 Chunk
            Assert.IsTrue(sameBefore,
                "同一アーキタイプの Entity は同一 Chunk に共存する（前提確認）");

            var outsideChunkBefore = _em.GetChunk(outside);

            var targets = new NativeArray<Entity>(1, Allocator.Temp);
            try
            {
                targets[0] = target;
                SimulateP5ValueWrite(targets, 42);
            }
            finally
            {
                targets.Dispose();
            }

            Debug.Log($"[7-2] P5 後: outside={ReadValue(outside)}, target={ReadValue(target)}");

            // ★ ECS の仕様: ChunkComponent は Chunk 単位の値なので、
            //   同一 Chunk 上の outside も target への書込みで上書きされる。
            Assert.AreEqual(42, ReadValue(outside),
                "ChunkComponent は Chunk 単位の値なので、同一 Chunk 上の他 Entity にも" +
                "副作用が及ぶ（ECS の仕様）。" +
                "ECSave 実装では P4/P5 で復元領域外 Entity を別 Chunk に分けるか、" +
                "ChunkComponent 復元時の衝突を許容するかを決める必要がある");
            Assert.IsTrue(SameChunk(outside, target),
                "P5 の値書き込みは Chunk 所属を変化させない");
            Assert.AreEqual(outsideChunkBefore, _em.GetChunk(outside));
        }

        // ─────────────────────────────────────────────
        // 検証 3: 破棄（P7）が復元領域外 Entity に与える影響
        // ─────────────────────────────────────────────
        [Test]
        public void 検証3_破棄が復元領域外Entityに与える影響()
        {
            var outside = CreateOutsideEntity(chunkValue: 999);
            var outsideChunkBefore = _em.GetChunk(outside);

            var targets = new NativeArray<Entity>(3, Allocator.Temp);
            try
            {
                for (int i = 0; i < 3; i++)
                    targets[i] = CreateTargetEntity(chunkValue: i);

                Debug.Log($"[7-3] 破棄前: outside={ReadValue(outside)}, " +
                          $"outside の Chunk={outsideChunkBefore.GetHashCode()}");

                SimulateP7Destroy(targets);

                var outsideChunkAfter = _em.GetChunk(outside);
                Debug.Log($"[7-3] 破棄後: outside={ReadValue(outside)}, " +
                          $"outside の Chunk={outsideChunkAfter.GetHashCode()}");

                Assert.IsTrue(_em.Exists(outside),
                    "復元領域外 Entity は破棄されてはならない");
                Assert.AreEqual(999, ReadValue(outside),
                    "別 Chunk の復元領域外 Entity の ChunkComponent 値は変化してはならない");
                Assert.AreEqual(outsideChunkBefore, outsideChunkAfter,
                    "別 Chunk の復元領域外 Entity の Chunk 所属は変化してはならない");
            }
            finally
            {
                targets.Dispose();
            }
        }

        // ─────────────────────────────────────────────
        // 検証 4: 構造変更（P4）が復元領域外 Entity に与える影響
        // ─────────────────────────────────────────────
        [Test]
        public void 検証4_構造変更が復元領域外Entityに与える影響()
        {
            var outside = CreateOutsideEntity(chunkValue: 999);
            var outsideChunkBefore = _em.GetChunk(outside);

            var targets = new NativeArray<Entity>(5, Allocator.Temp);
            try
            {
                for (int i = 0; i < 5; i++)
                    targets[i] = CreateTargetEntity(chunkValue: i);

                Debug.Log($"[7-4] P4 前: outside={ReadValue(outside)}");

                SimulateP4StructureChange(targets);

                var outsideChunkAfter = _em.GetChunk(outside);
                Debug.Log($"[7-4] P4 後: outside={ReadValue(outside)}");

                Assert.IsTrue(_em.Exists(outside));
                Assert.AreEqual(999, ReadValue(outside),
                    "別 Chunk の復元領域外 Entity の ChunkComponent 値は P4 で変化してはならない");
                Assert.AreEqual(outsideChunkBefore, outsideChunkAfter,
                    "別 Chunk の復元領域外 Entity の Chunk 所属は P4 で変化してはならない");
            }
            finally
            {
                targets.Dispose();
            }
        }

        // ─────────────────────────────────────────────
        // 検証 5: 実復元経路全体（P3〜P7 相当）
        // ─────────────────────────────────────────────
        [Test]
        public void 検証5_実復元経路全体()
        {
            var outside = CreateOutsideEntity(chunkValue: 999);
            var outsideChunkBefore = _em.GetChunk(outside);

            const int N = 100;
            var targets = new NativeArray<Entity>(N, Allocator.Temp);
            var destroyTargets = new NativeArray<Entity>(N / 2, Allocator.Temp);
            try
            {
                for (int i = 0; i < N; i++)
                    targets[i] = CreateTargetEntity(chunkValue: i);

                Debug.Log($"[7-5] 初期: outside={ReadValue(outside)}");

                SimulateP4StructureChange(targets);
                SimulateP5ValueWrite(targets, 42);
                SimulateP6ChunkComponentRestore(targets, 42);

                for (int i = 0; i < N / 2; i++)
                    destroyTargets[i] = targets[i];
                SimulateP7Destroy(destroyTargets);

                var outsideChunkAfter = _em.GetChunk(outside);
                Debug.Log($"[7-5] 処理後: outside={ReadValue(outside)}");

                Assert.IsTrue(_em.Exists(outside),
                    "復元領域外 Entity は一連の復元処理後も存在しなければならない");
                Assert.AreEqual(999, ReadValue(outside),
                    "別 Chunk の復元領域外 Entity の ChunkComponent 値は変化してはならない");
                Assert.AreEqual(outsideChunkBefore, outsideChunkAfter,
                    "別 Chunk の復元領域外 Entity の Chunk 所属は変化してはならない");
            }
            finally
            {
                targets.Dispose();
                destroyTargets.Dispose();
            }
        }
    }
}
