using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public sealed class GenPrefabIdentityScope : SaveScope
    {
        public static Func<Entity, PrefabIdentityInfo?> NextProvider = _ => null;

        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterIdentity<GenPlayerId>();
            b.WorldId("GenPrefabIdentityWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.WithPrefabIdentityProvider(e => NextProvider(e));
        }
    }

    public sealed class GenNoPrefabIdentityScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterIdentity<GenPlayerId>();
            b.WorldId("GenPrefabIdentityWorld");
        }
    }

    [TestFixture]
    public sealed class SubScenePrefabIdentityEndToEndTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenPrefabIdentityWorld");
            GenPrefabIdentityScope.NextProvider = _ => null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        // 非イテレータメソッドなので unsafe 使用可
        private static bool HasPrefabIdentitySection(byte[] saveData)
        {
            var err = SaveCompression.TryDecompress(saveData, out var raw);
            if (err != SaveLoadErrorCode.None) return false;

            unsafe
            {
                fixed (byte* p = raw)
                {
                    try
                    {
                        var reader = new FormatReader(new ByteView(p, raw.Length));
                        reader.ReadHeader();
                        var table = reader.ReadSectionTable();
                        for (int i = 0; i < table.Count; i++)
                        {
                            if (table.Entries[i].Kind == SectionKind.PrefabIdentity) return true;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        // 非イテレータメソッド：PrefabIdentity セクションの Count を破壊
        private static void CorruptPrefabIdentityCount(byte[] raw)
        {
            unsafe
            {
                fixed (byte* p = raw)
                {
                    var reader = new FormatReader(new ByteView(p, raw.Length));
                    reader.ReadHeader();
                    var table = reader.ReadSectionTable();
                    for (int i = 0; i < table.Count; i++)
                    {
                        if (table.Entries[i].Kind != SectionKind.PrefabIdentity) continue;
                        byte* body = p + (int)table.Entries[i].Offset;
                        *(uint*)body = 1000000;
                        break;
                    }
                }
            }
        }

        // 非イテレータメソッド：チェックサムを再計算
        private static void RecomputeChecksum(byte[] raw)
        {
            unsafe
            {
                fixed (byte* p = raw)
                {
                    int footerOff = raw.Length - ECSaveFormatConstants.FooterSize;
                    ulong ck = ChecksumCalculator.Compute(new ByteView(p, footerOff));
                    *(ulong*)(p + footerOff) = ck;
                }
            }
        }

        [UnityTest]
        public IEnumerator ProviderReturnsValid_PrefabIdentitySectionWritten()
        {
            GenPrefabIdentityScope.NextProvider = _ =>
                new PrefabIdentityInfo(0xAAAA, 0xBBBB);

            var scope = ScriptableObject.CreateInstance<GenPrefabIdentityScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);
                Assert.That(HasPrefabIdentitySection(save.Data), Is.True,
                    "PrefabIdentity section should be written when provider returns valid info");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ProviderReturnsNull_NoPrefabIdentitySection()
        {
            GenPrefabIdentityScope.NextProvider = _ => null;

            var scope = ScriptableObject.CreateInstance<GenPrefabIdentityScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);
                Assert.That(HasPrefabIdentitySection(save.Data), Is.False,
                    "PrefabIdentity section should not be written when provider returns null");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ProviderDisabled_NoPrefabIdentitySection()
        {
            var scope = ScriptableObject.CreateInstance<GenNoPrefabIdentityScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);
                Assert.That(HasPrefabIdentitySection(save.Data), Is.False);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_WithPrefabIdentity_LoadSucceeds()
        {
            GenPrefabIdentityScope.NextProvider = _ =>
                new PrefabIdentityInfo(0xAAAA, 0xBBBB);

            var scope = ScriptableObject.CreateInstance<GenPrefabIdentityScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenPrefabIdentityWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.True,
                    "Load should succeed when PrefabIdentity section exists (S2b-1 verifies only)");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator CorruptPrefabIdentitySection_ReturnsCorruptedData()
        {
            GenPrefabIdentityScope.NextProvider = _ =>
                new PrefabIdentityInfo(0xAAAA, 0xBBBB);

            var scope = ScriptableObject.CreateInstance<GenPrefabIdentityScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                var err = SaveCompression.TryDecompress(save.Data, out var raw);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));

                // unsafe 処理は非イテレータヘルパに委譲
                CorruptPrefabIdentityCount(raw);
                RecomputeChecksum(raw);

                _world.Dispose();
                _world = new World("GenPrefabIdentityWorld");

                var load = sys.Deserialize(_world, raw,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.False);
                Assert.That(load.ErrorCode, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }
    }
}
