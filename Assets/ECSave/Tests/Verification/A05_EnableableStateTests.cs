using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A05Enableable : IComponentData, IEnableableComponent { public int Value; }
    public struct A05Plain : IComponentData { public int Value; }
    public struct A05BufferElem : IBufferElementData, IEnableableComponent { public int Value; }

    [DisableAutoCreation]
    public partial class A05ProbeSystem : SystemBase
    {
        ComponentLookup<A05Enableable> _lookup;

        protected override void OnCreate()
        {
            _lookup = GetComponentLookup<A05Enableable>(false);
        }

        protected override void OnUpdate() { }

        public ComponentLookup<A05Enableable> Lookup => _lookup;

        public void Refresh() { _lookup.Update(this); }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct A05LookupJob : IJob
    {
        public ComponentLookup<A05Enableable> Lookup;
        public Entity Target;
        public NativeArray<int> Out;
        public NativeArray<int> WasManaged;

        public void Execute()
        {
            Out[0] = Lookup.IsComponentEnabled(Target) ? 1 : 0;
            Lookup.SetComponentEnabled(Target, false);
            Out[1] = Lookup.IsComponentEnabled(Target) ? 1 : 0;
            WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
        }
    }

    public class A05_EnableableStateTests
    {
        World _world;
        EntityManager _em;
        A05ProbeSystem _probe;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A05");
            _em = _world.EntityManager;
            _probe = _world.CreateSystemManaged<A05ProbeSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void EntityManager_SetIsComponentEnabled_IComponentData()
        {
            var e = _em.CreateEntity(typeof(A05Enableable));
            Assert.IsTrue(_em.IsComponentEnabled<A05Enableable>(e));

            _em.SetComponentEnabled<A05Enableable>(e, false);
            Assert.IsFalse(_em.IsComponentEnabled<A05Enableable>(e));

            _em.SetComponentEnabled<A05Enableable>(e, true);
            Assert.IsTrue(_em.IsComponentEnabled<A05Enableable>(e));

            Debug.Log("[A-05] EntityManager.Set/IsComponentEnabled<IComponentData> 成功");
        }

        [Test]
        public void EntityManager_SetIsComponentEnabled_Buffer()
        {
            var e = _em.CreateEntity(typeof(A05BufferElem));
            Assert.IsTrue(_em.IsComponentEnabled<A05BufferElem>(e));

            _em.SetComponentEnabled<A05BufferElem>(e, false);
            Assert.IsFalse(_em.IsComponentEnabled<A05BufferElem>(e));

            Debug.Log("[A-05] EntityManager.Set/IsComponentEnabled<Buffer> 成功");
        }

        [Test]
        public void EntityManager_MissingComponent_Behavior()
        {
            var e = _em.CreateEntity();
            bool threw = false;
            try
            {
                _em.SetComponentEnabled<A05Enableable>(e, false);
            }
            catch (System.Exception ex)
            {
                threw = true;
                Debug.Log($"[A-05] コンポーネント欠落時の例外: {ex.GetType().Name}");
            }
            Debug.Log($"[A-05] T を持たない Entity への SetComponentEnabled: threw={threw}");
            Assert.Pass();
        }

        [Test]
        public void ComponentLookup_BurstJob()
        {
            var e = _em.CreateEntity(typeof(A05Enableable));
            _probe.Refresh();

            var outArr = new NativeArray<int>(2, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new A05LookupJob
                {
                    Lookup = _probe.Lookup,
                    Target = e,
                    Out = outArr,
                    WasManaged = wm,
                }.Run();

                Debug.Log($"[A-05] ComponentLookup in Burst: before={outArr[0]}, after={outArr[1]}, " +
                          $"managed={wm[0] == 1}");

                Assert.AreEqual(1, outArr[0], "初期状態が Enabled でない");
                Assert.AreEqual(0, outArr[1], "SetComponentEnabled(false) が反映されていない");
                Assert.IsFalse(_em.IsComponentEnabled<A05Enableable>(e),
                    "ジョブ内の Set がメインスレッドから見えていない");

                if (wm[0] == 1)
                    Assert.Inconclusive("Burst で実行されていない");
            }
            finally
            {
                outArr.Dispose();
                wm.Dispose();
            }
        }

        [Test]
        public void ArchetypeChunk_SetComponentEnabled()
        {
            var arch = _em.CreateArchetype(typeof(A05Enableable));
            var es = _em.CreateEntity(arch, 10, Allocator.TempJob);
            var query = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A05Enableable>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A05Enableable>(false);
                foreach (var c in chunks)
                {
                    for (int i = 0; i < c.Count; i++)
                        c.SetComponentEnabled(ref handle, i, false);
                }

                for (int i = 0; i < es.Length; i++)
                    Assert.IsFalse(_em.IsComponentEnabled<A05Enableable>(es[i]),
                        $"Entity {i} の Enabled が false になっていない");

                Debug.Log($"[A-05] ArchetypeChunk.SetComponentEnabled: {es.Length} 件を false に設定");
            }
            finally
            {
                chunks.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void ArchetypeChunk_GetEnabledMask()
        {
            var arch = _em.CreateArchetype(typeof(A05Enableable));
            var es = _em.CreateEntity(arch, 10, Allocator.TempJob);
            _em.SetComponentEnabled<A05Enableable>(es[3], false);
            _em.SetComponentEnabled<A05Enableable>(es[7], false);

            var query = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A05Enableable>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A05Enableable>(true);
                int total = 0, enabled = 0;
                foreach (var c in chunks)
                {
                    var mask = c.GetEnabledMask(ref handle);
                    for (int i = 0; i < c.Count; i++)
                    {
                        total++;
                        if (mask[i]) enabled++;
                    }
                }
                Debug.Log($"[A-05] GetEnabledMask: total={total}, enabled={enabled}");
                Assert.AreEqual(10, total);
                Assert.AreEqual(8, enabled, "無効化した 2 件がマスクに反映されていない");
            }
            finally
            {
                chunks.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void EntityCommandBuffer_SetComponentEnabled()
        {
            var e = _em.CreateEntity(typeof(A05Enableable));
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            try
            {
                ecb.SetComponentEnabled<A05Enableable>(e, false);
                ecb.Playback(_em);
                Assert.IsFalse(_em.IsComponentEnabled<A05Enableable>(e));
                Debug.Log("[A-05] ECB.SetComponentEnabled 成功");
            }
            finally
            {
                ecb.Dispose();
            }
        }

        [Test]
        public void Query_DefaultExcludesDisabled_IgnoreOptionIncludes()
        {
            var arch = _em.CreateArchetype(typeof(A05Enableable));
            var es = _em.CreateEntity(arch, 5, Allocator.TempJob);
            _em.SetComponentEnabled<A05Enableable>(es[0], false);
            _em.SetComponentEnabled<A05Enableable>(es[1], false);

            var qDefault = _em.CreateEntityQuery(typeof(A05Enableable));
            var qIgnore = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A05Enableable>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            Debug.Log($"[A-05] default={qDefault.CalculateEntityCount()}, ignore={qIgnore.CalculateEntityCount()}");
            Assert.AreEqual(3, qDefault.CalculateEntityCount(),
                "デフォルトクエリが無効化済み Entity を除外していない");
            Assert.AreEqual(5, qIgnore.CalculateEntityCount(),
                "IgnoreComponentEnabledState で全件が取得できていない");

            es.Dispose();
        }
    }
}
