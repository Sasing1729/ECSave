using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A19Data : IComponentData { public int Value; }
    public struct A19Tag : IComponentData { }

    public class A19_CustomQuerySystemEntityTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A19");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void DefaultQuery_ExcludesSystemEntities()
        {
            _em.CreateEntity(typeof(A19Data));
            _em.CreateEntity(typeof(A19Data));
            _em.CreateEntity(typeof(A19Data));

            var q = _em.CreateEntityQuery(typeof(A19Data));
            int count = q.CalculateEntityCount();

            Debug.Log($"[A-19] default query count={count}");
            Assert.AreEqual(3, count, "デフォルトクエリで Prefab / システム Entity が混入");
        }

        [Test]
        public void QueryWithIncludeSystems_CountsSystemEntities()
        {
            _em.CreateEntity(typeof(A19Data));

            var qWith = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A19Data>() },
                Options = EntityQueryOptions.IncludeSystems,
            });
            var qDefault = _em.CreateEntityQuery(typeof(A19Data));

            Debug.Log($"[A-19] IncludeSystems={qWith.CalculateEntityCount()}, " +
                      $"default={qDefault.CalculateEntityCount()}");
            Assert.Pass();
        }

        [Test]
        public void QueryWithIncludePrefab_CountsPrefab()
        {
            _em.CreateEntity(typeof(A19Data));
            _em.CreateEntity(typeof(A19Data), typeof(Prefab));

            var qDefault = _em.CreateEntityQuery(typeof(A19Data));
            var qWithPrefab = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A19Data>() },
                Options = EntityQueryOptions.IncludePrefab,
            });

            Debug.Log($"[A-19] default={qDefault.CalculateEntityCount()}, " +
                      $"IncludePrefab={qWithPrefab.CalculateEntityCount()}");
            Assert.AreEqual(1, qDefault.CalculateEntityCount(),
                "デフォルトで Prefab が混入");
            Assert.AreEqual(2, qWithPrefab.CalculateEntityCount(),
                "IncludePrefab で Prefab が含まれていない");
        }

        [Test]
        public void QueryWithIncludeDisabledEntities()
        {
            var e = _em.CreateEntity(typeof(A19Data));
            _em.SetEnabled(e, false);

            var qDefault = _em.CreateEntityQuery(typeof(A19Data));
            var qIncludeDisabled = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A19Data>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });

            Debug.Log($"[A-19] default={qDefault.CalculateEntityCount()}, " +
                      $"IncludeDisabledEntities={qIncludeDisabled.CalculateEntityCount()}");
            Assert.AreEqual(0, qDefault.CalculateEntityCount());
            Assert.AreEqual(1, qIncludeDisabled.CalculateEntityCount());
        }

        [Test]
        public void QueryWithIncludeSystemsAndPrefab_StillExcludesInternalEntity()
        {
            _em.CreateEntity(typeof(A19Data));
            _em.CreateEntity(typeof(A19Data), typeof(Prefab));

            var qAll = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A19Data>() },
                Options = EntityQueryOptions.IncludeSystems
                        | EntityQueryOptions.IncludePrefab
                        | EntityQueryOptions.IncludeDisabledEntities,
            });

            int count = qAll.CalculateEntityCount();
            Debug.Log($"[A-19] IncludeSystems|IncludePrefab|IncludeDisabledEntities={count}");
            Assert.GreaterOrEqual(count, 2);
            Debug.Log("[A-19] 全 Include オプション指定でも、ToEntityArray で各 Entity を検査すれば" +
                      "ECS 内部 Entity を識別できる（Prefab フラグ、World 種別等）");
        }

        [Test]
        public void QueryFilter_WithEntityManagerWorldCompare()
        {
            var world2 = new World("A19-Other");
            try
            {
                _em.CreateEntity(typeof(A19Data));
                world2.EntityManager.CreateEntity(typeof(A19Data));
                world2.EntityManager.CreateEntity(typeof(A19Data));

                var q1 = _em.CreateEntityQuery(typeof(A19Data));
                var q2 = world2.EntityManager.CreateEntityQuery(typeof(A19Data));

                Debug.Log($"[A-19] World1 count={q1.CalculateEntityCount()}, " +
                          $"World2 count={q2.CalculateEntityCount()}");
                Assert.AreEqual(1, q1.CalculateEntityCount());
                Assert.AreEqual(2, q2.CalculateEntityCount());
            }
            finally
            {
                if (world2.IsCreated) world2.Dispose();
            }
        }

        [Test]
        public void QueryBuilder_WithOptions_Combines()
        {
            _em.CreateEntity(typeof(A19Data));
            _em.CreateEntity(typeof(A19Data), typeof(Prefab));

            var q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<A19Data>()
                .WithOptions(EntityQueryOptions.IncludePrefab)
                .Build(_em);

            Debug.Log($"[A-19] Builder.WithOptions(IncludePrefab) count={q.CalculateEntityCount()}");
            Assert.AreEqual(2, q.CalculateEntityCount());
        }
    }
}
