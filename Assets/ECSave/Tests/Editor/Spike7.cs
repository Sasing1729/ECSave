using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    // ─────────────────────────────────────────────
    // Spike 7 用の型（Spike7b の型とは別名）
    // ─────────────────────────────────────────────
    public struct Spike7ChunkData : IComponentData
    {
        public int Value;
    }

    public struct Spike7Tag : IComponentData { }

    public struct Spike7UpdateTag : IComponentData { }

    /// <summary>
    /// Spike 7: 実際の ECSave 復元経路を模擬し、
    /// 復元領域外 Entity の Chunk 所属・Chunk Component 値が変化するかを検証する。
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

            _outsideArchetype = _em.CreateArchetype(
                typeof(Spike7Tag),
                typeof(Spike7UpdateTag),
                ComponentType.ChunkComponent<Spike7ChunkData>());

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

        private void SimulateP6ChunkComponentRestore(int value)
        {
            var query = _em.CreateEntityQuery(typeof(Spike7Tag));
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                foreach (var chunk in chunks)
                {
                    if (chunk.HasChunkComponent<Spike7ChunkData>())
                    {
                        _em.SetChunkComponentData(chunk, new Spike7ChunkData { Value = value });
                    }
                }
            }
            finally
            {
                chunks.Dispose();
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

            var targets = new NativeArray<Entity>(1, Allocator.Temp);
            try
            {
                targets[0] = target;
                SimulateP4StructureChange(targets);
                SimulateP5ValueWrite(targets, 42);
                SimulateP6ChunkComponentRestore(42);
            }
            finally
            {
                targets.Dispose();
            }

            var outsideChunkAfter = _em.GetChunk(outside);
            Debug.Log($"[7-1] 処理後: outside={ReadValue(outside)}, " +
                      $"Chunk={outsideChunkAfter.GetHashCode()}");
            Debug.Log($"[7-1] Chunk 変化? {!outsideChunkBefore.Equals(outsideChunkAfter)}");
            Debug.Log($"[7-1] 期待: outside=999 のまま、Chunk 変化なし");
        }

        // ─────────────────────────────────────────────
        // 検証 2: 復元領域外 Entity が復元対象と同一 Chunk の場合
        // ─────────────────────────────────────────────
        [Test]
        public void 検証2_復元領域外Entityが復元対象と同一Chunkの場合()
        {
            var outside = CreateOutsideEntity(chunkValue: 999);
            var target = CreateTargetEntity(chunkValue: 42);

            var sameBefore = SameChunk(outside, target);
            Debug.Log($"[7-2] 初期: outside={ReadValue(outside)}, target={ReadValue(target)}");
            Debug.Log($"[7-2] 初期 同一 Chunk? {sameBefore}");

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
            Debug.Log($"[7-2] 期待: outside の値が 42 に変化しうる（同一 Chunk の副作用）");

            var outsideChunkAfter = _em.GetChunk(outside);
            Debug.Log($"[7-2] Chunk 変化? {!outsideChunkBefore.Equals(outsideChunkAfter)}");
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
                Debug.Log($"[7-3] Chunk 変化? {!outsideChunkBefore.Equals(outsideChunkAfter)}");
                Debug.Log($"[7-3] 期待: outside の Chunk は変化しない、値も保持");
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
                Debug.Log($"[7-4] Chunk 変化? {!outsideChunkBefore.Equals(outsideChunkAfter)}");
                Debug.Log($"[7-4] 期待: outside の Chunk は変化しない、値も保持");
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
                SimulateP6ChunkComponentRestore(42);

                for (int i = 0; i < N / 2; i++)
                    destroyTargets[i] = targets[i];
                SimulateP7Destroy(destroyTargets);

                var outsideChunkAfter = _em.GetChunk(outside);
                Debug.Log($"[7-5] 処理後: outside={ReadValue(outside)}");
                Debug.Log($"[7-5] Chunk 変化? {!outsideChunkBefore.Equals(outsideChunkAfter)}");
                Debug.Log($"[7-5] 期待: outside の Chunk は変化しない、値も保持");
            }
            finally
            {
                targets.Dispose();
                destroyTargets.Dispose();
            }
        }
    }
}
