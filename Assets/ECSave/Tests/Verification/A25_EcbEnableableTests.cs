using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A25Enableable : IComponentData, IEnableableComponent { public int Value; }

    public class A25_EcbEnableableTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A25");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void Ecb_SetComponentEnabled_False()
        {
            var e = _em.CreateEntity(typeof(A25Enableable));
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            try
            {
                ecb.SetComponentEnabled<A25Enableable>(e, false);
                ecb.Playback(_em);

                Debug.Log($"[A-25] ECB.SetComponentEnabled(false) → IsEnabled={_em.IsComponentEnabled<A25Enableable>(e)}");
                Assert.IsFalse(_em.IsComponentEnabled<A25Enableable>(e));
            }
            finally
            {
                ecb.Dispose();
            }
        }

        [Test]
        public void Ecb_SetComponentEnabled_TrueAfterFalse()
        {
            var e = _em.CreateEntity(typeof(A25Enableable));
            _em.SetComponentEnabled<A25Enableable>(e, false);

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            try
            {
                ecb.SetComponentEnabled<A25Enableable>(e, true);
                ecb.Playback(_em);

                Debug.Log($"[A-25] ECB.SetComponentEnabled(true) → IsEnabled={_em.IsComponentEnabled<A25Enableable>(e)}");
                Assert.IsTrue(_em.IsComponentEnabled<A25Enableable>(e));
            }
            finally
            {
                ecb.Dispose();
            }
        }

        [Test]
        public void Ecb_SetComponentEnabled_DeferredCreation()
        {
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            try
            {
                var tempEntity = ecb.CreateEntity();
                ecb.AddComponent<A25Enableable>(tempEntity);
                ecb.SetComponentEnabled<A25Enableable>(tempEntity, false);

                ecb.Playback(_em);

                var entities = _em.GetAllEntities(Allocator.Temp);
                try
                {
                    Assert.AreEqual(1, entities.Length);
                    var created = entities[0];
                    Assert.IsFalse(_em.IsComponentEnabled<A25Enableable>(created),
                        "遅延生成 Entity への SetComponentEnabled(false) が反映されていない");
                    Debug.Log($"[A-25] ECB で CreateEntity + AddComponent + SetComponentEnabled(false) が動作");
                }
                finally
                {
                    entities.Dispose();
                }
            }
            finally
            {
                ecb.Dispose();
            }
        }

        [Test]
        public void Ecb_SetComponentEnabled_MissingComponent_Behavior()
        {
            var e = _em.CreateEntity();
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            bool threw = false;
            try
            {
                ecb.SetComponentEnabled<A25Enableable>(e, false);
                try
                {
                    ecb.Playback(_em);
                }
                catch (System.Exception ex)
                {
                    threw = true;
                    Debug.Log($"[A-25] Playback 例外: {ex.GetType().Name}");
                }

                if (!threw)
                {
                    Debug.Log("[A-25] Playback は例外なし。次フレームで T を持たない Entity への" +
                              "SetComponentEnabled は ArgumentException 相当になる可能性");
                }
            }
            finally
            {
                ecb.Dispose();
            }

            Debug.Log($"[A-25] T を持たない Entity への ECB.SetComponentEnabled: threw={threw}");
            Assert.Pass();
        }

        [Test]
        public void Ecb_ParallelWriter_SetComponentEnabled()
        {
            var e1 = _em.CreateEntity(typeof(A25Enableable));
            var e2 = _em.CreateEntity(typeof(A25Enableable));
            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            try
            {
                var writer = ecb.AsParallelWriter();
                writer.SetComponentEnabled<A25Enableable>(0, e1, false);
                writer.SetComponentEnabled<A25Enableable>(0, e2, false);
                ecb.Playback(_em);

                Assert.IsFalse(_em.IsComponentEnabled<A25Enableable>(e1));
                Assert.IsFalse(_em.IsComponentEnabled<A25Enableable>(e2));
                Debug.Log("[A-25] ECB.AsParallelWriter().SetComponentEnabled が動作");
            }
            finally
            {
                ecb.Dispose();
            }
        }
    }
}
