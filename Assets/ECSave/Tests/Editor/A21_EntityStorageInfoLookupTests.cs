using ECSave.Tests;
using NUnit.Framework;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Debug = UnityEngine.Debug;

namespace ECSave.Tests
{
    public struct A21Data : IComponentData { public int Value; }

    [BurstCompile(CompileSynchronously = true)]
    public struct A21ExistsJob : IJob
    {
        [ReadOnly] public EntityStorageInfoLookup Lookup;
        [ReadOnly] public ComponentTypeHandle<Prefab> PrefabHandle;
        [ReadOnly] public NativeArray<Entity> Query;
        public NativeArray<int> Exists;   // 1 = 存在
        public NativeArray<int> IsPrefab; // 1 = Prefab を持つチャンク
        public NativeArray<int> WasManaged;

        public void Execute()
        {
            for (int i = 0; i < Query.Length; i++)
            {
                bool exists = Lookup.Exists(Query[i]);
                Exists[i] = exists ? 1 : 0;
                IsPrefab[i] = 0;
                if (exists)
                {
                    var info = Lookup[Query[i]];
                    var chunk = info.Chunk;
                    IsPrefab[i] = chunk.Has(ref PrefabHandle) ? 1 : 0;
                }
            }
            WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
        }
    }

    /// <summary>
    /// EntityStorageInfoLookup は SystemBase / SystemState / SystemAPI から取得する（EntityManager には無い）。
    /// SaveRuntimeSystem と同じ形（システムが保持し、Update(this) で更新）で検証する。
    /// </summary>
    [DisableAutoCreation]
    public partial class A21ProbeSystem : SystemBase
    {
        EntityStorageInfoLookup _lookup;
        ComponentTypeHandle<Prefab> _prefabHandle;

        protected override void OnCreate()
        {
            _lookup = GetEntityStorageInfoLookup();
            _prefabHandle = GetComponentTypeHandle<Prefab>(true);
        }

        protected override void OnUpdate() { }

        public void Probe(NativeArray<Entity> query, NativeArray<int> exists, NativeArray<int> isPrefab, NativeArray<int> managed)
        {
            // 構造変更の後はハンドルの更新が必要（SaveRuntimeSystem でも W2 の直前に行う）
            _lookup.Update(this);
            _prefabHandle.Update(this);
            new A21ExistsJob
            {
                Lookup = _lookup,
                PrefabHandle = _prefabHandle,
                Query = query,
                Exists = exists,
                IsPrefab = isPrefab,
                WasManaged = managed,
            }.Run();
        }
    }

    /// <summary>
    /// A-21: EntityStorageInfoLookup（W2 の有効性フィルタ。内部設計 7.2節）。
    /// 確認: (1) SystemBase からの取得・Update(this)・ジョブでの使用 (2) 生存／破棄済み／Entity.Null／再利用された古い世代の判定
    ///       (3) チャンクから Prefab を判定できること (4) 10万件のコスト
    /// </summary>
    public class A21_EntityStorageInfoLookupTests
    {
        World _world;
        EntityManager _em;
        A21ProbeSystem _probe;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A21");
            _em = _world.EntityManager;
            _probe = _world.CreateSystemManaged<A21ProbeSystem>();
        }

        [TearDown] public void TearDown() { if (_world != null && _world.IsCreated) _world.Dispose(); }

        [Test]
        public void Exists_Alive_Destroyed_Null_StaleVersion_Prefab()
        {
            var alive = _em.CreateEntity(typeof(A21Data));
            var destroyed = _em.CreateEntity(typeof(A21Data));
            _em.DestroyEntity(destroyed);
            var recycled = _em.CreateEntity(typeof(A21Data)); // destroyed と同じ Index を再利用しうる
            var prefab = _em.CreateEntity(typeof(A21Data), typeof(Prefab));

            Debug.Log($"[A-21] destroyed={destroyed} recycled={recycled} (Index が同じなら世代判定を検証できる)");

            var query = new NativeArray<Entity>(5, Allocator.TempJob);
            var exists = new NativeArray<int>(5, Allocator.TempJob);
            var isPrefab = new NativeArray<int>(5, Allocator.TempJob);
            var managed = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                query[0] = alive; query[1] = destroyed; query[2] = Entity.Null; query[3] = recycled; query[4] = prefab;
                _probe.Probe(query, exists, isPrefab, managed);

                Debug.Log($"[A-21] burst managed={managed[0] == 1}; exists=[{exists[0]},{exists[1]},{exists[2]},{exists[3]},{exists[4]}] prefab=[{isPrefab[0]},{isPrefab[1]},{isPrefab[2]},{isPrefab[3]},{isPrefab[4]}]");

                Assert.AreEqual(1, exists[0], "生存 Entity");
                Assert.AreEqual(0, exists[1], "破棄済み Entity（古い世代）が存在扱い");
                Assert.AreEqual(0, exists[2], "Entity.Null が存在扱い");
                Assert.AreEqual(1, exists[3], "再利用後の Entity");
                Assert.AreEqual(1, exists[4], "Prefab Entity 自体は存在する");
                Assert.AreEqual(0, isPrefab[0]);
                Assert.AreEqual(0, isPrefab[3]);
                Assert.AreEqual(1, isPrefab[4], "チャンクから Prefab を判定できない（W2 の除外方式を見直す）");
                if (managed[0] == 1) Assert.Inconclusive("Burst で実行されていない（Burst を有効にして再実行）。Burst 内の EntityStorageInfoLookup は未検証");
            }
            finally { query.Dispose(); exists.Dispose(); isPrefab.Dispose(); managed.Dispose(); }
        }

        [Test]
        public void CachedLookup_AfterStructuralChange_WithUpdate()
        {
            var e = _em.CreateEntity(typeof(A21Data));
            var q = new NativeArray<Entity>(1, Allocator.TempJob);
            var exists = new NativeArray<int>(1, Allocator.TempJob);
            var isPrefab = new NativeArray<int>(1, Allocator.TempJob);
            var managed = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                q[0] = e;
                _probe.Probe(q, exists, isPrefab, managed);
                Assert.AreEqual(1, exists[0]);

                _em.DestroyEntity(e); // 構造変更。保持しているルックアップは Update(this) で更新する
                _probe.Probe(q, exists, isPrefab, managed);
                Assert.AreEqual(0, exists[0], "Update(this) 後も破棄済みを存在扱いしている");
            }
            finally { q.Dispose(); exists.Dispose(); isPrefab.Dispose(); managed.Dispose(); }
        }

        [Test]
        public void Cost_100kEntities_LogOnly()
        {
            const int n = 100_000;
            var archetype = _em.CreateArchetype(typeof(A21Data));
            var entities = _em.CreateEntity(archetype, n, Allocator.TempJob); // A-03 の参考（NativeArray 返却版）
            var exists = new NativeArray<int>(n, Allocator.TempJob);
            var isPrefab = new NativeArray<int>(n, Allocator.TempJob);
            var managed = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                _probe.Probe(entities, exists, isPrefab, managed); // ウォームアップ
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++) _probe.Probe(entities, exists, isPrefab, managed);
                sw.Stop();
                Debug.Log($"[A-21] 10万 Entity の Exists＋Prefab 判定: {sw.Elapsed.TotalMilliseconds / 10:F3} ms/回");
                int count = 0;
                for (int i = 0; i < n; i++) count += exists[i];
                Assert.AreEqual(n, count);
            }
            finally { entities.Dispose(); exists.Dispose(); isPrefab.Dispose(); managed.Dispose(); }
        }
    }
}
