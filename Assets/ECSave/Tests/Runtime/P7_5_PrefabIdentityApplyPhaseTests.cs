using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using ECSave.Phase.Deserialize.Apply;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class P7_5_PrefabIdentityApplyPhaseTests
    {
        private World _world;

        private sealed class RecordingStep : PrefabMigrationStepBase
        {
            public int InvocationCount;
            public Entity LastEntity;
            public ulong LastTargetHash;

            private readonly ulong _old;
            private readonly ulong _new;

            public override ulong OldPrefabSchemaHash => _old;
            public override ulong NewPrefabSchemaHash => _new;

            public RecordingStep(ulong oldHash, ulong newHash)
            {
                _old = oldHash;
                _new = newHash;
            }

            public override void Migrate(ref PrefabMigrationContext context)
            {
                InvocationCount++;
                LastEntity = context.Entity;
                LastTargetHash = context.TargetSchemaHash;
            }
        }

        private sealed class ThrowingStep : PrefabMigrationStepBase
        {
            private readonly ulong _old;
            private readonly ulong _new;

            public override ulong OldPrefabSchemaHash => _old;
            public override ulong NewPrefabSchemaHash => _new;

            public ThrowingStep(ulong oldHash, ulong newHash)
            {
                _old = oldHash;
                _new = newHash;
            }

            public override void Migrate(ref PrefabMigrationContext context)
                => throw new InvalidOperationException("injected step failure");
        }

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("P75TestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static PrephaseContext MakeCtx(int persistentCount = 4)
        {
            var ctx = PrephaseContext.Create(
                new SaveDefinition(Array.Empty<SaveTypeModule>(), 1, "P75TestWorld"), null);
            ctx.PersistentToEntity = new NativeArray<Entity>(
                persistentCount, Allocator.TempJob);
            for (int i = 0; i < persistentCount; i++)
                ctx.PersistentToEntity[i] = Entity.Null;
            return ctx;
        }

        // === #13: PrefabMigrationsToApply == null ===
        [Test]
        public void NullList_ReturnsNone()
        {
            var ctx = MakeCtx();
            try
            {
                Assert.That(ctx.PrefabMigrationsToApply, Is.Null);
                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, _world.EntityManager);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { ctx.Dispose(); }
        }

        // === #13: 空リスト ===
        [Test]
        public void EmptyList_ReturnsNone()
        {
            var ctx = MakeCtx();
            try
            {
                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>();
                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, _world.EntityManager);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { ctx.Dispose(); }
        }

        // === #14: PersistentId 範囲外 ===
        [Test]
        public void PersistentIdOutOfRange_Skipped()
        {
            var ctx = MakeCtx(4);
            try
            {
                var step = new RecordingStep(0x1, 0x2);
                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                {
                    new PrefabMigrationEntry
                    {
                        PersistentId = 999,
                        SourcePrefabGuid = 0xAAAA,
                        SourceSchemaHash = 0x1,
                        Steps = new[] { step },
                    },
                };

                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, _world.EntityManager);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(step.InvocationCount, Is.EqualTo(0));
            }
            finally { ctx.Dispose(); }
        }

        // === #15: PersistentToEntity[pid] == Null ===
        [Test]
        public void TargetEntityNull_Skipped()
        {
            var ctx = MakeCtx(4);
            try
            {
                // pid=0 は Entity.Null のまま
                var step = new RecordingStep(0x1, 0x2);
                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                {
                    new PrefabMigrationEntry
                    {
                        PersistentId = 0,
                        SourcePrefabGuid = 0xAAAA,
                        SourceSchemaHash = 0x1,
                        Steps = new[] { step },
                    },
                };

                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, _world.EntityManager);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(step.InvocationCount, Is.EqualTo(0));
            }
            finally { ctx.Dispose(); }
        }

        // === #16: !em.Exists(target) ===
        [Test]
        public void TargetEntityDestroyed_Skipped()
        {
            var ctx = MakeCtx(4);
            try
            {
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.DestroyEntity(e);
                ctx.PersistentToEntity[0] = e;

                var step = new RecordingStep(0x1, 0x2);
                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                {
                    new PrefabMigrationEntry
                    {
                        PersistentId = 0,
                        SourcePrefabGuid = 0xAAAA,
                        SourceSchemaHash = 0x1,
                        Steps = new[] { step },
                    },
                };

                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, em);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(step.InvocationCount, Is.EqualTo(0));
            }
            finally { ctx.Dispose(); }
        }

        // === #17: Steps == null ===
        [Test]
        public void NullSteps_Skipped()
        {
            var ctx = MakeCtx(4);
            try
            {
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                ctx.PersistentToEntity[0] = e;

                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                {
                    new PrefabMigrationEntry
                    {
                        PersistentId = 0,
                        SourcePrefabGuid = 0xAAAA,
                        SourceSchemaHash = 0x1,
                        Steps = null,
                    },
                };

                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, em);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { ctx.Dispose(); }
        }

        // === #18: Steps 内の null 要素 ===
        [Test]
        public void NullStepElement_Skipped()
        {
            var ctx = MakeCtx(4);
            try
            {
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                ctx.PersistentToEntity[0] = e;

                var step = new RecordingStep(0x1, 0x2);
                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                {
                    new PrefabMigrationEntry
                    {
                        PersistentId = 0,
                        SourcePrefabGuid = 0xAAAA,
                        SourceSchemaHash = 0x1,
                        Steps = new PrefabMigrationStepBase[] { null, step, null },
                    },
                };

                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, em);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(step.InvocationCount, Is.EqualTo(1));
            }
            finally { ctx.Dispose(); }
        }

        // === #19: Migrate 例外 → WorldModificationFailed ===
        [Test]
        public void MigrateThrows_ReturnsWorldModificationFailed()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var ctx = MakeCtx(4);
                try
                {
                    var em = _world.EntityManager;
                    var e = em.CreateEntity();
                    ctx.PersistentToEntity[0] = e;

                    ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                    {
                        new PrefabMigrationEntry
                        {
                            PersistentId = 0,
                            SourcePrefabGuid = 0xAAAA,
                            SourceSchemaHash = 0x1,
                            Steps = new PrefabMigrationStepBase[] { new ThrowingStep(0x1, 0x2) },
                        },
                    };

                    var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, em);
                    Assert.That(err, Is.EqualTo(SaveLoadErrorCode.WorldModificationFailed));
                }
                finally { ctx.Dispose(); }
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        // === #20: 複数ステップ連鎖 ===
        [Test]
        public void MultiStep_AllInvokedInOrder()
        {
            var ctx = MakeCtx(4);
            try
            {
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                ctx.PersistentToEntity[0] = e;

                var s1 = new RecordingStep(0x1, 0x2);
                var s2 = new RecordingStep(0x2, 0x3);
                var s3 = new RecordingStep(0x3, 0x4);
                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                {
                    new PrefabMigrationEntry
                    {
                        PersistentId = 0,
                        SourcePrefabGuid = 0xAAAA,
                        SourceSchemaHash = 0x1,
                        Steps = new[] { s1, s2, s3 },
                    },
                };

                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, em);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(s1.InvocationCount, Is.EqualTo(1));
                Assert.That(s2.InvocationCount, Is.EqualTo(1));
                Assert.That(s3.InvocationCount, Is.EqualTo(1));
                Assert.That(s1.LastTargetHash, Is.EqualTo(0x2UL));
                Assert.That(s2.LastTargetHash, Is.EqualTo(0x3UL));
                Assert.That(s3.LastTargetHash, Is.EqualTo(0x4UL));
                Assert.That(s1.LastEntity, Is.EqualTo(e));
            }
            finally { ctx.Dispose(); }
        }

        // === #21: 複数 Entry ===
        [Test]
        public void MultipleEntries_AllApplied()
        {
            var ctx = MakeCtx(4);
            try
            {
                var em = _world.EntityManager;
                var e0 = em.CreateEntity();
                var e1 = em.CreateEntity();
                ctx.PersistentToEntity[0] = e0;
                ctx.PersistentToEntity[1] = e1;

                var step0 = new RecordingStep(0x1, 0x2);
                var step1 = new RecordingStep(0x3, 0x4);
                ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                {
                    new PrefabMigrationEntry
                    {
                        PersistentId = 0,
                        SourcePrefabGuid = 0xAAAA,
                        SourceSchemaHash = 0x1,
                        Steps = new[] { step0 },
                    },
                    new PrefabMigrationEntry
                    {
                        PersistentId = 1,
                        SourcePrefabGuid = 0xBBBB,
                        SourceSchemaHash = 0x3,
                        Steps = new[] { step1 },
                    },
                };

                var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, em);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(step0.InvocationCount, Is.EqualTo(1));
                Assert.That(step1.InvocationCount, Is.EqualTo(1));
                Assert.That(step0.LastEntity, Is.EqualTo(e0));
                Assert.That(step1.LastEntity, Is.EqualTo(e1));
            }
            finally { ctx.Dispose(); }
        }

        // === 2件目で例外 → 3件目は実行されない ===
        [Test]
        public void SecondEntryThrows_ThirdNotApplied()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var ctx = MakeCtx(4);
                try
                {
                    var em = _world.EntityManager;
                    var e0 = em.CreateEntity();
                    var e1 = em.CreateEntity();
                    var e2 = em.CreateEntity();
                    ctx.PersistentToEntity[0] = e0;
                    ctx.PersistentToEntity[1] = e1;
                    ctx.PersistentToEntity[2] = e2;

                    var step0 = new RecordingStep(0x1, 0x2);
                    var step2 = new RecordingStep(0x5, 0x6);
                    ctx.PrefabMigrationsToApply = new List<PrefabMigrationEntry>
                    {
                        new PrefabMigrationEntry
                        {
                            PersistentId = 0,
                            Steps = new[] { step0 },
                        },
                        new PrefabMigrationEntry
                        {
                            PersistentId = 1,
                            Steps = new PrefabMigrationStepBase[] { new ThrowingStep(0x3, 0x4) },
                        },
                        new PrefabMigrationEntry
                        {
                            PersistentId = 2,
                            Steps = new[] { step2 },
                        },
                    };

                    var err = P7_5_PrefabIdentityApplyPhase.Execute(ref ctx, em);
                    Assert.That(err, Is.EqualTo(SaveLoadErrorCode.WorldModificationFailed));
                    Assert.That(step0.InvocationCount, Is.EqualTo(1), "First entry applied before failure");
                    Assert.That(step2.InvocationCount, Is.EqualTo(0), "Third entry not applied after failure");
                }
                finally { ctx.Dispose(); }
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }
    }
}
