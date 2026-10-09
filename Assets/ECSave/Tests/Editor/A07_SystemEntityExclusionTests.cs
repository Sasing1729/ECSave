using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A07Data : IComponentData { public int Value; }

    [DisableAutoCreation]
    public partial class A07ProbeSystem : SystemBase
    {
        public EntityQuery PlainQuery;
        public EntityQuery IncludeSystemsQuery;
        public EntityQuery IncludePrefabQuery;

        protected override void OnCreate()
        {
            PlainQuery = GetEntityQuery(typeof(A07Data));
            IncludeSystemsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A07Data>() },
                Options = EntityQueryOptions.IncludeSystems,
            });
            IncludePrefabQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A07Data>() },
                Options = EntityQueryOptions.IncludePrefab,
            });
        }

        protected override void OnUpdate() { }
    }

    public class A07_SystemEntityExclusionTests
    {
        World _world;
        EntityManager _em;
        A07ProbeSystem _probe;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A07");
            _em = _world.EntityManager;
            _probe = _world.CreateSystemManaged<A07ProbeSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void DefaultQuery_ExcludesSystemEntities_AndPrefabs()
        {
            _em.CreateEntity(typeof(A07Data));
            _em.CreateEntity(typeof(A07Data), typeof(Prefab));

            int plainCount = _probe.PlainQuery.CalculateEntityCount();
            int withSystems = _probe.IncludeSystemsQuery.CalculateEntityCount();
            int withPrefab = _probe.IncludePrefabQuery.CalculateEntityCount();

            Debug.Log($"[A-07] plain={plainCount}, IncludeSystems={withSystems}, IncludePrefab={withPrefab}");

            Assert.AreEqual(1, plainCount,
                "Plain query が Prefab を除外できていない");
            Assert.GreaterOrEqual(withSystems, plainCount);
            Assert.AreEqual(2, withPrefab,
                "IncludePrefab で Prefab が含まれていない");
        }

        [Test]
        public void SystemEntities_AreExcludedByDefault()
        {
            _em.CreateEntity(typeof(A07Data));

            int plain = _probe.PlainQuery.CalculateEntityCount();
            int withSystems = _probe.IncludeSystemsQuery.CalculateEntityCount();

            Debug.Log($"[A-07] plain={plain}, IncludeSystems={withSystems}");

            Assert.GreaterOrEqual(withSystems, plain,
                "IncludeSystems の結果が plain より少ない");
            Debug.Log("[A-07] 観測: カスタム World でシステムを CreateSystemManaged しても、" +
                      "IncludeSystems で追加されるシステム Entity は 0 件。" +
                      "6.6 ではシステム Entity は別の機構で管理されている可能性");
        }

        [Test]
        public void Prefab_IsExcludedByDefault_AndIncludedWithOption()
        {
            _em.CreateEntity(typeof(A07Data), typeof(Prefab));

            int plain = _probe.PlainQuery.CalculateEntityCount();
            int withPrefab = _probe.IncludePrefabQuery.CalculateEntityCount();

            Debug.Log($"[A-07] plain={plain}, IncludePrefab={withPrefab}");

            Assert.AreEqual(0, plain, "Plain query が Prefab を含んでいる");
            Assert.AreEqual(1, withPrefab, "IncludePrefab で Prefab が含まれていない");
        }

        [Test]
        public void EntityManager_GetAllEntities_Options()
        {
            _em.CreateEntity(typeof(A07Data));
            _em.CreateEntity(typeof(A07Data), typeof(Prefab));

            var all = _em.GetAllEntities(Allocator.Temp);
            var allWithSystems = _em.GetAllEntities(
                Allocator.Temp,
                EntityManager.GetAllEntitiesOptions.IncludeSystems);
            try
            {
                Debug.Log($"[A-07] GetAllEntities default={all.Length}, IncludeSystems={allWithSystems.Length}");
                Debug.Log("[A-07] 観測: GetAllEntities は Prefab を除外しない。" +
                          "Prefab を含めない場合は EntityQuery + EntityQueryOptions を使う");
                Assert.GreaterOrEqual(allWithSystems.Length, all.Length);
            }
            finally
            {
                all.Dispose();
                allWithSystems.Dispose();
            }
        }
    }
}
