using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public struct GenChunkValue : IComponentData
    {
        public int Value;
    }

    public struct GenFragmentMarker : IComponentData
    {
    }

    public sealed class GenChunkMergedScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterChunkComponent<GenChunkValue>();
            b.WorldId("GenChunkMergedWorld");
        }
    }

    public class ChunkComponentMergedTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenChunkMergedWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static GenChunkMergedScope CreateScope()
            => ScriptableObject.CreateInstance<GenChunkMergedScope>();

        private static EntityQuery ChunkQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<GenChunkValue>() },
            });

        private static bool HasMergedWarning(LoadResult result)
        {
            if (result.Warnings == null) return false;
            for (int i = 0; i < result.Warnings.Count; i++)
                if (result.Warnings[i].Kind == LoadWarningKind.ChunkComponentMerged)
                    return true;
            return false;
        }

        private static void SetupTwoGroups(EntityManager em, out Entity eA, out Entity eB)
        {
            // 異なる Archetype で 2 Chunk に分ける
            eA = em.CreateEntity(typeof(GenHealth));
            em.SetComponentData(eA, new GenHealth { Current = 1, Max = 100 });

            eB = em.CreateEntity(typeof(GenHealth), typeof(GenFragmentMarker));
            em.SetComponentData(eB, new GenHealth { Current = 2, Max = 100 });

            em.AddChunkComponentData<GenChunkValue>(eA);
            em.AddChunkComponentData<GenChunkValue>(eB);

            var q = ChunkQuery(em);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(2, chunks.Length, "setup should produce two chunks");

            em.SetChunkComponentData(chunks[0], new GenChunkValue { Value = 100 });
            em.SetChunkComponentData(chunks[1], new GenChunkValue { Value = 200 });
        }

        private static void SetupSingleGroup(EntityManager em, out Entity e)
        {
            e = em.CreateEntity(typeof(GenHealth));
            em.SetComponentData(e, new GenHealth { Current = 1, Max = 100 });
            em.AddChunkComponentData<GenChunkValue>(e);

            var q = ChunkQuery(em);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(1, chunks.Length);
            em.SetChunkComponentData(chunks[0], new GenChunkValue { Value = 100 });
        }

        [UnityTest]
        public IEnumerator MultipleGroups_EmitsWarning()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                SetupTwoGroups(em, out _, out _);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("GenChunkMergedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");
                Assert.IsTrue(HasMergedWarning(load),
                    "Two groups merging into one chunk should emit ChunkComponentMerged warning");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SingleGroup_NoWarning()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                SetupSingleGroup(em, out _);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkMergedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                Assert.IsFalse(HasMergedWarning(load),
                    "Single group should not emit ChunkComponentMerged warning");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Warning_HasCorrectFields()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                SetupTwoGroups(em, out _, out _);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenChunkMergedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                LoadWarning? merged = null;
                for (int i = 0; i < load.Warnings.Count; i++)
                {
                    if (load.Warnings[i].Kind == LoadWarningKind.ChunkComponentMerged)
                    {
                        merged = load.Warnings[i];
                        break;
                    }
                }

                Assert.IsTrue(merged.HasValue, "Expected warning not found");
                var w = merged.Value;
                Assert.AreEqual(Entity.Null, w.SourceEntity);
                Assert.AreEqual(-1, w.SaveEntityIndex);
                Assert.IsNotNull(w.TypeName);
                Assert.IsTrue(w.TypeName.Contains("GenChunkValue"),
                    $"TypeName should reference GenChunkValue, got: {w.TypeName}");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
