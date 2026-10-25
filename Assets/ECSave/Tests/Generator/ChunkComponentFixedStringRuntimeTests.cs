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
    public struct GenChunkFixedString : IComponentData
    {
        public FixedString32Bytes Name;
        public int Value;
    }

    public struct GenChunkFixedList : IComponentData
    {
        public FixedList128Bytes<FixedString32Bytes> Items;
        public int Value;
    }

    public struct GenChunkMarkerA : IComponentData { }
    public struct GenChunkMarkerB : IComponentData { }

    public sealed class GenChunkFixedStringScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterComponent<GenFixedStringProfile>();
            b.RegisterChunkComponent<GenChunkFixedString>();
            b.RegisterChunkComponent<GenChunkFixedList>();
            b.WorldId("GenChunkFixedStringWorld");
        }
    }

    public sealed class GenChunkFixedStringNoRepairScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterChunkComponent<GenChunkFixedString>();
            b.RegisterChunkComponent<GenChunkFixedList>();
            b.WorldId("GenChunkFixedStringWorld");
            b.DisableAutoRepairFixedString();
        }
    }

    [TestFixture]
    public sealed class ChunkComponentFixedStringRuntimeTests
    {
        private const int FixedStringElementDataOffset = 8;
        private const int FixedStringElementStride = 32;

        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenChunkFixedStringWorld");
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

        private static unsafe void SetFixedStringRaw(
            ref FixedString32Bytes fs, byte[] data, int length)
        {
            byte* dst = (byte*)UnsafeUtility.AddressOf(ref fs);
            CorruptFixedStringAt(dst, data, length);
        }

        private static List<LoadWarning> FixedStringWarnings(LoadResult load)
            => load.Warnings
                .Where(w => w.Kind == LoadWarningKind.FixedStringMayBeTruncated)
                .ToList();

        private static EntityQuery ChunkFixedStringQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<GenChunkFixedString>() },
            });

        private static EntityQuery ChunkFixedListQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<GenChunkFixedList>() },
            });

        private static GenChunkFixedString GetChunkFixedString(EntityManager em)
        {
            var q = ChunkFixedStringQuery(em);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.Greater(chunks.Length, 0);
            var entities = chunks[0].GetNativeArray(em.GetEntityTypeHandle());
            Assert.Greater(entities.Length, 0);
            return em.GetChunkComponentData<GenChunkFixedString>(entities[0]);
        }

        private static GenChunkFixedList GetChunkFixedList(EntityManager em)
        {
            var q = ChunkFixedListQuery(em);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.Greater(chunks.Length, 0);
            var entities = chunks[0].GetNativeArray(em.GetEntityTypeHandle());
            Assert.Greater(entities.Length, 0);
            return em.GetChunkComponentData<GenChunkFixedList>(entities[0]);
        }

        [Test]
        public void ChunkComponent_FixedString_RoundTrip_ValueRestored()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedString>(e);
                var chunk = em.GetChunk(e);
                em.SetChunkComponentData(chunk, new GenChunkFixedString
                {
                    Name = "hello",
                    Value = 42,
                });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var restored = GetChunkFixedString(em);
                Assert.AreEqual("hello", restored.Name.ToString());
                Assert.AreEqual(42, restored.Value);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_FixedList_RoundTrip_ValueRestored()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedList>(e);
                var chunk = em.GetChunk(e);

                var value = new GenChunkFixedList { Value = 99 };
                value.Items.Add("abc");
                value.Items.Add("def");
                em.SetChunkComponentData(chunk, value);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var restored = GetChunkFixedList(em);
                Assert.AreEqual(2, restored.Items.Length);
                Assert.AreEqual("abc", restored.Items[0].ToString());
                Assert.AreEqual("def", restored.Items[1].ToString());
                Assert.AreEqual(99, restored.Value);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_FixedString_HealthyData_NoWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedString>(e);
                var chunk = em.GetChunk(e);
                em.SetChunkComponentData(chunk, new GenChunkFixedString
                {
                    Name = "healthy",
                    Value = 42,
                });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load));

                var restored = GetChunkFixedString(em);
                Assert.AreEqual("healthy", restored.Name.ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_FixedString_Corrupted_Repaired()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedString>(e);
                var chunk = em.GetChunk(e);

                var broken = new GenChunkFixedString { Value = 42 };
                SetFixedStringRaw(ref broken.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.SetChunkComponentData(chunk, broken);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var restored = GetChunkFixedString(em);
                Assert.AreEqual(27, restored.Name.Length);
                Assert.AreEqual("あああああああああ", restored.Name.ToString());
                Assert.AreEqual(42, restored.Value);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_FixedList_Corrupted_Repaired()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedList>(e);
                var chunk = em.GetChunk(e);

                var broken = new GenChunkFixedList { Value = 99 };
                broken.Items.Add(default);
                broken.Items.Add(default);

                unsafe
                {
                    byte* listBase = (byte*)UnsafeUtility.AddressOf(ref broken.Items);
                    CorruptFixedStringAt(listBase + FixedStringElementDataOffset + 0 * FixedStringElementStride,
                        AWithPartialTail(9, 0xE3, 0x81), 29);
                    CorruptFixedStringAt(listBase + FixedStringElementDataOffset + 1 * FixedStringElementStride,
                        AWithPartialTail(9, 0xE3), 28);
                }

                em.SetChunkComponentData(chunk, broken);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var restored = GetChunkFixedList(em);
                Assert.AreEqual(2, restored.Items.Length);
                Assert.AreEqual(27, restored.Items[0].Length);
                Assert.AreEqual(27, restored.Items[1].Length);
                Assert.AreEqual(99, restored.Value);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_MultipleChunks_EachWarned()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e1 = em.CreateEntity(typeof(GenHealth), typeof(GenChunkMarkerA));
                em.AddChunkComponentData<GenChunkFixedString>(e1);
                var chunk1 = em.GetChunk(e1);
                var broken1 = new GenChunkFixedString { Value = 1 };
                SetFixedStringRaw(ref broken1.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.SetChunkComponentData(chunk1, broken1);

                var e2 = em.CreateEntity(typeof(GenHealth), typeof(GenChunkMarkerB));
                em.AddChunkComponentData<GenChunkFixedString>(e2);
                var chunk2 = em.GetChunk(e2);
                var broken2 = new GenChunkFixedString { Value = 2 };
                SetFixedStringRaw(ref broken2.Name, AWithPartialTail(9, 0xE3), 28);
                em.SetChunkComponentData(chunk2, broken2);

                Assert.AreNotEqual(chunk1, chunk2);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(2, FixedStringWarnings(load).Count);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_MixedWithCommon_BothWarned()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedString>(e);
                var chunk = em.GetChunk(e);
                var broken = new GenChunkFixedString { Value = 1 };
                SetFixedStringRaw(ref broken.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.SetChunkComponentData(chunk, broken);

                var commonEntity = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(commonEntity, profile);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(2, FixedStringWarnings(load).Count);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_OptOut_WarningButNoRepair()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringNoRepairScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedString>(e);
                var chunk = em.GetChunk(e);
                var broken = new GenChunkFixedString { Value = 1 };
                SetFixedStringRaw(ref broken.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.SetChunkComponentData(chunk, broken);

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var restored = GetChunkFixedString(em);
                Assert.AreEqual(29, restored.Name.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void ChunkComponent_EndToEnd_ResaveCleansWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenChunkFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity(typeof(GenHealth));
                em.AddChunkComponentData<GenChunkFixedString>(e);
                var chunk = em.GetChunk(e);
                var broken = new GenChunkFixedString { Value = 1 };
                SetFixedStringRaw(ref broken.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.SetChunkComponentData(chunk, broken);

                var save1 = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save1.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load1 = sys.Deserialize(_world, save1.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load1.IsSuccess);
                Assert.IsNotEmpty(FixedStringWarnings(load1));

                var save2 = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save2.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkFixedStringWorld");
                em = _world.EntityManager;

                var load2 = sys.Deserialize(_world, save2.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load2.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load2));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }
    }
}
