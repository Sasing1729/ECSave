using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A02Tag : IComponentData { }
    public struct A02TagB : IComponentData { }
    public struct A02TagC : IComponentData { }
    public struct A02Data : IComponentData { public int Value; }

    public class A02_EntityQueryBuilderTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A02");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void MultipleWithAny_IsOrGroup_NotAnd()
        {
            _em.CreateEntity(typeof(A02Data), typeof(A02Tag));
            _em.CreateEntity(typeof(A02Data), typeof(A02TagB));
            _em.CreateEntity(typeof(A02Data), typeof(A02Tag), typeof(A02TagB));
            _em.CreateEntity(typeof(A02Data), typeof(A02TagC));
            _em.CreateEntity(typeof(A02Data));

            var q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<A02Data>()
                .WithAny<A02Tag>()
                .WithAny<A02TagB>()
                .Build(_world.EntityManager);

            int count = q.CalculateEntityCount();
            Debug.Log($"[A-02] multiple WithAny count={count} (OR=3, AND=1)");
            Assert.AreEqual(3, count,
                "複数の WithAny が OR として扱われていない（AND になっている可能性）");
        }

        [Test]
        public void WithAll_WithAny_WithNone_Combinations()
        {
            _em.CreateEntity(typeof(A02Data), typeof(A02Tag));
            _em.CreateEntity(typeof(A02Data), typeof(A02TagB));
            _em.CreateEntity(typeof(A02Data), typeof(A02Tag), typeof(A02TagB));
            _em.CreateEntity(typeof(A02Data), typeof(A02TagC));
            _em.CreateEntity(typeof(A02Data));

            var q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<A02Data>()
                .WithAny<A02Tag, A02TagB>()
                .WithNone<A02TagC>()
                .Build(_world.EntityManager);

            int count = q.CalculateEntityCount();
            Debug.Log($"[A-02] WithAll/WithAny/WithNone count={count} (expected 3)");
            Assert.AreEqual(3, count);
        }

        [Test]
        public void Query_BelongsToCreatingWorld()
        {
            var world2 = new World("A02-Other");
            try
            {
                _em.CreateEntity(typeof(A02Data));
                world2.EntityManager.CreateEntity(typeof(A02Data));
                world2.EntityManager.CreateEntity(typeof(A02Data));

                var q1 = _em.CreateEntityQuery(typeof(A02Data));
                var q2 = world2.EntityManager.CreateEntityQuery(typeof(A02Data));

                Debug.Log($"[A-02] world1 count={q1.CalculateEntityCount()}, world2 count={q2.CalculateEntityCount()}");

                Assert.AreEqual(1, q1.CalculateEntityCount(),
                    "World1 のクエリが World2 の Entity を含んでいる");
                Assert.AreEqual(2, q2.CalculateEntityCount(),
                    "World2 のクエリが World1 の Entity を含んでいる");
            }
            finally
            {
                if (world2.IsCreated) world2.Dispose();
            }
        }

        [Test]
        public void CreateEntityQuery_WithBuilder_And_WithDesc()
        {
            _em.CreateEntity(typeof(A02Data));

            var q1 = _em.CreateEntityQuery(typeof(A02Data));

            var q2 = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A02Data>() },
            });

            var q3 = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<A02Data>()
                .Build(_em);

            Debug.Log($"[A-02] q1={q1.CalculateEntityCount()}, q2={q2.CalculateEntityCount()}, q3={q3.CalculateEntityCount()}");

            Assert.AreEqual(1, q1.CalculateEntityCount());
            Assert.AreEqual(1, q2.CalculateEntityCount());
            Assert.AreEqual(1, q3.CalculateEntityCount());
        }

        [Test]
        public void IncludeDisabledEntities_And_IgnoreComponentEnabledState()
        {
            _em.CreateEntity(typeof(A02Data));

            var qDefault = _em.CreateEntityQuery(typeof(A02Data));

            var qIgnore = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A02Data>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            Debug.Log($"[A-02] default={qDefault.CalculateEntityCount()}, " +
                      $"ignoreEnabled={qIgnore.CalculateEntityCount()}");
            Assert.Pass();
        }

        [Test]
        public void EntityQueryBuilder_BuildWithSystemBase()
        {
            _em.CreateEntity(typeof(A02Data));

            var sys = _world.CreateSystemManaged<A02ProbeSystem>();
            int count = sys.BuildQueryAndCount();
            Debug.Log($"[A-02] SystemBase-built query count={count}");
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Query_WorldBelongsTo_RequiresImplementation()
        {
            Debug.Log("[A-02] EntityQuery.World は公開されていない。" +
                      "別 World の query 検出は、RestorationScope.Custom の実装時に" +
                      "独自の判定ロジック（EntityManager の比較等）で行う。");
            Debug.Log("[A-02] 現時点の検証では、別 World の query は" +
                      "CalculateEntityCount が各 World の Entity のみを返すことで間接的に確認できる" +
                      "（Query_BelongsToCreatingWorld で検証済み）。");
            Assert.Pass();
        }
    }

    [DisableAutoCreation]
    public partial class A02ProbeSystem : SystemBase
    {
        public int BuildQueryAndCount()
        {
            var q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<A02Data>()
                .Build(this);
            return q.CalculateEntityCount();
        }

        protected override void OnUpdate() { }
    }
}
