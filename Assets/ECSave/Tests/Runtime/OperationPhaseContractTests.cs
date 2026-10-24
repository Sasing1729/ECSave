using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.TestTools;

namespace ECSave.Tests.Runtime
{
    [TestFixture]
    public sealed class OperationPhaseContractTests
    {
        private World _world;
        private SaveDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("OperationPhaseContractWorld");

            _definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "OperationPhaseContractWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            _world = null;
        }

        private static List<OperationPhase> CaptureSerializePhases(
            SaveDefinition def, World world)
        {
            var phases = new List<OperationPhase>();
            var inner = new SerializeOperation(def, world, phases.Add);
            try
            {
                var ct = default(System.Threading.CancellationToken);
                int guard = 0;
                while (!inner.MoveNext(ct) && guard < 100) guard++;
            }
            finally
            {
                inner.Dispose();
            }
            return phases;
        }

        private static List<OperationPhase> CaptureDeserializePhases(
            SaveDefinition def, World world, byte[] data)
        {
            var phases = new List<OperationPhase>();
            var inner = new DeserializeOperation(
                def, world, data,
                RestorationScope.EntireWorld,
                DeletionPolicy.ByRegistration,
                phases.Add);
            try
            {
                var ct = default(System.Threading.CancellationToken);
                int guard = 0;
                while (!inner.MoveNext(ct) && guard < 100) guard++;
            }
            finally
            {
                inner.Dispose();
            }
            return phases;
        }

        [Test]
        public void SerializeOperation_EmitsExactlyS1S2S3S4()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var phases = CaptureSerializePhases(_definition, _world);

            CollectionAssert.AreEqual(
                new[]
                {
                    OperationPhase.S1_AwaitSnapshot,
                    OperationPhase.S2_Encode,
                    OperationPhase.S3_Seal,
                    OperationPhase.S4_Complete,
                },
                phases,
                "SerializeOperation must emit exactly S1 → S2 → S3 → S4 in order");
        }

        [Test]
        public void SerializeOperation_DoesNotEmitReservedPhases()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var phases = CaptureSerializePhases(_definition, _world);

            CollectionAssert.DoesNotContain(phases, OperationPhase.S0_Guard);
            CollectionAssert.DoesNotContain(phases, OperationPhase.D0_Guard);
            CollectionAssert.DoesNotContain(phases, OperationPhase.None);
        }

        [Test]
        public void SerializeOperation_CurrentPhase_MapsStages()
        {
            var op = new SerializeOperation(_definition, _world);
            try
            {
                Assert.AreEqual(OperationPhase.S1_AwaitSnapshot, op.CurrentPhase);

                var ct = default(System.Threading.CancellationToken);
                Assert.IsFalse(op.MoveNext(ct));
                Assert.AreEqual(OperationPhase.S2_Encode, op.CurrentPhase);

                Assert.IsFalse(op.MoveNext(ct));
                Assert.AreEqual(OperationPhase.S3_Seal, op.CurrentPhase);

                Assert.IsTrue(op.MoveNext(ct));
                Assert.AreEqual(OperationPhase.S4_Complete, op.CurrentPhase);
            }
            finally
            {
                op.Dispose();
            }
        }

        [Test]
        public void DeserializeOperation_EmitsExactlyD1D9P3P8()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var saveOp = new SerializeOperation(_definition, _world);
            var saveResult = saveOp.Execute(default);
            Assert.IsTrue(saveResult.IsSuccess);

            var phases = CaptureDeserializePhases(_definition, _world, saveResult.Data);

            CollectionAssert.AreEqual(
                new[]
                {
                    OperationPhase.D1_StructureValidation,
                    OperationPhase.D9_AwaitApply,
                    OperationPhase.P3_Generate,
                    OperationPhase.P8_Commit,
                },
                phases,
                "DeserializeOperation must emit exactly D1 → D9 → P3 → P8 in order");
        }

        [Test]
        public void DeserializeOperation_DoesNotEmitReservedPhases()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var saveOp = new SerializeOperation(_definition, _world);
            var saveResult = saveOp.Execute(default);

            var phases = CaptureDeserializePhases(_definition, _world, saveResult.Data);

            CollectionAssert.DoesNotContain(phases, OperationPhase.D2_Checksum);
            CollectionAssert.DoesNotContain(phases, OperationPhase.D3_Version);
            CollectionAssert.DoesNotContain(phases, OperationPhase.D4_WorldId);
            CollectionAssert.DoesNotContain(phases, OperationPhase.D5_SchemaMatch);
            CollectionAssert.DoesNotContain(phases, OperationPhase.D6_Migration);
            CollectionAssert.DoesNotContain(phases, OperationPhase.D7_EntityRefValidation);
            CollectionAssert.DoesNotContain(phases, OperationPhase.D8_RecordValidation);
            CollectionAssert.DoesNotContain(phases, OperationPhase.P1_Plan);
            CollectionAssert.DoesNotContain(phases, OperationPhase.P2_Backup);
            CollectionAssert.DoesNotContain(phases, OperationPhase.P4_StructuralChange);
            CollectionAssert.DoesNotContain(phases, OperationPhase.P5_ValueWrite);
            CollectionAssert.DoesNotContain(phases, OperationPhase.P6_ChunkComponent);
            CollectionAssert.DoesNotContain(phases, OperationPhase.P7_Destroy);
        }

        [Test]
        public void DeserializeOperation_CurrentPhase_MapsStages()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var saveOp = new SerializeOperation(_definition, _world);
            var saveResult = saveOp.Execute(default);

            var op = new DeserializeOperation(
                _definition, _world, saveResult.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);
            try
            {
                Assert.AreEqual(OperationPhase.D1_StructureValidation, op.CurrentPhase);

                var ct = default(System.Threading.CancellationToken);
                Assert.IsFalse(op.MoveNext(ct));
                Assert.AreEqual(OperationPhase.D9_AwaitApply, op.CurrentPhase);

                Assert.IsFalse(op.MoveNext(ct));
                Assert.AreEqual(OperationPhase.P3_Generate, op.CurrentPhase);

                Assert.IsTrue(op.MoveNext(ct));
                Assert.AreEqual(OperationPhase.P8_Commit, op.CurrentPhase);
            }
            finally
            {
                op.Dispose();
            }
        }

        [Test]
        public void SerializeOperation_FailureDoesNotEmitS4()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                System.Array.Empty<IdentityModule>(),
                new SelectionModule[] { new ThrowingSelectionModule() },
                gameDataVersion: 1,
                worldId: "OperationPhaseContractWorld");

            // SerializeOperation は捕捉した例外を Debug.LogError で記録する。
            // 本テストでは ThrowingSelectionModule により意図的に失敗させるため、
            // そのエラーログを「想定内」として事前登録する。
            // メッセージは Exception.ToString() を含むため、スタックトレース全体が
            // 単一の文字列として渡ってくる。RegexOptions.Singleline で複数行マッチさせる。
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new Regex(
                    @"\[ECSave\] SerializeOperation\.MoveNext threw in AsyncScheduled: " +
                    @"System\.InvalidOperationException: Injected selection failure",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant));

            var phases = new List<OperationPhase>();
            var op = new SerializeOperation(def, _world, phases.Add);
            try
            {
                var ct = default(System.Threading.CancellationToken);
                while (!op.MoveNext(ct)) { }

                CollectionAssert.DoesNotContain(phases, OperationPhase.S4_Complete,
                    "S4_Complete must not be emitted when the operation has failed");
            }
            finally
            {
                op.Dispose();
            }
        }

        [Test]
        public void DeserializeOperation_InvalidDataDoesNotEmitP8()
        {
            var phases = new List<OperationPhase>();
            var op = new DeserializeOperation(
                _definition, _world,
                new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                RestorationScope.EntireWorld,
                DeletionPolicy.None,
                phases.Add);
            try
            {
                var ct = default(System.Threading.CancellationToken);
                while (!op.MoveNext(ct)) { }

                CollectionAssert.DoesNotContain(phases, OperationPhase.P8_Commit,
                    "P8_Commit must not be emitted when deserialization fails");
            }
            finally
            {
                op.Dispose();
            }
        }

        [Test]
        public void SerializeOperation_PhaseOrder_IsStrictlyMonotonic()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var phases = CaptureSerializePhases(_definition, _world);

            int[] order =
            {
                (int)OperationPhase.S1_AwaitSnapshot,
                (int)OperationPhase.S2_Encode,
                (int)OperationPhase.S3_Seal,
                (int)OperationPhase.S4_Complete,
            };

            for (int i = 1; i < phases.Count; i++)
            {
                Assert.Greater((int)phases[i], (int)phases[i - 1],
                    $"Phase order must be strictly increasing: {phases[i - 1]} → {phases[i]}");
            }
        }

        [Test]
        public void DeserializeOperation_PhaseOrder_IsStrictlyMonotonic()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var saveOp = new SerializeOperation(_definition, _world);
            var saveResult = saveOp.Execute(default);

            var phases = CaptureDeserializePhases(_definition, _world, saveResult.Data);

            for (int i = 1; i < phases.Count; i++)
            {
                Assert.Greater((int)phases[i], (int)phases[i - 1],
                    $"Phase order must be strictly increasing: {phases[i - 1]} → {phases[i]}");
            }
        }

        /// <summary>
        /// 意図的に失敗を起こす SelectionModule。
        /// AddSelections 内で BuildQuery が呼ばれた時点で例外を投げるため、
        /// SerializeOperation は S2_Encode 段階で Failure となる。
        /// </summary>
        private sealed class ThrowingSelectionModule : SelectionModule
        {
            public override string FullTypeName
                => "OperationPhaseContractTests.ThrowingSelection";
            public override string FieldPath => "Injected";
            public override EntityQuery BuildQuery(EntityManager em)
                => throw new System.InvalidOperationException("Injected selection failure");
            public override void ExtractSelected(
                EntityQuery query, EntityManager em, NativeParallelHashSet<Entity> selected)
                => throw new System.InvalidOperationException("Injected selection failure");
        }
    }
}
