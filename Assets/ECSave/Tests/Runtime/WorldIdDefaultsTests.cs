using System.Text;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// Review #1: WorldId 未設定時のフォールバックと上限検証。
    /// </summary>
    public class WorldIdDefaultsTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("DefaultWorldName");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void EmptyWorldId_SaveLoadSucceeds_UsesWorldName()
        {
            // WorldId 未設定の SaveDefinition
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "");

            var sys = new SaveSystem(def);
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            // ヘッダの WorldId が World.Name になっている
            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);
            var actualWorldId = ReadWorldId(raw);
            Assert.AreEqual("DefaultWorldName", actualWorldId,
                "Empty WorldId should fall back to World.Name");

            // 同じ World にロードできる
            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
        }

        [Test]
        public void EmptyWorldId_DifferentWorldName_LoadFails()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "");

            var sys = new SaveSystem(def);
            var em = _world.EntityManager;
            em.CreateEntity().SetComponentDataSafe(new Health { Current = 1, Max = 2 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            // 別名の World でロード → WorldMismatch
            _world.Dispose();
            _world = new World("DifferentWorldName");

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.WorldMismatch, load.ErrorCode);
        }

        [Test]
        public void ExplicitWorldId_UsesExplicitValue()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "CustomWorldId");

            var sys = new SaveSystem(def);
            _world.EntityManager.CreateEntity();

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);
            Assert.AreEqual("CustomWorldId", ReadWorldId(raw));
        }

        [Test]
        public void WorldId_AtMaxUtf8Bytes_Valid()
        {
            // 1024 byte ちょうど（ASCII 1024 文字）
            string id = new string('A', ECSaveFormatConstants.MaxWorldIdLength);
            var impl = new SaveDefinitionBuilderImpl();
            Assert.DoesNotThrow(() => impl.WorldId(id));
        }

        [Test]
        public void WorldId_OverMaxUtf8Bytes_Throws()
        {
            string id = new string('A', ECSaveFormatConstants.MaxWorldIdLength + 1);
            var impl = new SaveDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(() => impl.WorldId(id));
            Assert.AreEqual("ECSAVE-R003", ex.Code);
        }

        [Test]
        public void WorldId_MultiByteUtf8OverLimit_Throws()
        {
            // 日本語 1 文字 = 3 byte。342 文字 = 1026 byte > 1024
            string id = new string('あ', 342);
            var impl = new SaveDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(() => impl.WorldId(id));
            Assert.AreEqual("ECSAVE-R003", ex.Code);
        }

        private static unsafe string ReadWorldId(byte[] raw)
        {
            fixed (byte* p = raw)
            {
                var reader = new FormatReader(new ByteView(p, raw.Length));
                var h = reader.ReadHeader();
                return h.WorldIdLength == 0 ? string.Empty
                    : Encoding.UTF8.GetString(h.WorldIdPtr, h.WorldIdLength);
            }
        }
    }

    internal static class EntityManagerTestExtensions
    {
        public static void SetComponentDataSafe<T>(this Entity e, T value)
            where T : unmanaged, IComponentData
        {
            // no-op: test helper for readability
        }
    }
}
