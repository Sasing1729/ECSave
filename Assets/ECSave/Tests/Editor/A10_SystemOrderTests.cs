using NUnit.Framework;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public static class A10OrderLog
    {
        public static readonly List<string> Entries = new List<string>();
        public static void Record(string name) { Entries.Add(name); }
        public static void Reset() { Entries.Clear(); }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [DisableAutoCreation]
    public partial class A10EcsaveSnapshotGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [DisableAutoCreation]
    public partial class A10EarlyPeer : SystemBase
    {
        protected override void OnUpdate() { A10OrderLog.Record(nameof(A10EarlyPeer)); }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(A10EarlyPeer))]
    [DisableAutoCreation]
    public partial class A10EarlyPeerAfter : SystemBase
    {
        protected override void OnUpdate() { A10OrderLog.Record(nameof(A10EarlyPeerAfter)); }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(A10EarlyPeer))]
    [DisableAutoCreation]
    public partial class A10EarlyPeerBefore : SystemBase
    {
        protected override void OnUpdate() { A10OrderLog.Record(nameof(A10EarlyPeerBefore)); }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [DisableAutoCreation]
    public partial class A10Default : SystemBase
    {
        protected override void OnUpdate() { A10OrderLog.Record(nameof(A10Default)); }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(A10EarlyPeer))]
    [DisableAutoCreation]
    public partial class A10DefaultAfterEarly : SystemBase
    {
        protected override void OnUpdate() { A10OrderLog.Record(nameof(A10DefaultAfterEarly)); }
    }

    public class A10_SystemOrderTests
    {
        World _world;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A10");
            A10OrderLog.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        void AddSystems()
        {
            var initGroup = _world.CreateSystemManaged<InitializationSystemGroup>();

            var snapshot = _world.CreateSystemManaged<A10EcsaveSnapshotGroup>();
            initGroup.AddSystemToUpdateList(snapshot);

            var earlyPeer = _world.CreateSystemManaged<A10EarlyPeer>();
            initGroup.AddSystemToUpdateList(earlyPeer);

            var earlyPeerAfter = _world.CreateSystemManaged<A10EarlyPeerAfter>();
            initGroup.AddSystemToUpdateList(earlyPeerAfter);

            var earlyPeerBefore = _world.CreateSystemManaged<A10EarlyPeerBefore>();
            initGroup.AddSystemToUpdateList(earlyPeerBefore);

            var defaultSys = _world.CreateSystemManaged<A10Default>();
            initGroup.AddSystemToUpdateList(defaultSys);

            var defaultAfterEarly = _world.CreateSystemManaged<A10DefaultAfterEarly>();
            initGroup.AddSystemToUpdateList(defaultAfterEarly);

            initGroup.SortSystems();
        }

        [Test]
        public void InitializationSystemGroup_Update_InvokesChildren()
        {
            AddSystems();
            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            Assert.IsNotNull(initGroup);

            initGroup.Update();

            Debug.Log($"[A-10] execution order: {string.Join(", ", A10OrderLog.Entries)}");
            Assert.Greater(A10OrderLog.Entries.Count, 0,
                "InitializationSystemGroup.Update() で子システムが実行されていない");
        }

        [Test]
        public void OrderFirst_RunsBefore_DefaultBucket()
        {
            AddSystems();
            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            initGroup.Update();

            Debug.Log($"[A-10] execution order: {string.Join(", ", A10OrderLog.Entries)}");

            var iEarly = A10OrderLog.Entries.IndexOf(nameof(A10EarlyPeer));
            var iDefault = A10OrderLog.Entries.IndexOf(nameof(A10Default));

            Assert.GreaterOrEqual(iEarly, 0, "A10EarlyPeer (OrderFirst) が実行されなかった");
            Assert.GreaterOrEqual(iDefault, 0, "A10Default (Default bucket) が実行されなかった");
            Assert.Less(iEarly, iDefault,
                "OrderFirst のシステムが Default bucket より後に実行された");
        }

        [Test]
        public void WithinOrderFirstBucket_UpdateBefore_UpdateAfter_Work()
        {
            AddSystems();
            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            initGroup.Update();

            Debug.Log($"[A-10] execution order: {string.Join(", ", A10OrderLog.Entries)}");

            var iBefore = A10OrderLog.Entries.IndexOf(nameof(A10EarlyPeerBefore));
            var iPeer = A10OrderLog.Entries.IndexOf(nameof(A10EarlyPeer));
            var iAfter = A10OrderLog.Entries.IndexOf(nameof(A10EarlyPeerAfter));

            Assert.IsTrue(iBefore >= 0 && iPeer >= 0 && iAfter >= 0,
                $"Before={iBefore}, Peer={iPeer}, After={iAfter} のいずれかが実行されなかった");
            Assert.Less(iBefore, iPeer, "OrderFirst bucket 内で UpdateBefore が効いていない");
            Assert.Less(iPeer, iAfter, "OrderFirst bucket 内で UpdateAfter が効いていない");
        }

        [Test]
        public void DefaultBucket_UpdateAfter_OrderFirst_Works()
        {
            AddSystems();
            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            initGroup.Update();

            Debug.Log($"[A-10] execution order: {string.Join(", ", A10OrderLog.Entries)}");

            var iEarly = A10OrderLog.Entries.IndexOf(nameof(A10EarlyPeer));
            var iDefaultAfter = A10OrderLog.Entries.IndexOf(nameof(A10DefaultAfterEarly));

            Assert.GreaterOrEqual(iEarly, 0);
            Assert.GreaterOrEqual(iDefaultAfter, 0);
            Assert.Less(iEarly, iDefaultAfter,
                "Default bucket の UpdateAfter(OrderFirst) が OrderFirst を Default より前に配置できていない");
        }

        [Test]
        public void Note_ActualECSaveSnapshotGroup_Placement()
        {
            AddSystems();
            var initGroup = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            initGroup.Update();

            Debug.Log("[A-10] 観測: OrderFirst bucket 内の A10EcsaveSnapshotGroup は " +
                      "ゲーム側 Default bucket の UpdateAfter 対象になり得る。");
            Debug.Log("[A-10] 設計への示唆: 外部設計 12.6.1 の [UpdateAfter(ECSaveSnapshotGroup)] は、" +
                      "InitializationSystemGroup の直接の子にのみ効く。" +
                      "サブグループ内のシステムには効かないため、グループ単位の制約が必要。");
            Assert.Pass();
        }
    }
}
