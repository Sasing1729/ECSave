using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using ECSave;
using ECSave.Internal;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public struct GenNestedInner : IComponentData
    {
        public FixedString32Bytes Label;
        public int Score;
    }

    public struct GenNestedOuter : IComponentData
    {
        public int Id;
        public GenNestedInner Inner;
        public FixedString64Bytes Direct;
    }

    public struct GenDeepA : IComponentData
    {
        public FixedString32Bytes Data;
    }
    public struct GenDeepB : IComponentData
    {
        public GenDeepA Inner;
    }
    public struct GenDeepC : IComponentData
    {
        public GenDeepB Mid;
    }

    public sealed class GenNestedFixedStringScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenNestedOuter>();
            b.WorldId("GenNestedFixedStringWorld");
        }
    }

    public sealed class GenDeepNestedScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenDeepC>();
            b.WorldId("GenDeepNestedWorld");
        }
    }

    public sealed class GenNestedNoRepairScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenNestedOuter>();
            b.WorldId("GenNestedFixedStringWorld");
            b.DisableAutoRepairFixedString();
        }
    }

    [TestFixture]
    public sealed class FixedStringNestedAutoRepairTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenNestedFixedStringWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            _world = null;
        }

        private static unsafe void SetFixedStringRaw(
            ref FixedString32Bytes fs, byte[] data, int length)
        {
            byte* dst = (byte*)UnsafeUtility.AddressOf(ref fs);
            *(ushort*)dst = (ushort)length;
            for (int i = 0; i < length && i < data.Length; i++)
                dst[2 + i] = data[i];
        }

        private static unsafe void SetFixedStringRaw(
            ref FixedString64Bytes fs, byte[] data, int length)
        {
            byte* dst = (byte*)UnsafeUtility.AddressOf(ref fs);
            *(ushort*)dst = (ushort)length;
            for (int i = 0; i < length && i < data.Length; i++)
                dst[2 + i] = data[i];
        }

        private static byte[] RepeatA(int count)
        {
            var data = new byte[count * 3];
            for (int i = 0; i < count; i++)
            {
                data[i * 3 + 0] = 0xE3;
                data[i * 3 + 1] = 0x81;
                data[i * 3 + 2] = 0x82;
            }
            return data;
        }

        private static byte[] AWithPartialTail(int count, params byte[] partial)
        {
            var body = RepeatA(count);
            var result = new byte[body.Length + partial.Length];
            System.Buffer.BlockCopy(body, 0, result, 0, body.Length);
            System.Buffer.BlockCopy(partial, 0, result, body.Length, partial.Length);
            return result;
        }

        private static List<LoadWarning> FixedStringWarnings(LoadResult load)
            => load.Warnings
                .Where(w => w.Kind == LoadWarningKind.FixedStringMayBeTruncated)
                .ToList();

        [Test]
        public void NestedFixedString_Depth1_Repaired()
        {
            var scope = ScriptableObject.CreateInstance<GenNestedFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenNestedOuter { Id = 1 };
                SetFixedStringRaw(ref profile.Inner.Label, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e, profile);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenNestedFixedStringWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsNotEmpty(FixedStringWarnings(load));

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNestedOuter));
                using var profiles = q.ToComponentDataArray<GenNestedOuter>(Allocator.Temp);
                Assert.AreEqual(1, profiles.Length);
                Assert.AreEqual(27, profiles[0].Inner.Label.Length);
                Assert.AreEqual("あああああああああ", profiles[0].Inner.Label.ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NestedFixedString_Depth3_Repaired()
        {
            var scope = ScriptableObject.CreateInstance<GenDeepNestedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = default(GenDeepC);
                SetFixedStringRaw(ref profile.Mid.Inner.Data, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e, profile);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenDeepNestedWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsNotEmpty(FixedStringWarnings(load));

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenDeepC));
                using var profiles = q.ToComponentDataArray<GenDeepC>(Allocator.Temp);
                Assert.AreEqual(27, profiles[0].Mid.Inner.Data.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NestedAndDirect_BothBroken_BothRepaired()
        {
            var scope = ScriptableObject.CreateInstance<GenNestedFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenNestedOuter { Id = 1 };
                SetFixedStringRaw(ref profile.Inner.Label, AWithPartialTail(9, 0xE3, 0x81), 29);
                SetFixedStringRaw(ref profile.Direct, AWithPartialTail(20, 0xE3), 61);
                em.AddComponentData(e, profile);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenNestedFixedStringWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNestedOuter));
                using var profiles = q.ToComponentDataArray<GenNestedOuter>(Allocator.Temp);
                Assert.AreEqual(27, profiles[0].Inner.Label.Length);
                Assert.AreEqual(60, profiles[0].Direct.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NestedAndDirect_OnlyNestedBroken_OnlyNestedRepaired()
        {
            var scope = ScriptableObject.CreateInstance<GenNestedFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenNestedOuter { Id = 1 };
                SetFixedStringRaw(ref profile.Inner.Label, AWithPartialTail(9, 0xE3, 0x81), 29);
                profile.Direct = "hello";
                em.AddComponentData(e, profile);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenNestedFixedStringWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var warnings = FixedStringWarnings(load);
                Assert.AreEqual(1, warnings.Count);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNestedOuter));
                using var profiles = q.ToComponentDataArray<GenNestedOuter>(Allocator.Temp);
                Assert.AreEqual(27, profiles[0].Inner.Label.Length);
                Assert.AreEqual("hello", profiles[0].Direct.ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NestedFixedString_OptOut_WarningButNoRepair()
        {
            var scope = ScriptableObject.CreateInstance<GenNestedNoRepairScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenNestedOuter { Id = 1 };
                SetFixedStringRaw(ref profile.Inner.Label, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e, profile);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenNestedFixedStringWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsNotEmpty(FixedStringWarnings(load));

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNestedOuter));
                using var profiles = q.ToComponentDataArray<GenNestedOuter>(Allocator.Temp);
                Assert.AreEqual(29, profiles[0].Inner.Label.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void GeneratedModule_NestedPathInMetadata()
        {
            var scope = ScriptableObject.CreateInstance<GenNestedFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var modules = sys.Definition.Modules;

                SaveTypeModule found = null;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[i].FullTypeName == typeof(GenNestedOuter).FullName)
                    {
                        found = modules[i];
                        break;
                    }
                }
                Assert.IsNotNull(found);

                var fields = found.FixedStringFields;
                Assert.GreaterOrEqual(fields.Length, 2);

                bool hasNestedPath = false;
                bool hasDirectPath = false;
                foreach (var f in fields)
                {
                    if (f.Name == "Inner.Label")
                    {
                        hasNestedPath = true;
                        Assert.AreEqual(4, f.Offset);
                        Assert.AreEqual(29, f.Capacity);
                    }
                    if (f.Name == "Direct")
                    {
                        hasDirectPath = true;
                        Assert.AreEqual(40, f.Offset);
                        Assert.AreEqual(61, f.Capacity);
                    }
                }
                Assert.IsTrue(hasNestedPath);
                Assert.IsTrue(hasDirectPath);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NestedFixedString_EndToEnd_ResaveCleansWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenNestedFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenNestedOuter { Id = 1 };
                SetFixedStringRaw(ref profile.Inner.Label, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e, profile);

                var save1 = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save1.IsSuccess);

                _world.Dispose();
                _world = new World("GenNestedFixedStringWorld");

                var load1 = sys.Deserialize(_world, save1.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load1.IsSuccess);
                Assert.IsNotEmpty(FixedStringWarnings(load1));

                var save2 = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save2.IsSuccess);

                _world.Dispose();
                _world = new World("GenNestedFixedStringWorld");

                var load2 = sys.Deserialize(_world, save2.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load2.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load2));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void MultipleEntities_OnlyOneBroken_OthersIntact()
        {
            var scope = ScriptableObject.CreateInstance<GenNestedFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e1 = em.CreateEntity();
                var p1 = new GenNestedOuter { Id = 1 };
                p1.Inner.Label = "ok";
                p1.Direct = "fine";
                em.AddComponentData(e1, p1);

                var e2 = em.CreateEntity();
                var p2 = new GenNestedOuter { Id = 2 };
                SetFixedStringRaw(ref p2.Inner.Label, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e2, p2);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenNestedFixedStringWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNestedOuter));
                using var profiles = q.ToComponentDataArray<GenNestedOuter>(Allocator.Temp);
                Assert.AreEqual(2, profiles.Length);

                int healthyCount = 0, repairedCount = 0;
                for (int i = 0; i < profiles.Length; i++)
                {
                    if (profiles[i].Id == 1 && profiles[i].Inner.Label.ToString() == "ok")
                        healthyCount++;
                    if (profiles[i].Id == 2 && profiles[i].Inner.Label.Length == 27)
                        repairedCount++;
                }
                Assert.AreEqual(1, healthyCount);
                Assert.AreEqual(1, repairedCount);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }
    }
}
