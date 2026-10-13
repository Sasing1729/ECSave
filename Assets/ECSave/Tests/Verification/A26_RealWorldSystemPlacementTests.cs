using NUnit.Framework;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public static class A26OrderLog
    {
        public static readonly List<string> Entries = new List<string>();
        public static void Record(string name) { Entries.Add(name); }
        public static void Reset() { Entries.Clear(); }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [DisableAutoCreation]
    public partial class A26EcsaveSnapshotGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(A26EcsaveSnapshotGroup))]
    [DisableAutoCreation]
    public partial class A26SnapshotSystem : SystemBase
    {
        public static int UpdateCount;
        protected override void OnUpdate()
        {
            A26OrderLog.Record(nameof(A26SnapshotSystem));
            UpdateCount++;
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [DisableAutoCreation]
    public partial class A26EarlyPeer : SystemBase
    {
        public static int UpdateCount;
        protected override void OnUpdate()
        {
            A26OrderLog.Record(nameof(A26EarlyPeer));
            UpdateCount++;
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(A26EcsaveSnapshotGroup))]
    [DisableAutoCreation]
    public partial class A26AfterEcsave : SystemBase
    {
        public static int UpdateCount;
        protected override void OnUpdate()
        {
            A26OrderLog.Record(nameof(A26AfterEcsave));
            UpdateCount++;
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [DisableAutoCreation]
    public partial class A26DefaultBucket : SystemBase
    {
        public static int UpdateCount;
        protected override void OnUpdate()
        {
            A26OrderLog.Record(nameof(A26DefaultBucket));
            UpdateCount++;
        }
    }

    public class A26_RealWorldSystemPlacementTests
    {
        World _world;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A26-Real");
            _world.CreateSystemManaged<InitializationSystemGroup>();

            A26SnapshotSystem.UpdateCount = 0;
            A26EarlyPeer.UpdateCount = 0;
            A26AfterEcsave.UpdateCount = 0;
            A26DefaultBucket.UpdateCount = 0;
            A26OrderLog.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        void AddSystems()
        {
            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();

            var snapGroup = _world.CreateSystemManaged<A26EcsaveSnapshotGroup>();
            initGroup.AddSystemToUpdateList(snapGroup);

            var snapSys = _world.CreateSystemManaged<A26SnapshotSystem>();
            snapGroup.AddSystemToUpdateList(snapSys);

            var early = _world.CreateSystemManaged<A26EarlyPeer>();
            initGroup.AddSystemToUpdateList(early);

            var after = _world.CreateSystemManaged<A26AfterEcsave>();
            initGroup.AddSystemToUpdateList(after);

            var def = _world.CreateSystemManaged<A26DefaultBucket>();
            initGroup.AddSystemToUpdateList(def);

            initGroup.SortSystems();
            snapGroup.SortSystems();
        }

        [Test]
        public void EcsaveSnapshotGroup_ContainsSnapshotSystem()
        {
            AddSystems();

            var snapGroup = _world.GetExistingSystemManaged<A26EcsaveSnapshotGroup>();
            Assert.IsNotNull(snapGroup);

            snapGroup.Update();

            Debug.Log($"[A-26] SnapshotGroup.Update() 後の SnapshotSystem.UpdateCount={A26SnapshotSystem.UpdateCount}");
            Assert.AreEqual(1, A26SnapshotSystem.UpdateCount,
                "SnapshotSystem が SnapshotGroup に登録されていない（Update が呼ばれない）");
        }

        [Test]
        public void Update_InitializationGroup_InvokesAllSystems()
        {
            AddSystems();

            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            initGroup.Update();

            Debug.Log($"[A-26] SnapshotSystem={A26SnapshotSystem.UpdateCount}, " +
                      $"EarlyPeer={A26EarlyPeer.UpdateCount}, " +
                      $"AfterEcsave={A26AfterEcsave.UpdateCount}, " +
                      $"DefaultBucket={A26DefaultBucket.UpdateCount}");
            Debug.Log($"[A-26] execution order: {string.Join(", ", A26OrderLog.Entries)}");

            Assert.AreEqual(1, A26SnapshotSystem.UpdateCount);
            Assert.AreEqual(1, A26EarlyPeer.UpdateCount);
            Assert.AreEqual(1, A26AfterEcsave.UpdateCount);
            Assert.AreEqual(1, A26DefaultBucket.UpdateCount);
        }

        [Test]
        public void RealWorld_OrderFirst_vs_DefaultBucket()
        {
            AddSystems();

            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            initGroup.Update();

            Debug.Log($"[A-26] execution order: {string.Join(", ", A26OrderLog.Entries)}");

            var snapIdx = A26OrderLog.Entries.IndexOf(nameof(A26SnapshotSystem));
            var earlyIdx = A26OrderLog.Entries.IndexOf(nameof(A26EarlyPeer));
            var defaultIdx = A26OrderLog.Entries.IndexOf(nameof(A26DefaultBucket));

            Assert.IsTrue(snapIdx >= 0 && earlyIdx >= 0 && defaultIdx >= 0);
            Assert.Less(snapIdx, defaultIdx,
                "OrderFirst の SnapshotSystem が Default bucket より後に実行された");
            Assert.Less(earlyIdx, defaultIdx,
                "OrderFirst の EarlyPeer が Default bucket より後に実行された");
        }

        [Test]
        public void DefaultGameObjectInjectionWorld_SnapshotGroupPlacement()
        {
            var defaultWorld = World.DefaultGameObjectInjectionWorld;
            Assert.IsNotNull(defaultWorld, "DefaultGameObjectInjectionWorld が存在しない");
            Assert.IsTrue(defaultWorld.IsCreated);

            var initGroup = defaultWorld.GetExistingSystemManaged<InitializationSystemGroup>();
            Assert.IsNotNull(initGroup,
                "Default World に InitializationSystemGroup が自動追加されていない");

            Debug.Log($"[A-26] Default World の InitializationSystemGroup: {initGroup.GetType().Name}");
            Debug.Log("[A-26] Default World では InitializationSystemGroup が自動で提供される。" +
                      "ECSaveSnapshotGroup は本番環境で [UpdateInGroup] 属性により自動配置される");
        }

        [Test]
        public void SubGroup_NotAffectedByOuterUpdateAfter()
        {
            AddSystems();

            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            initGroup.Update();

            Debug.Log($"[A-26] execution order: {string.Join(", ", A26OrderLog.Entries)}");

            var snapIdx = A26OrderLog.Entries.IndexOf(nameof(A26SnapshotSystem));
            var afterIdx = A26OrderLog.Entries.IndexOf(nameof(A26AfterEcsave));

            Assert.IsTrue(snapIdx >= 0 && afterIdx >= 0);
            Assert.Less(snapIdx, afterIdx,
                "SnapshotSystem が AfterEcsave より後に実行された");
            Debug.Log("[A-26] 観測: Default bucket の [UpdateAfter(SnapshotGroup)] は、" +
                      "SnapshotGroup の内部システム (SnapshotSystem) の完了を保証しない可能性。" +
                      "厳密な順序制約にはグループ単位の操作が必要");
        }
    }
}
