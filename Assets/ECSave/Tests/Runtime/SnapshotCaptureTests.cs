using NUnit.Framework;
using ECSave.Internal;
using Game.Domain;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// Phase 5a：A-22 の生バイト経路を使った SnapshotCapture の単体テスト。
    /// 型付きアクセスの値と、生バイト経路の値が一致することを検証する。
    /// </summary>
    public unsafe class SnapshotCaptureTests
    {
        private World _world;
        private EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("SnapshotCaptureTestWorld");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Health>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        [Test]
        public void CaptureChunks_RawBytes_MatchTypedValues()
        {
            var archetype = _em.CreateArchetype(typeof(Health));
            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                var e = _em.CreateEntity(archetype);
                _em.SetComponentData(e, new Health { Current = i * 10, Max = 100 });
            }

            var buffer = new SnapshotBuffer(16, sizeof(Health), Allocator.TempJob);
            try
            {
                var query = BuildQuery(_em);
                SnapshotCapture.CaptureChunksPlain<Health>(query, _em, ref buffer);

                Assert.AreEqual(count, buffer.Count);

                // 生バイト経由で読み戻す
                var actual = new System.Collections.Generic.HashSet<(int, int)>();
                for (int i = 0; i < buffer.Count; i++)
                {
                    byte* p = buffer.GetValuePtr(i);
                    int current = *(int*)p;
                    int max = *(int*)(p + 4);
                    actual.Add((current, max));
                }

                for (int i = 0; i < count; i++)
                    Assert.IsTrue(actual.Contains((i * 10, 100)),
                        $"missing Health({i * 10}, 100)");
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test]
        public void CaptureChunks_EmptyQuery_YieldsEmptyBuffer()
        {
            var buffer = new SnapshotBuffer(16, sizeof(Health), Allocator.TempJob);
            try
            {
                var query = BuildQuery(_em);
                SnapshotCapture.CaptureChunksPlain<Health>(query, _em, ref buffer);
                Assert.AreEqual(0, buffer.Count);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test]
        public void CaptureChunks_MultipleChunks_AllCaptured()
        {
            // 複数 Chunk にまたがる数を用意する
            var archetype = _em.CreateArchetype(typeof(Health));
            const int count = 500; // 通常 1 Chunk に収まらない数
            for (int i = 0; i < count; i++)
            {
                var e = _em.CreateEntity(archetype);
                _em.SetComponentData(e, new Health { Current = i, Max = i + 1000 });
            }

            var buffer = new SnapshotBuffer(64, sizeof(Health), Allocator.TempJob);
            try
            {
                var query = BuildQuery(_em);
                SnapshotCapture.CaptureChunksPlain<Health>(query, _em, ref buffer);

                Assert.AreEqual(count, buffer.Count);

                var actual = new System.Collections.Generic.HashSet<(int, int)>();
                for (int i = 0; i < buffer.Count; i++)
                {
                    byte* p = buffer.GetValuePtr(i);
                    actual.Add((*(int*)p, *(int*)(p + 4)));
                }

                for (int i = 0; i < count; i++)
                    Assert.IsTrue(actual.Contains((i, i + 1000)));
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test]
        public void CaptureChunks_AfterStructuralChange_NoStaleCache()
        {
            // A-22：構造変更後は Chunk ポインタが無効化される。
            // SnapshotCapture は Chunk をキャッシュしないため、追加後の Capture でも正しい値が取れる。
            var archetype = _em.CreateArchetype(typeof(Health));
            for (int i = 0; i < 5; i++)
            {
                var e = _em.CreateEntity(archetype);
                _em.SetComponentData(e, new Health { Current = i, Max = 100 });
            }

            var buffer1 = new SnapshotBuffer(16, sizeof(Health), Allocator.TempJob);
            try
            {
                var q1 = BuildQuery(_em);
                SnapshotCapture.CaptureChunksPlain<Health>(q1, _em, ref buffer1);
                Assert.AreEqual(5, buffer1.Count);
            }
            finally { buffer1.Dispose(); }

            // 構造変更：さらに 5 件追加
            for (int i = 0; i < 5; i++)
            {
                var e = _em.CreateEntity(archetype);
                _em.SetComponentData(e, new Health { Current = i + 100, Max = 100 });
            }

            var buffer2 = new SnapshotBuffer(16, sizeof(Health), Allocator.TempJob);
            try
            {
                var q2 = BuildQuery(_em);
                SnapshotCapture.CaptureChunksPlain<Health>(q2, _em, ref buffer2);
                Assert.AreEqual(10, buffer2.Count);

                var actual = new System.Collections.Generic.HashSet<(int, int)>();
                for (int i = 0; i < buffer2.Count; i++)
                {
                    byte* p = buffer2.GetValuePtr(i);
                    actual.Add((*(int*)p, *(int*)(p + 4)));
                }

                for (int i = 0; i < 5; i++)
                    Assert.IsTrue(actual.Contains((i, 100)));
                for (int i = 0; i < 5; i++)
                    Assert.IsTrue(actual.Contains((i + 100, 100)));
            }
            finally { buffer2.Dispose(); }
        }
    }
}
