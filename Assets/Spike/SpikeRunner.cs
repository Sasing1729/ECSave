using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SaveSpike
{
    public class SpikeRunner : MonoBehaviour
    {
        World _world;
        string _status = "(idle)";
        System.Action _pendingCallback;

        public int LargeCount = 100_000;
        public int SmallCount = 1_000;

        void Awake()
        {
            _world = World.DefaultGameObjectInjectionWorld;
        }

        void Update()
        {
            if (!SnapshotExchange.Ready || _pendingCallback == null) return;

            // ★ ここでジョブを完了させる（TempJob のリーク防止）
            SnapshotExchange.Handle.Complete();
            SnapshotExchange.Ready = false;

            var cb = _pendingCallback;
            _pendingCallback = null;
            try { cb(); }
            catch (System.Exception e) { _status = "ERR: " + e.Message; }
        }

        void RequestSnapshot(System.Action onReady)
        {
            if (_pendingCallback != null)
            {
                _status = "snapshot already pending";
                return;
            }
            SnapshotExchange.Ready = false;
            SnapshotExchange.RequestPending = true;
            _pendingCallback = onReady;
        }

        // ============================================================
        // GUI
        // ============================================================
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 560, 900));
            GUILayout.Label("=== Spike Runner ===");
            GUILayout.Label(_status);
            GUILayout.Space(10);

            GUILayout.Label("Setup");
            if (GUILayout.Button("Clear World"))
                _status = RunSafe(ClearWorld);
            if (GUILayout.Button($"Spawn Mixed ({SmallCount})"))
                _status = RunSafe(() => SpawnMixed(SmallCount));
            if (GUILayout.Button($"Spawn Mixed ({LargeCount})"))
                _status = RunSafe(() => SpawnMixed(LargeCount));

            GUILayout.Space(10);
            GUILayout.Label("Tests");
            if (GUILayout.Button("S8: RoundTrip (1k)"))
                TestRoundTrip(SmallCount);
            if (GUILayout.Button("S4: Missing Components"))
                TestMissingComponents();
            if (GUILayout.Button("S10: Duplicate IDs"))
                TestDuplicateIds();

            GUILayout.Space(10);
            GUILayout.Label("Performance");
            if (GUILayout.Button($"S1/S2: {LargeCount} Serialize+Deserialize"))
                TestLarge(LargeCount);

            GUILayout.EndArea();
        }

        string RunSafe(System.Action a)
        {
            try { a(); return "OK"; }
            catch (System.Exception e) { return "ERR: " + e.Message; }
        }

        // ============================================================
        // Setup
        // ============================================================
        void ClearWorld()
        {
            using var q = _world.EntityManager.CreateEntityQuery(typeof(SpikeId));
            _world.EntityManager.DestroyEntity(q);
            // 他のタグ付きも消す
            using var q2 = _world.EntityManager.CreateEntityQuery(typeof(SpikePosition));
            _world.EntityManager.DestroyEntity(q2);
            using var q3 = _world.EntityManager.CreateEntityQuery(typeof(SpikeDroppedItemTag));
            _world.EntityManager.DestroyEntity(q3);
        }

        void SpawnMixed(int n)
        {
            var em = _world.EntityManager;
            var arch = em.CreateArchetype(
                typeof(SpikeId), typeof(SpikeHealth), typeof(SpikePosition), typeof(SpikeTarget));

            var ents = em.CreateEntity(arch, n, Allocator.TempJob);
            var ids = new NativeArray<SpikeId>(n, Allocator.TempJob);
            var healths = new NativeArray<SpikeHealth>(n, Allocator.TempJob);
            var positions = new NativeArray<SpikePosition>(n, Allocator.TempJob);

            for (int i = 0; i < n; i++)
            {
                ids[i] = new SpikeId { Value = i };
                healths[i] = new SpikeHealth { Current = 100 + (i % 50), Max = 200 };
                positions[i] = new SpikePosition { Value = new float3(i % 100, 0, i / 100) };
            }

            for (int i = 0; i < n; i++)
            {
                em.SetComponentData(ents[i], ids[i]);
                em.SetComponentData(ents[i], healths[i]);
                em.SetComponentData(ents[i], positions[i]);
            }

            ents.Dispose();
            ids.Dispose();
            healths.Dispose();
            positions.Dispose();
        }

        void SpawnPlayer(int id)
        {
            var em = _world.EntityManager;
            var arch = em.CreateArchetype(
                typeof(SpikeId), typeof(SpikePlayer), typeof(SpikeHealth),
                typeof(SpikeStunned), typeof(SpikeInventory));
            var e = em.CreateEntity(arch);
            em.SetComponentData(e, new SpikeId { Value = id });
            em.SetComponentData(e, new SpikeHealth { Current = 200, Max = 200 });
            em.AddBuffer<SpikeInventory>(e).Add(new SpikeInventory { ItemId = 1, Count = 3 });
        }

        void SpawnCamera()
        {
            var em = _world.EntityManager;
            var arch = em.CreateArchetype(typeof(SpikePosition), typeof(SpikeCameraTag));
            var e = em.CreateEntity(arch);
            em.SetComponentData(e, new SpikePosition { Value = new float3(0, 10, -5) });
        }

        void SpawnDropItem(int id)
        {
            var em = _world.EntityManager;
            var arch = em.CreateArchetype(typeof(SpikeId), typeof(SpikePosition), typeof(SpikeDroppedItemTag));
            var e = em.CreateEntity(arch);
            em.SetComponentData(e, new SpikeId { Value = id });
            em.SetComponentData(e, new SpikePosition { Value = new float3(5, 0, 5) });
        }

        // ============================================================
        // Tests
        // ============================================================
        void TestRoundTrip(int n)
        {
            ClearWorld();
            SpawnMixed(n);
            SpawnPlayer(9000);
            SpawnCamera();
            SpawnDropItem(9001);

            RequestSnapshot(() =>
            {
                var cols = SnapshotExchange.Result;
                SnapshotExchange.Ready = false;

                int initialCount = cols.Count;
                var bytes = SpikeSerializer.Serialize(cols, out var st, SpikeWriterKind.Manual);
                cols.Dispose();

                var result = SpikeDeserializer.Apply(
                    _world.EntityManager, bytes, SpikeDeletionPolicy.ByRegistration,
                    out var dt, out _, out var finalCount);

                _status = $"S8 RoundTrip: initial={initialCount} final={finalCount} " +
                          $"serialize={st.WriteMs:F1}ms deserialize={dt.TotalMs:F1}ms result={result}";
            });
        }

        void TestMissingComponents()
        {
            ClearWorld();
            SpawnPlayer(1);

            RequestSnapshot(() =>
            {
                var cols = SnapshotExchange.Result;
                SnapshotExchange.Ready = false;

                var bytes = SpikeSerializer.Serialize(cols, out _, SpikeWriterKind.Manual);
                cols.Dispose();

                // 既存EntityからSpikeHealthを剥がす
                using var q = _world.EntityManager.CreateEntityQuery(typeof(SpikeId));
                var e = q.GetSingletonEntity();
                _world.EntityManager.RemoveComponent<SpikeHealth>(e);

                _status = _world.EntityManager.HasComponent<SpikeHealth>(e)
                    ? "S4: failed to remove SpikeHealth"
                    : "S4: SpikeHealth removed, loading...";

                var result = SpikeDeserializer.Apply(
                    _world.EntityManager, bytes, SpikeDeletionPolicy.ByRegistration,
                    out var dt, out _, out var _);

                bool restored = _world.EntityManager.HasComponent<SpikeHealth>(e);
                var hp = restored ? _world.EntityManager.GetComponentData<SpikeHealth>(e) : default;
                _status = $"S4: result={result} restored={restored} hp={hp.Current}/{hp.Max} t={dt.TotalMs:F1}ms";
            });
        }

        void TestDuplicateIds()
        {
            ClearWorld();
            SpawnMixed(10);

            RequestSnapshot(() =>
            {
                var cols = SnapshotExchange.Result;
                SnapshotExchange.Ready = false;

                var bytes = SpikeSerializer.Serialize(cols, out _, SpikeWriterKind.Manual);
                cols.Dispose();

                // World側に重複IDを作る
                var em = _world.EntityManager;
                var arch = em.CreateArchetype(typeof(SpikeId), typeof(SpikeHealth));
                var dup = em.CreateEntity(arch);
                em.SetComponentData(dup, new SpikeId { Value = 5 });  // 既存と衝突
                em.SetComponentData(dup, new SpikeHealth { Current = 1, Max = 1 });

                var result = SpikeDeserializer.Apply(
                    em, bytes, SpikeDeletionPolicy.ByRegistration,
                    out var dt, out _, out var _);

                _status = $"S10: result={result} (expected ValidationFailed) t={dt.TotalMs:F1}ms";

                // 後始末
                em.DestroyEntity(dup);
            });
        }

        void TestLarge(int n)
        {
            ClearWorld();
            SpawnMixed(n);

            RequestSnapshot(() =>
            {
                var cols = SnapshotExchange.Result;
                SnapshotExchange.Ready = false;

                var scheduleTicks = SnapshotExchange.ScheduleTicks;
                double scheduleMs = scheduleTicks * 1000.0 / Stopwatch.Frequency;
                int snapCount = cols.Count;

                var sw = Stopwatch.StartNew();
                var bytes = SpikeSerializer.Serialize(cols, out var st, SpikeWriterKind.Manual);
                double serializeMs = sw.Elapsed.TotalMilliseconds;
                cols.Dispose();

                // World を空にしてからロード
                ClearWorld();

                sw.Restart();
                var result = SpikeDeserializer.Apply(
                    _world.EntityManager, bytes, SpikeDeletionPolicy.ByRegistration,
                    out var dt, out _, out var finalCount);
                double loadMs = sw.Elapsed.TotalMilliseconds;

                _status =
                    $"N={n} snapSchedule={scheduleMs:F2}ms serialize={serializeMs:F1}ms " +
                    $"(resolve={st.ResolveMs:F1} write={st.WriteMs:F1} checksum={st.ChecksumMs:F1}) " +
                    $"load={loadMs:F1}ms (decode={dt.DecodeMs:F1} plan={dt.PlanMs:F1} " +
                    $"create={dt.CreateMs:F1} write={dt.WriteValuesMs:F1}) " +
                    $"result={result} count={finalCount}";
            });
        }
    }
}
