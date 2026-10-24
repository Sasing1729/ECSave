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
    public struct GenFixedListStringComp : IComponentData
    {
        public FixedList128Bytes<FixedString32Bytes> Items;
    }

    public sealed class GenFixedListStringScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenFixedListStringComp>();
            b.WorldId("GenFixedListStringWorld");
        }
    }

    public sealed class GenFixedListStringNoRepairScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenFixedListStringComp>();
            b.WorldId("GenFixedListStringWorld");
            b.DisableAutoRepairFixedString();
        }
    }

    public struct GenInnerStruct
    {
        public int Score;
        public FixedString32Bytes Label;
    }

    public struct GenFixedListOfStructComp : IComponentData
    {
        public FixedList512Bytes<GenInnerStruct> Items;
    }

    public sealed class GenFixedListOfStructScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenFixedListOfStructComp>();
            b.WorldId("GenFixedListStructWorld");
        }
    }

    public struct GenTwoLevelComp : IComponentData
    {
        public FixedList512Bytes<FixedList128Bytes<FixedString32Bytes>> Outer;
    }

    public sealed class GenTwoLevelScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenTwoLevelComp>();
            b.WorldId("GenTwoLevelWorld");
        }
    }

    public struct GenMixedComp : IComponentData
    {
        public FixedString32Bytes Name;
        public FixedList128Bytes<FixedString32Bytes> Items;
    }

    public sealed class GenMixedScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenMixedComp>();
            b.WorldId("GenMixedWorld");
        }
    }

    [TestFixture]
    public sealed class FixedListFixedStringRuntimeTests
    {
        private const int FixedStringElementDataOffset = 8;

        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenFixedListStringWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            _world = null;
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

        private static unsafe void CorruptFixedStringAt(byte* location, byte[] data, int length)
        {
            *(ushort*)location = (ushort)length;
            for (int i = 0; i < length && i < data.Length; i++)
                location[2 + i] = data[i];
        }

        private static unsafe byte* GetElementPtr(byte* listBase, int dataOffset, int stride, int index)
            => listBase + dataOffset + index * stride;

        private static List<LoadWarning> FixedStringWarnings(LoadResult load)
            => load.Warnings
                .Where(w => w.Kind == LoadWarningKind.FixedStringMayBeTruncated)
                .ToList();

        private static unsafe void SetFixedStringRaw(
            ref FixedString32Bytes fs, byte[] data, int length)
        {
            byte* dst = (byte*)UnsafeUtility.AddressOf(ref fs);
            CorruptFixedStringAt(dst, data, length);
        }

        [Test]
        public void FixedList_HealthyData_NoWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add("あいう");
                comp.Items.Add("かきくけこ");
                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsEmpty(FixedStringWarnings(load));

                var q = em.CreateEntityQuery(typeof(GenFixedListStringComp));
                using var comps = q.ToComponentDataArray<GenFixedListStringComp>(Allocator.Temp);
                Assert.AreEqual(1, comps.Length);
                Assert.AreEqual(2, comps[0].Items.Length);
                Assert.AreEqual("あいう", comps[0].Items[0].ToString());
                Assert.AreEqual("かきくけこ", comps[0].Items[1].ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FixedList_EmptyList_NoWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenFixedListStringComp());

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsEmpty(FixedStringWarnings(load));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_FirstElementCorrupted_Repaired()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add(default);
                comp.Items.Add(default);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListStringComp));
                using var comps = q.ToComponentDataArray<GenFixedListStringComp>(Allocator.Temp);
                Assert.AreEqual(2, comps[0].Items.Length);
                Assert.AreEqual(27, comps[0].Items[0].Length);
                Assert.AreEqual("あああああああああ", comps[0].Items[0].ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_SecondElementCorrupted_OnlySecondRepaired()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add("healthy");
                comp.Items.Add(default);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 1),
                    AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListStringComp));
                using var comps = q.ToComponentDataArray<GenFixedListStringComp>(Allocator.Temp);
                Assert.AreEqual("healthy", comps[0].Items[0].ToString());
                Assert.AreEqual(27, comps[0].Items[1].Length);
                Assert.AreEqual("あああああああああ", comps[0].Items[1].ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_AllElementsCorrupted_AllRepaired()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add(default);
                comp.Items.Add(default);
                comp.Items.Add(default);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3, 0x81), 29);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 1),
                    AWithPartialTail(9, 0xE3), 28);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 2),
                    AWithPartialTail(8, 0xE3, 0x81, 0x82, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListStringComp));
                using var comps = q.ToComponentDataArray<GenFixedListStringComp>(Allocator.Temp);
                Assert.AreEqual(3, comps[0].Items.Length);
                Assert.AreEqual(27, comps[0].Items[0].Length);
                Assert.AreEqual(27, comps[0].Items[1].Length);
                Assert.AreEqual(27, comps[0].Items[2].Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_LengthOverCapacity_Clamped()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add("a");
                comp.Items.Add("b");
                comp.Items.Add("c");

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                *(ushort*)listBase = 10;

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListStringComp));
                using var comps = q.ToComponentDataArray<GenFixedListStringComp>(Allocator.Temp);
                Assert.AreEqual(3, comps[0].Items.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_AtCapacityNonAscii_WarningNoRepair()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add(default);

                var content = "あああああああああaa";
                Assert.AreEqual(29, Encoding.UTF8.GetByteCount(content));

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 0),
                    Encoding.UTF8.GetBytes(content), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListStringComp));
                using var comps = q.ToComponentDataArray<GenFixedListStringComp>(Allocator.Temp);
                Assert.AreEqual(29, comps[0].Items[0].Length);
                Assert.AreEqual(content, comps[0].Items[0].ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_OptOut_WarningButNoRepair()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringNoRepairScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add(default);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListStringComp));
                using var comps = q.ToComponentDataArray<GenFixedListStringComp>(Allocator.Temp);
                Assert.AreEqual(29, comps[0].Items[0].Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_EndToEnd_ResaveCleansWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add(default);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save1 = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save1.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load1 = sys.DeserializeAsync(_world, save1.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load1.IsSuccess);
                Assert.IsNotEmpty(FixedStringWarnings(load1));

                var save2 = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save2.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load2 = sys.DeserializeAsync(_world, save2.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load2.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load2));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedList_MultipleEntities_EachWarned()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e1 = em.CreateEntity();
                var c1 = new GenFixedListStringComp();
                c1.Items.Add("healthy");
                em.AddComponentData(e1, c1);

                var e2 = em.CreateEntity();
                var c2 = new GenFixedListStringComp();
                c2.Items.Add(default);
                byte* l2 = (byte*)UnsafeUtility.AddressOf(ref c2.Items);
                CorruptFixedStringAt(GetElementPtr(l2, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e2, c2);

                var e3 = em.CreateEntity();
                var c3 = new GenFixedListStringComp();
                c3.Items.Add(default);
                byte* l3 = (byte*)UnsafeUtility.AddressOf(ref c3.Items);
                CorruptFixedStringAt(GetElementPtr(l3, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3), 28);
                em.AddComponentData(e3, c3);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(2, FixedStringWarnings(load).Count);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedListOfStruct_HealthyData_NoWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListOfStructScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListOfStructComp();
                comp.Items.Add(new GenInnerStruct { Score = 10, Label = "あいう" });
                comp.Items.Add(new GenInnerStruct { Score = 20, Label = "かきく" });
                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStructWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load));

                var q = em.CreateEntityQuery(typeof(GenFixedListOfStructComp));
                using var comps = q.ToComponentDataArray<GenFixedListOfStructComp>(Allocator.Temp);
                Assert.AreEqual(2, comps[0].Items.Length);
                Assert.AreEqual("あいう", comps[0].Items[0].Label.ToString());
                Assert.AreEqual(10, comps[0].Items[0].Score);
                Assert.AreEqual("かきく", comps[0].Items[1].Label.ToString());
                Assert.AreEqual(20, comps[0].Items[1].Score);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedListOfStruct_CorruptedLabel_Repaired()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListOfStructScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListOfStructComp();
                comp.Items.Add(new GenInnerStruct { Score = 10, Label = "healthy" });

                int structSize = UnsafeUtility.SizeOf<GenInnerStruct>();
                Assert.AreEqual(36, structSize);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                byte* labelPtr = GetElementPtr(listBase, 4, structSize, 0) + 4;
                CorruptFixedStringAt(labelPtr, AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStructWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListOfStructComp));
                using var comps = q.ToComponentDataArray<GenFixedListOfStructComp>(Allocator.Temp);
                Assert.AreEqual(27, comps[0].Items[0].Label.Length);
                Assert.AreEqual("あああああああああ", comps[0].Items[0].Label.ToString());
                Assert.AreEqual(10, comps[0].Items[0].Score);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedListOfStruct_MultipleElementsCorrupted_AllRepaired()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListOfStructScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListOfStructComp();
                comp.Items.Add(new GenInnerStruct { Score = 1 });
                comp.Items.Add(new GenInnerStruct { Score = 2 });
                comp.Items.Add(new GenInnerStruct { Score = 3 });

                int structSize = UnsafeUtility.SizeOf<GenInnerStruct>();
                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, 4, structSize, 0) + 4,
                    AWithPartialTail(9, 0xE3, 0x81), 29);
                CorruptFixedStringAt(GetElementPtr(listBase, 4, structSize, 2) + 4,
                    AWithPartialTail(9, 0xE3), 28);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStructWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenFixedListOfStructComp));
                using var comps = q.ToComponentDataArray<GenFixedListOfStructComp>(Allocator.Temp);
                Assert.AreEqual(27, comps[0].Items[0].Label.Length);
                Assert.AreEqual(27, comps[0].Items[2].Label.Length);
                Assert.AreEqual(1, comps[0].Items[0].Score);
                Assert.AreEqual(3, comps[0].Items[2].Score);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void FixedListOfStruct_EndToEnd_ResaveCleansWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListOfStructScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListOfStructComp();
                comp.Items.Add(new GenInnerStruct { Score = 10 });

                int structSize = UnsafeUtility.SizeOf<GenInnerStruct>();
                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, 4, structSize, 0) + 4,
                    AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save1 = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save1.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStructWorld");
                em = _world.EntityManager;

                var load1 = sys.DeserializeAsync(_world, save1.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load1.IsSuccess);
                Assert.IsNotEmpty(FixedStringWarnings(load1));

                var save2 = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save2.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStructWorld");
                em = _world.EntityManager;

                var load2 = sys.DeserializeAsync(_world, save2.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load2.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load2));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void TwoLevel_HealthyData_NoWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenTwoLevelScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenTwoLevelComp();

                var inner = new FixedList128Bytes<FixedString32Bytes>();
                inner.Add("hello");
                inner.Add("world");
                comp.Outer.Add(inner);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenTwoLevelWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void TwoLevel_InnerElementCorrupted_Repaired()
        {
            var scope = ScriptableObject.CreateInstance<GenTwoLevelScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenTwoLevelComp();

                var inner = new FixedList128Bytes<FixedString32Bytes>();
                inner.Add(default);
                comp.Outer.Add(inner);

                byte* outerBase = (byte*)UnsafeUtility.AddressOf(ref comp.Outer);
                byte* outerElem0 = GetElementPtr(outerBase, 8, 128, 0);
                byte* innerElem0 = GetElementPtr(outerElem0, 8, 32, 0);
                CorruptFixedStringAt(innerElem0, AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenTwoLevelWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenTwoLevelComp));
                using var comps = q.ToComponentDataArray<GenTwoLevelComp>(Allocator.Temp);
                Assert.AreEqual(1, comps[0].Outer.Length);
                Assert.AreEqual(1, comps[0].Outer[0].Length);
                Assert.AreEqual(27, comps[0].Outer[0][0].Length);
                Assert.AreEqual("あああああああああ", comps[0].Outer[0][0].ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void TwoLevel_InnerListLengthOverCapacity_Clamped()
        {
            var scope = ScriptableObject.CreateInstance<GenTwoLevelScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenTwoLevelComp();

                var inner = new FixedList128Bytes<FixedString32Bytes>();
                inner.Add("a");
                inner.Add("b");
                inner.Add("c");
                comp.Outer.Add(inner);

                byte* outerBase = (byte*)UnsafeUtility.AddressOf(ref comp.Outer);
                byte* outerElem0 = GetElementPtr(outerBase, 8, 128, 0);
                *(ushort*)outerElem0 = 10;

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenTwoLevelWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenTwoLevelComp));
                using var comps = q.ToComponentDataArray<GenTwoLevelComp>(Allocator.Temp);
                Assert.AreEqual(3, comps[0].Outer[0].Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void TwoLevel_OuterListLengthOverCapacity_Clamped()
        {
            var scope = ScriptableObject.CreateInstance<GenTwoLevelScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenTwoLevelComp();

                for (int i = 0; i < 3; i++)
                {
                    var inner = new FixedList128Bytes<FixedString32Bytes>();
                    inner.Add("x");
                    comp.Outer.Add(inner);
                }

                byte* outerBase = (byte*)UnsafeUtility.AddressOf(ref comp.Outer);
                *(ushort*)outerBase = 10;

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenTwoLevelWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenTwoLevelComp));
                using var comps = q.ToComponentDataArray<GenTwoLevelComp>(Allocator.Temp);
                Assert.AreEqual(3, comps[0].Outer.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void MixedSingleAndList_BothCorrupted_OneWarningPerEntity()
        {
            var scope = ScriptableObject.CreateInstance<GenMixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenMixedComp();
                comp.Items.Add(default);

                SetFixedStringRaw(ref comp.Name, AWithPartialTail(9, 0xE3, 0x81), 29);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3), 28);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenMixedWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenMixedComp));
                using var comps = q.ToComponentDataArray<GenMixedComp>(Allocator.Temp);
                Assert.AreEqual(27, comps[0].Name.Length);
                Assert.AreEqual(27, comps[0].Items[0].Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void MixedSingleAndList_OnlySingleCorrupted_OneWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenMixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenMixedComp();
                comp.Items.Add("healthy");
                SetFixedStringRaw(ref comp.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenMixedWorld");
                em = _world.EntityManager;

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = em.CreateEntityQuery(typeof(GenMixedComp));
                using var comps = q.ToComponentDataArray<GenMixedComp>(Allocator.Temp);
                Assert.AreEqual(27, comps[0].Name.Length);
                Assert.AreEqual("healthy", comps[0].Items[0].ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public unsafe void SameInput_SameWarningCount()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedListStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var comp = new GenFixedListStringComp();
                comp.Items.Add(default);

                byte* listBase = (byte*)UnsafeUtility.AddressOf(ref comp.Items);
                CorruptFixedStringAt(GetElementPtr(listBase, FixedStringElementDataOffset, 32, 0),
                    AWithPartialTail(9, 0xE3, 0x81), 29);

                em.AddComponentData(e, comp);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedListStringWorld");
                em = _world.EntityManager;

                var load1 = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load1.IsSuccess);
                int count1 = FixedStringWarnings(load1).Count;

                var load2 = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load2.IsSuccess);
                int count2 = FixedStringWarnings(load2).Count;

                Assert.AreEqual(count1, count2);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }
    }
}
