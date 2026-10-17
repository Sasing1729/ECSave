using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using System;
using System.Reflection;

namespace ECSave.Tests
{
    // ---------------------------------------------------------------
    // A-28 用のテスト型（トップレベル）
    // ---------------------------------------------------------------
    public struct A28_TestComponent : IComponentData { public int Value; }
    public struct A28_TestTag : IComponentData { }

    // Source Generator が partial を処理するため、トップレベルに配置
    public partial class A28_TestSystem : SystemBase
    {
        public EntityQuery QueryFromSystemState { get; private set; }

        protected override void OnCreate()
        {
            QueryFromSystemState = GetEntityQuery(typeof(A28_TestComponent));
        }

        protected override void OnUpdate() { }
    }

    // ---------------------------------------------------------------
    // A-28: EntityQuery の World 所有判定
    // 目的: 利用者が作成した EntityQuery が、指定 World のものかを公開APIで判定できるか
    // ---------------------------------------------------------------
    [TestFixture]
    public class A28_QueryWorldOwnershipTests
    {
        private World _worldA;
        private World _worldB;

        [SetUp]
        public void SetUp()
        {
            _worldA = new World("A28_WorldA");
            _worldB = new World("A28_WorldB");
        }

        [TearDown]
        public void TearDown()
        {
            _worldA?.Dispose();
            _worldB?.Dispose();
        }

        // ---------------------------------------------------------------
        // Probe 01: EntityManager.World は公開されているか（既知の前提確認）
        // ---------------------------------------------------------------
        [Test]
        public void Probe01_EntityManager_World_IsPublic()
        {
            var emA = _worldA.EntityManager;
            var emB = _worldB.EntityManager;

            Assert.AreEqual(_worldA, emA.World);
            Assert.AreEqual(_worldB, emB.World);
            Assert.AreNotEqual(emA.World, emB.World);

            Debug.Log($"[A-28] emA.World={emA.World.Name}, emB.World={emB.World.Name}");
        }

        // ---------------------------------------------------------------
        // Probe 02: EntityManager.IsQueryValid という API は存在するか
        // ---------------------------------------------------------------
        [Test]
        public void Probe02_EntityManager_IsQueryValid_ExistenceCheck()
        {
            var method = typeof(EntityManager).GetMethod(
                "IsQueryValid",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(EntityQuery) },
                null);

            if (method == null)
            {
                Debug.Log("[A-28] EntityManager.IsQueryValid(EntityQuery) は存在しない");
            }
            else
            {
                Debug.Log($"[A-28] EntityManager.IsQueryValid(EntityQuery) が存在する: 戻り値={method.ReturnType.Name}");
            }
        }

        // ---------------------------------------------------------------
        // Probe 03: 別 World の EntityManager に EntityQuery を渡すと例外が出るか
        // ---------------------------------------------------------------
        [Test]
        public void Probe03_QueryFromA_UsedOnEntityManagerB_Throws()
        {
            var emA = _worldA.EntityManager;
            var emB = _worldB.EntityManager;

            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));

            try
            {
                emB.AddComponent<A28_TestTag>(queryA);
                Debug.Log("[A-28] emB.AddComponent(queryA) は例外を投げなかった");
            }
            catch (Exception ex)
            {
                Debug.Log($"[A-28] emB.AddComponent(queryA) が投げた: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                emB.RemoveComponent<A28_TestTag>(queryA);
                Debug.Log("[A-28] emB.RemoveComponent(queryA) は例外を投げなかった");
            }
            catch (Exception ex)
            {
                Debug.Log($"[A-28] emB.RemoveComponent(queryA) が投げた: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Probe 04: EntityQuery の Equals / == は World を区別するか
        // ---------------------------------------------------------------
        [Test]
        public void Probe04_QueryEquals_AcrossWorlds()
        {
            var emA = _worldA.EntityManager;
            var emB = _worldB.EntityManager;

            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));
            var queryB = emB.CreateEntityQuery(typeof(A28_TestComponent));

            bool eq = queryA.Equals(queryB);
            bool opEq = queryA == queryB;
            int hashA = queryA.GetHashCode();
            int hashB = queryB.GetHashCode();

            Debug.Log($"[A-28] queryA.Equals(queryB)={eq}, queryA==queryB={opEq}, hashA={hashA}, hashB={hashB}");

            var queryA2 = emA.CreateEntityQuery(typeof(A28_TestComponent));
            Debug.Log($"[A-28] queryA.Equals(queryA2)={queryA.Equals(queryA2)}, hashA={queryA.GetHashCode()}, hashA2={queryA2.GetHashCode()}");
        }

        // ---------------------------------------------------------------
        // Probe 05: EntityQuery の内部フィールドに World 相当があるか（調査用、本番では使わない）
        // ---------------------------------------------------------------
        [Test]
        public void Probe05_EntityQuery_InternalFields_Probe()
        {
            var type = typeof(EntityQuery);

            Debug.Log("[A-28] EntityQuery のプロパティ:");
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Debug.Log($"  - {p.Name} : {p.PropertyType.Name} (public={p.GetMethod?.IsPublic})");
            }

            Debug.Log("[A-28] EntityQuery のフィールド:");
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Debug.Log($"  - {f.Name} : {f.FieldType.Name} (public={f.IsPublic})");
            }
        }

        // ---------------------------------------------------------------
        // Probe 06: CalculateEntityCount は自 World のデータだけを数えるか
        // ---------------------------------------------------------------
        [Test]
        public void Probe06_CalculateEntityCount_CrossWorld_Isolation()
        {
            var emA = _worldA.EntityManager;
            var emB = _worldB.EntityManager;

            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));
            var queryB = emB.CreateEntityQuery(typeof(A28_TestComponent));

            emA.CreateEntity(typeof(A28_TestComponent));
            emA.CreateEntity(typeof(A28_TestComponent));
            emB.CreateEntity(typeof(A28_TestComponent));

            int countA = queryA.CalculateEntityCount();
            int countB = queryB.CalculateEntityCount();

            Debug.Log($"[A-28] queryA.CalculateEntityCount()={countA} (期待=2), queryB.CalculateEntityCount()={countB} (期待=1)");

            Assert.AreEqual(2, countA, "queryA は World A のデータのみを数えるべき");
            Assert.AreEqual(1, countB, "queryB は World B のデータのみを数えるべき");
        }

        // ---------------------------------------------------------------
        // Probe 07: EntityQuery.GetEntityQueryMask 経由で World 情報が取れるか
        // ---------------------------------------------------------------
        [Test]
        public void Probe07_QueryMask_Probe()
        {
            var emA = _worldA.EntityManager;
            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));

            var mask = queryA.GetEntityQueryMask();
            Debug.Log($"[A-28] mask.ToString()={mask}");

            var maskType = mask.GetType();
            foreach (var f in maskType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try
                {
                    var v = f.GetValue(mask);
                    Debug.Log($"  - mask.{f.Name} = {v}");
                }
                catch { }
            }
        }

        // ---------------------------------------------------------------
        // Probe 08: IsQueryValid の意味論 - 自 World のクエリ
        // ---------------------------------------------------------------
        [Test]
        public void Probe08_IsQueryValid_SameWorld()
        {
            var emA = _worldA.EntityManager;
            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));

            bool valid = emA.IsQueryValid(queryA);
            Debug.Log($"[A-28] emA.IsQueryValid(queryA)={valid} (期待=true)");

            Assert.IsTrue(valid, "自 World のクエリは valid であるべき");
        }

        // ---------------------------------------------------------------
        // Probe 09: IsQueryValid の意味論 - 別 World のクエリ
        // ---------------------------------------------------------------
        [Test]
        public void Probe09_IsQueryValid_CrossWorld()
        {
            var emA = _worldA.EntityManager;
            var emB = _worldB.EntityManager;
            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));

            bool validB = emB.IsQueryValid(queryA);
            Debug.Log($"[A-28] emB.IsQueryValid(queryA)={validB} (期待=false なら A 案成立)");
        }

        // ---------------------------------------------------------------
        // Probe 10: IsQueryValid の意味論 - Dispose 後のクエリ
        // ---------------------------------------------------------------
        [Test]
        public void Probe10_IsQueryValid_AfterDispose()
        {
            var emA = _worldA.EntityManager;
            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));

            bool before = emA.IsQueryValid(queryA);
            queryA.Dispose();
            bool after = emA.IsQueryValid(queryA);

            Debug.Log($"[A-28] before dispose={before}, after dispose={after} (期待=true, false)");
        }

        // ---------------------------------------------------------------
        // Probe 11: default(EntityQuery) を IsQueryValid に渡す
        // ---------------------------------------------------------------
        [Test]
        public void Probe11_IsQueryValid_DefaultEntityQuery()
        {
            var emA = _worldA.EntityManager;
            var defaultQuery = default(EntityQuery);

            try
            {
                bool valid = emA.IsQueryValid(defaultQuery);
                Debug.Log($"[A-28] emA.IsQueryValid(default(EntityQuery))={valid}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[A-28] emA.IsQueryValid(default(EntityQuery)) が投げた: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Probe 12: default(EntityQuery) を他 API に渡してクラッシュしないか
        // ---------------------------------------------------------------
        [Test]
        public void Probe12_DefaultQuery_SafetyProbe()
        {
            var defaultQuery = default(EntityQuery);

            try
            {
                int count = defaultQuery.CalculateEntityCount();
                Debug.Log($"[A-28] default(EntityQuery).CalculateEntityCount()={count}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[A-28] default(EntityQuery).CalculateEntityCount() が投げた: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                var arr = defaultQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                Debug.Log($"[A-28] default(EntityQuery).ToEntityArray()={arr.Length}");
                arr.Dispose();
            }
            catch (Exception ex)
            {
                Debug.Log($"[A-28] default(EntityQuery).ToEntityArray() が投げた: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                bool empty = defaultQuery.IsEmpty;
                Debug.Log($"[A-28] default(EntityQuery).IsEmpty={empty}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[A-28] default(EntityQuery).IsEmpty が投げた: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Probe 13: SystemState.GetEntityQuery 経由のクエリ
        // ---------------------------------------------------------------
        [Test]
        public void Probe13_IsQueryValid_FromSystemState()
        {
            var system = _worldA.CreateSystemManaged<A28_TestSystem>();
            var query = system.QueryFromSystemState;

            bool validA = _worldA.EntityManager.IsQueryValid(query);
            bool validB = _worldB.EntityManager.IsQueryValid(query);

            Debug.Log($"[A-28] SystemState.GetEntityQuery: emA.IsQueryValid={validA} (期待=true), emB.IsQueryValid={validB} (期待=false)");

            Assert.IsTrue(validA, "自 World の SystemState クエリは valid");
            Assert.IsFalse(validB, "別 World の SystemState クエリは invalid");
        }

        // ---------------------------------------------------------------
        // Probe 14: 引数検証の実装方針
        // ---------------------------------------------------------------
        [Test]
        public void Probe14_ArgumentException_Contract()
        {
            var emA = _worldA.EntityManager;
            var emB = _worldB.EntityManager;

            var queryA = emA.CreateEntityQuery(typeof(A28_TestComponent));
            Assert.IsTrue(emA.IsQueryValid(queryA), "自 World のクエリは valid");
            Assert.IsFalse(emB.IsQueryValid(queryA), "別 World のクエリは invalid");

            queryA.Dispose();
            Assert.IsFalse(emA.IsQueryValid(queryA), "Dispose 済みのクエリは invalid");

            var defaultQuery = default(EntityQuery);
            try
            {
                bool valid = emA.IsQueryValid(defaultQuery);
                Debug.Log($"[A-28] default(EntityQuery): IsQueryValid={valid}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[A-28] default(EntityQuery): IsQueryValid が例外 {ex.GetType().Name}");
            }
        }
    }
}