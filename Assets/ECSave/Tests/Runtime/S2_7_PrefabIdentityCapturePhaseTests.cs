using System;
using System.Collections.Generic;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Serialize;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class S2_7_PrefabIdentityCapturePhaseTests
    {
        private static SubSceneDefinition MakeDef(
            Func<Entity, PrefabIdentityInfo?> provider)
        {
            return new SubSceneDefinition(
                false, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                provider,
                Array.Empty<SubSceneDestroyedIdentityHandler>(),
                Array.Empty<PrefabMigrationStepBase>());
        }

        private static SubSceneDefinition DisabledDef()
        {
            return new SubSceneDefinition(
                false, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                null,
                Array.Empty<SubSceneDestroyedIdentityHandler>(),
                Array.Empty<PrefabMigrationStepBase>());
        }

        private static Entity MakeEntity(int index)
        {
            var e = default(Entity);
            unsafe
            {
                int* p = (int*)&e;
                p[0] = index;
                p[1] = 1;
            }
            return e;
        }

        private static (NativeList<Entity>, NativeParallelHashMap<Entity, int>)
            MakeEntities(params int[] indices)
        {
            var list = new NativeList<Entity>(indices.Length, Allocator.TempJob);
            var map = new NativeParallelHashMap<Entity, int>(indices.Length, Allocator.TempJob);
            for (int i = 0; i < indices.Length; i++)
            {
                var e = MakeEntity(indices[i]);
                list.Add(e);
                map.TryAdd(e, i);
            }
            return (list, map);
        }

        [Test]
        public void NullDef_NoSection()
        {
            var (entities, idOf) = MakeEntities(1, 2);
            try
            {
                var r = S2_7_PrefabIdentityCapturePhase.Execute(null, entities, idOf);
                Assert.That(r.HasSection, Is.False);
                Assert.That(r.Records.Count, Is.EqualTo(0));
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void DisabledProvider_NoSection()
        {
            var (entities, idOf) = MakeEntities(1, 2);
            try
            {
                var def = DisabledDef();
                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, entities, idOf);
                Assert.That(r.HasSection, Is.False);
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void EmptyEntities_NoSection()
        {
            var entities = new NativeList<Entity>(16, Allocator.TempJob);
            var idOf = new NativeParallelHashMap<Entity, int>(16, Allocator.TempJob);
            try
            {
                var def = MakeDef(_ => new PrefabIdentityInfo(0xAAAA, 0xBBBB));
                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, entities, idOf);
                Assert.That(r.HasSection, Is.False);
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void ProviderReturnsNull_NoSection()
        {
            var (entities, idOf) = MakeEntities(1, 2);
            try
            {
                var def = MakeDef(_ => null);
                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, entities, idOf);
                Assert.That(r.HasSection, Is.False);
                Assert.That(r.Records.Count, Is.EqualTo(0));
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void ProviderReturnsGuidZero_RecordSkipped()
        {
            var (entities, idOf) = MakeEntities(1, 2);
            try
            {
                var def = MakeDef(_ => new PrefabIdentityInfo(0UL, 0xBBBB));
                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, entities, idOf);
                Assert.That(r.HasSection, Is.False);
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void ProviderReturnsValidInfo_RecordsCaptured()
        {
            var (entities, idOf) = MakeEntities(1, 2, 3);
            try
            {
                int callCount = 0;
                var def = MakeDef(e =>
                {
                    callCount++;
                    return new PrefabIdentityInfo(
                        (ulong)(0x1000 + callCount),
                        (ulong)(0x2000 + callCount));
                });

                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, entities, idOf);

                Assert.That(r.HasSection, Is.True);
                Assert.That(r.Records.Count, Is.EqualTo(3));
                Assert.That(r.SectionBodySize,
                    Is.EqualTo(PrefabIdentityLayout.ComputeBodySize(3)));
                Assert.That(callCount, Is.EqualTo(3),
                    "Provider should be called once per entity");
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void ProviderMixed_OnlyValidRecorded()
        {
            var (entities, idOf) = MakeEntities(1, 2, 3, 4);
            try
            {
                int idx = 0;
                var def = MakeDef(_ =>
                {
                    idx++;
                    return idx switch
                    {
                        1 => new PrefabIdentityInfo(0xAAAA, 0xBBBB),
                        2 => null,
                        3 => new PrefabIdentityInfo(0UL, 0UL),
                        _ => new PrefabIdentityInfo(0xCCCC, 0xDDDD),
                    };
                });

                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, entities, idOf);

                Assert.That(r.Records.Count, Is.EqualTo(2),
                    "Only entities with valid info (1 and 4) should be recorded");
                Assert.That(r.Records[0].PrefabGuid, Is.EqualTo(0xAAAAUL));
                Assert.That(r.Records[1].PrefabGuid, Is.EqualTo(0xCCCCUL));
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void ProviderThrows_RecordSkipped()
        {
            var (entities, idOf) = MakeEntities(1, 2);
            try
            {
                int callCount = 0;
                var def = MakeDef(_ =>
                {
                    callCount++;
                    if (callCount == 1) throw new InvalidOperationException("boom");
                    return new PrefabIdentityInfo(0xAAAA, 0xBBBB);
                });

                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, entities, idOf);
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

                Assert.That(r.Records.Count, Is.EqualTo(1),
                    "Only the entity whose provider threw should be skipped");
            }
            finally { entities.Dispose(); idOf.Dispose(); }
        }

        [Test]
        public void EntityNotInIdOf_Skipped()
        {
            var list = new NativeList<Entity>(16, Allocator.TempJob);
            var map = new NativeParallelHashMap<Entity, int>(16, Allocator.TempJob);
            try
            {
                var e1 = MakeEntity(1);
                var e2 = MakeEntity(2);
                list.Add(e1);
                list.Add(e2);
                map.TryAdd(e1, 0);
                // e2 not added to map

                var def = MakeDef(_ => new PrefabIdentityInfo(0xAAAA, 0xBBBB));
                var r = S2_7_PrefabIdentityCapturePhase.Execute(def, list, map);

                Assert.That(r.Records.Count, Is.EqualTo(1));
            }
            finally { list.Dispose(); map.Dispose(); }
        }
    }
}
