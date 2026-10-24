using ECSave.Internal;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests.Generator
{
    public struct GenFixedStringProfile : IComponentData
    {
        public FixedString32Bytes Name;
        public FixedString64Bytes Bio;
        public int Level;
    }

    public sealed class GenFixedStringScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenFixedStringProfile>();
            b.WorldId("GenFixedStringWorld");
        }
    }

    public sealed class GenFixedStringNoRepairScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenFixedStringProfile>();
            b.WorldId("GenFixedStringWorld");
            b.DisableAutoRepairFixedString();
        }
    }

    [TestFixture]
    public sealed class FixedStringAutoRepairTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenFixedStringWorld");
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
        public void NoWarning_WhenBelowCapacity()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                profile.Name = "あいうえお";
                profile.Bio = "短い自己紹介";
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsEmpty(FixedStringWarnings(load));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NoWarning_WhenEmptyString()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenFixedStringProfile { Level = 1 });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsEmpty(FixedStringWarnings(load));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NoWarning_WhenAsciiAtCapacity()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name,
                    Encoding.ASCII.GetBytes(new string('a', 29)), 29);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsEmpty(FixedStringWarnings(load));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void Warning_WhenJapaneseAtCapacity()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var content = "あああああああああaa";
                Assert.AreEqual(29, Encoding.UTF8.GetByteCount(content));

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, Encoding.UTF8.GetBytes(content), 29);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var warnings = FixedStringWarnings(load);
                Assert.IsNotEmpty(warnings);
                Assert.AreEqual(typeof(GenFixedStringProfile).FullName, warnings[0].TypeName);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void AutoRepair_WhenIncompleteUtf8AtEnd_LengthIsRepaired()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var broken = AWithPartialTail(9, 0xE3, 0x81);
                Assert.AreEqual(29, broken.Length);

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, broken, 29);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsNotEmpty(FixedStringWarnings(load));

                var q = em.CreateEntityQuery(typeof(GenFixedStringProfile));
                using var profiles = q.ToComponentDataArray<GenFixedStringProfile>(Allocator.Temp);
                Assert.AreEqual(1, profiles.Length);
                Assert.AreEqual(27, profiles[0].Name.Length);
                Assert.AreEqual("あああああああああ", profiles[0].Name.ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void AutoRepair_PartialHeadByteOnly_TruncatesCleanly()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var broken = AWithPartialTail(9, 0xE3);
                Assert.AreEqual(28, broken.Length);

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, broken, 28);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsNotEmpty(FixedStringWarnings(load));

                var q = em.CreateEntityQuery(typeof(GenFixedStringProfile));
                using var profiles = q.ToComponentDataArray<GenFixedStringProfile>(Allocator.Temp);
                Assert.AreEqual(27, profiles[0].Name.Length);
                Assert.AreEqual("あああああああああ", profiles[0].Name.ToString());
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void OptOut_WarningButNoRepair()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringNoRepairScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var broken = AWithPartialTail(9, 0xE3, 0x81);
                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, broken, 29);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsNotEmpty(FixedStringWarnings(load));

                var q = em.CreateEntityQuery(typeof(GenFixedStringProfile));
                using var profiles = q.ToComponentDataArray<GenFixedStringProfile>(Allocator.Temp);
                Assert.AreEqual(29, profiles[0].Name.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void DisableAutoRepair_SetsDefinitionFlag()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringNoRepairScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                Assert.IsFalse(sys.Definition.AutoRepairFixedString);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void Default_AutoRepairIsEnabled()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                Assert.IsTrue(sys.Definition.AutoRepairFixedString);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NameAtCapacity_OneWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name,
                    Encoding.UTF8.GetBytes("あああああああああaa"), 29);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void BioAtCapacity_OneWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                var bioContent = new string('あ', 20) + "a";
                SetFixedStringRaw(ref profile.Bio,
                    Encoding.UTF8.GetBytes(bioContent), 61);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void BothFieldsAtCapacity_OneWarningPerEntity()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name,
                    Encoding.UTF8.GetBytes("あああああああああaa"), 29);
                var bioContent = new string('あ', 20) + "a";
                SetFixedStringRaw(ref profile.Bio,
                    Encoding.UTF8.GetBytes(bioContent), 61);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void BothFieldsTruncated_OneWarningPerEntity_BothRepaired()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                SetFixedStringRaw(ref profile.Bio, AWithPartialTail(20, 0xE3), 61);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedStringWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(1, FixedStringWarnings(load).Count);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenFixedStringProfile));
                using var profiles = q.ToComponentDataArray<GenFixedStringProfile>(Allocator.Temp);
                Assert.AreEqual(27, profiles[0].Name.Length);
                Assert.AreEqual(60, profiles[0].Bio.Length);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void NoFixedString_NoWarning()
        {
            var scope = ScriptableObject.CreateInstance<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsEmpty(FixedStringWarnings(load));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void EndToEnd_BrokenSaveRecoveredOnLoad()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                for (int i = 0; i < 3; i++)
                {
                    var e = em.CreateEntity();
                    var profile = new GenFixedStringProfile { Level = i + 1 };
                    SetFixedStringRaw(ref profile.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                    em.AddComponentData(e, profile);
                }

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedStringWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.AreEqual(3, FixedStringWarnings(load).Count);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenFixedStringProfile));
                using var profiles = q.ToComponentDataArray<GenFixedStringProfile>(Allocator.Temp);
                Assert.AreEqual(3, profiles.Length);
                for (int i = 0; i < profiles.Length; i++)
                {
                    Assert.AreEqual(27, profiles[i].Name.Length);
                    Assert.AreEqual("あああああああああ", profiles[i].Name.ToString());
                }
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void EndToEnd_ReSaveAfterRepair_ProducesCleanData()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e, profile);

                var save1 = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save1.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedStringWorld");

                var load1 = sys.DeserializeAsync(_world, save1.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load1.IsSuccess);
                Assert.IsNotEmpty(FixedStringWarnings(load1));

                var save2 = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save2.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedStringWorld");

                var load2 = sys.DeserializeAsync(_world, save2.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load2.IsSuccess);
                Assert.IsEmpty(FixedStringWarnings(load2));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void GeneratedModule_ExposesFixedStringFields()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var modules = sys.Definition.Modules;

                SaveTypeModule found = null;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[i].FullTypeName == typeof(GenFixedStringProfile).FullName)
                    {
                        found = modules[i];
                        break;
                    }
                }
                Assert.IsNotNull(found);

                var fields = found.FixedStringFields;
                Assert.IsNotEmpty(fields);

                var nameField = fields.FirstOrDefault(f => f.Name == "Name");
                Assert.AreEqual(29, nameField.Capacity);
                Assert.AreEqual(0, nameField.Offset);

                var bioField = fields.FirstOrDefault(f => f.Name == "Bio");
                Assert.AreEqual(61, bioField.Capacity);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void SameInput_SameWarningCount()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedStringScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var profile = new GenFixedStringProfile { Level = 1 };
                SetFixedStringRaw(ref profile.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
                em.AddComponentData(e, profile);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

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
