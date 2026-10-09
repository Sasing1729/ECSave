using NUnit.Framework;
using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A09Data : IComponentData { public int Value; }

    public struct A09Shared : ISharedComponentData
    {
        public int Value;
    }

    public struct A09SharedParity : ISharedComponentData, IEquatable<A09SharedParity>
    {
        public int Value;

        public bool Equals(A09SharedParity other) => (Value & 1) == (other.Value & 1);
        public override int GetHashCode() => Value & 1;
        public override bool Equals(object obj) => obj is A09SharedParity o && Equals(o);
    }

    public class A09_SharedComponentTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A09");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void UnmanagedSharedComponent_AddGet()
        {
            var arch = _em.CreateArchetype(typeof(A09Data));
            var e = _em.CreateEntity(arch);

            _em.AddSharedComponent(e, new A09Shared { Value = 42 });
            var v = _em.GetSharedComponent<A09Shared>(e);
            Debug.Log($"[A-09] AddSharedComponent → GetSharedComponent value={v.Value}");
            Assert.AreEqual(42, v.Value);
        }

        [Test]
        public void SameValue_SameChunk_DifferentValue_DifferentChunk()
        {
            var arch = _em.CreateArchetype(typeof(A09Data));
            var e1 = _em.CreateEntity(arch);
            var e2 = _em.CreateEntity(arch);
            var e3 = _em.CreateEntity(arch);

            _em.AddSharedComponent(e1, new A09Shared { Value = 1 });
            _em.AddSharedComponent(e2, new A09Shared { Value = 1 });
            _em.AddSharedComponent(e3, new A09Shared { Value = 2 });

            var c1 = _em.GetChunk(e1);
            var c2 = _em.GetChunk(e2);
            var c3 = _em.GetChunk(e3);

            Debug.Log($"[A-09] same value → same chunk? {c1.Equals(c2)}");
            Debug.Log($"[A-09] different value → different chunk? {!c1.Equals(c3)}");

            Assert.IsTrue(c1.Equals(c2),
                "同一 SharedComponent 値を持つ Entity が同一 Chunk に配置されていない");
            Assert.IsFalse(c1.Equals(c3),
                "異なる SharedComponent 値を持つ Entity が同一 Chunk に配置されている");
        }

        [Test]
        public void CustomEquality_AffectsGrouping()
        {
            var arch = _em.CreateArchetype(typeof(A09Data));
            var e1 = _em.CreateEntity(arch);
            var e2 = _em.CreateEntity(arch);
            var e3 = _em.CreateEntity(arch);

            _em.AddSharedComponent(e1, new A09SharedParity { Value = 1 });
            _em.AddSharedComponent(e2, new A09SharedParity { Value = 3 });
            _em.AddSharedComponent(e3, new A09SharedParity { Value = 2 });

            var c1 = _em.GetChunk(e1);
            var c2 = _em.GetChunk(e2);
            var c3 = _em.GetChunk(e3);

            Debug.Log($"[A-09] parity(1) vs parity(3) → same chunk? {c1.Equals(c2)}");
            Debug.Log($"[A-09] parity(1) vs parity(2) → different chunk? {!c1.Equals(c3)}");

            Assert.IsTrue(c1.Equals(c2),
                "IEquatable<T> の等価判定が Chunk グループ化に反映されていない");
            Assert.IsFalse(c1.Equals(c3));
        }

        [Test]
        public void BulkAdd_SameValue_GroupsIntoOneChunk()
        {
            var arch = _em.CreateArchetype(typeof(A09Data));
            var es = _em.CreateEntity(arch, 100, Allocator.TempJob);
            try
            {
                var shared = new A09Shared { Value = 777 };
                for (int i = 0; i < es.Length; i++)
                    _em.AddSharedComponent(es[i], shared);

                var c0 = _em.GetChunk(es[0]);
                int distinctChunks = 0;
                for (int i = 1; i < es.Length; i++)
                {
                    if (!c0.Equals(_em.GetChunk(es[i])))
                        distinctChunks++;
                }
                Debug.Log($"[A-09] bulk add: 100 件中 distinct chunk 数={distinctChunks + 1}");
                Assert.AreEqual(0, distinctChunks,
                    "同一値の一括追加で全 Entity が同一 Chunk に集まっていない");
            }
            finally
            {
                es.Dispose();
            }
        }

        [Test]
        public void ChangeValue_MovesEntityToNewChunk()
        {
            var arch = _em.CreateArchetype(typeof(A09Data));
            var e = _em.CreateEntity(arch);
            _em.AddSharedComponent(e, new A09Shared { Value = 1 });
            var c1 = _em.GetChunk(e);

            _em.SetSharedComponent(e, new A09Shared { Value = 2 });
            var c2 = _em.GetChunk(e);

            Debug.Log($"[A-09] shared value change moves chunk? {!c1.Equals(c2)}");
            Assert.IsFalse(c1.Equals(c2),
                "SharedComponent 値の変更で Chunk が移動していない");
        }

        [Test]
        public void EntityQuery_FilterBySharedComponent()
        {
            var arch = _em.CreateArchetype(typeof(A09Data), typeof(A09Shared));
            var e1 = _em.CreateEntity(arch);
            var e2 = _em.CreateEntity(arch);
            var e3 = _em.CreateEntity(arch);

            _em.SetSharedComponent(e1, new A09Shared { Value = 10 });
            _em.SetSharedComponent(e2, new A09Shared { Value = 10 });
            _em.SetSharedComponent(e3, new A09Shared { Value = 20 });

            var q = _em.CreateEntityQuery(typeof(A09Data), typeof(A09Shared));
            q.AddSharedComponentFilter(new A09Shared { Value = 10 });

            int count = q.CalculateEntityCount();
            Debug.Log($"[A-09] shared component filter count={count} (expected 2)");
            Assert.AreEqual(2, count,
                "SharedComponent フィルタが機能していない");
        }

        [Test]
        public void EntityQuery_SetSharedComponent_Bulk()
        {
            var arch = _em.CreateArchetype(typeof(A09Data), typeof(A09Shared));
            var es = _em.CreateEntity(arch, 50, Allocator.TempJob);

            var q = _em.CreateEntityQuery(typeof(A09Data), typeof(A09Shared));
            _em.SetSharedComponent(q, new A09Shared { Value = 999 });

            var entities = q.ToEntityArray(Allocator.TempJob);
            try
            {
                foreach (var e in entities)
                    Assert.AreEqual(999, _em.GetSharedComponent<A09Shared>(e).Value);
                Debug.Log($"[A-09] EntityQuery.SetSharedComponent で {entities.Length} 件に一括設定");
            }
            finally
            {
                entities.Dispose();
                es.Dispose();
            }
        }
    }
}
