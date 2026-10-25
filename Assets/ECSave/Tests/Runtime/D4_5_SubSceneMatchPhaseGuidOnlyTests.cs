using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// <see cref="ContentVerificationMode.GuidOnly"/> の動作検証。
    ///
    /// <para>
    /// 保存 → 別 World でロード の往復で、GUID / State の不一致が
    /// <see cref="LoadWarningKind.SceneLoadStateMismatch"/> として報告されることを確認する。
    /// <see cref="ContentVerificationMode.GuidOnly"/> では ContentHash は検証されない。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class D4_5_SubSceneMatchPhaseGuidOnlyTests
    {
        private const string WorldName = "GuidOnlyVerifyWorld";
        private static readonly SceneGuid SceneA = SceneGuid.FromString("guid-only-scene-a");
        private static readonly SceneGuid SceneB = SceneGuid.FromString("guid-only-scene-b");

        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World(WorldName);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        // ─────────────────────────────────────────
        // Save/Load ヘルパー
        // ─────────────────────────────────────────

        private static SaveSystem CreateSystem(
            ContentVerificationMode mode,
            Func<IReadOnlyList<SceneLoadInfo>> provider)
        {
            var subScene = new SubSceneDefinition(
                registerSceneLoadState: true,
                registerDestroyedEntities: false,
                hasSceneRestoreCallback: false,
                withSceneStateRollback: false,
                contentVerification: mode,
                streamingConsistency: StreamingConsistencyMode.TimeBased,
                tombstoneCapacity: 0,
                sceneStateProvider: provider,
                sceneRestoreCallback: null,
                prefabMigrations: null);

            var def = new SaveDefinition(
                Array.Empty<SaveTypeModule>(),
                gameDataVersion: 1,
                worldId: WorldName);

            def.SubScene = subScene;

            return new SaveSystem(def);
        }

        private byte[] Save(
            ContentVerificationMode mode,
            params SceneLoadInfo[] scenes)
        {
            var sys = CreateSystem(mode, () => scenes);
            var result = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess, $"Serialize failed: {result.ErrorCode}");
            return result.Data;
        }

        private LoadResult Load(
            byte[] data,
            ContentVerificationMode mode,
            params SceneLoadInfo[] currentScenes)
        {
            RestartWorld();
            var sys = CreateSystem(mode, () => currentScenes);
            return sys.Deserialize(
                _world, data,
                RestorationScope.EntireWorld,
                DeletionPolicy.ByRegistration).GetAwaiter().GetResult();
        }

        private static bool HasWarning(LoadResult load, LoadWarningKind kind)
            => load.Warnings != null && load.Warnings.Any(w => w.Kind == kind);

        // ─────────────────────────────────────────
        // GuidOnly: 一致ケース（警告なし）
        // ─────────────────────────────────────────

        [Test]
        public void GuidOnly_MatchingGuidAndState_NoWarning()
        {
            var data = Save(
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded));

            var load = Load(
                data,
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded));

            Assert.IsTrue(load.IsSuccess, $"Load failed: {load.ErrorCode}");
            Assert.IsFalse(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch),
                "Same GUID+State must not warn; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
        }

        // ─────────────────────────────────────────
        // GuidOnly: State 不一致
        // ─────────────────────────────────────────

        [Test]
        public void GuidOnly_StateMismatch_EmitsSceneLoadStateMismatch()
        {
            var data = Save(
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded));

            var load = Load(
                data,
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Unloaded));

            Assert.IsTrue(load.IsSuccess);
            Assert.IsTrue(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch),
                "State mismatch must emit SceneLoadStateMismatch; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
        }

        // ─────────────────────────────────────────
        // GuidOnly: 保存時に存在した GUID が現在側に無い
        // ─────────────────────────────────────────

        [Test]
        public void GuidOnly_MissingGuid_EmitsSceneLoadStateMismatch()
        {
            var data = Save(
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded));

            // 現在側は SceneB のみ（SceneA が無い）
            var load = Load(
                data,
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneB, SceneLoadState.Loaded));

            Assert.IsTrue(load.IsSuccess);
            Assert.IsTrue(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch),
                "Missing GUID must emit SceneLoadStateMismatch; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
        }

        // ─────────────────────────────────────────
        // GuidOnly: Loading は比較対象外
        // ─────────────────────────────────────────

        [Test]
        public void GuidOnly_CurrentLoading_NoWarning()
        {
            var data = Save(
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded));

            var load = Load(
                data,
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loading));

            Assert.IsTrue(load.IsSuccess);
            Assert.IsFalse(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch),
                "Loading on current side must skip comparison; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
        }

        [Test]
        public void GuidOnly_SavedLoading_NoWarning()
        {
            var data = Save(
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loading));

            var load = Load(
                data,
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded));

            Assert.IsTrue(load.IsSuccess);
            Assert.IsFalse(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch),
                "Loading on saved side must skip comparison; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
        }

        // ─────────────────────────────────────────
        // GuidOnly: ContentHash は検証しない
        // ─────────────────────────────────────────

        [Test]
        public void GuidOnly_ContentHashDiffers_NoWarning()
        {
            // 保存時 ContentHash = 0xAAAA、現在側 = 0xBBBB（異なる）
            // GuidOnly なので ContentHash は検証されない
            var data = Save(
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAAUL));

            var load = Load(
                data,
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded, SceneRestoreMode.Required, 0xBBBBUL));

            Assert.IsTrue(load.IsSuccess);
            Assert.IsFalse(HasWarning(load, LoadWarningKind.SceneContentHashMismatch),
                "GuidOnly must not check ContentHash; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
            Assert.IsFalse(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch));
        }

        // ─────────────────────────────────────────
        // GuidOnly: Multiple scenes（部分的に一致）
        // ─────────────────────────────────────────

        [Test]
        public void GuidOnly_MultipleScenes_PartialMatch_WarnsOnlyForMismatch()
        {
            // 保存: A=Loaded, B=Loaded
            // 現在: A=Loaded, B=Unloaded → B のみ警告
            var data = Save(
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded),
                new SceneLoadInfo(SceneB, SceneLoadState.Loaded));

            var load = Load(
                data,
                ContentVerificationMode.GuidOnly,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded),
                new SceneLoadInfo(SceneB, SceneLoadState.Unloaded));

            Assert.IsTrue(load.IsSuccess);

            var stateMismatches = load.Warnings
                .Where(w => w.Kind == LoadWarningKind.SceneLoadStateMismatch)
                .ToList();
            Assert.AreEqual(1, stateMismatches.Count,
                "Only the mismatched scene (B) should warn; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
            Assert.AreEqual(1, stateMismatches[0].SaveEntityIndex,
                "Warning should point to the saved index of SceneB (1)");
        }

        // ─────────────────────────────────────────
        // GuidAndContentHash: 両方の検証が走る
        // ─────────────────────────────────────────

        [Test]
        public void GuidAndContentHash_StateAndHashMismatch_BothWarnings()
        {
            var data = Save(
                ContentVerificationMode.GuidAndContentHash,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAAUL));

            var load = Load(
                data,
                ContentVerificationMode.GuidAndContentHash,
                new SceneLoadInfo(SceneA, SceneLoadState.Unloaded, SceneRestoreMode.Required, 0xBBBBUL));

            Assert.IsTrue(load.IsSuccess);
            Assert.IsTrue(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch),
                "State mismatch must warn");
            Assert.IsTrue(HasWarning(load, LoadWarningKind.SceneContentHashMismatch),
                "ContentHash mismatch must warn; warnings: " +
                string.Join(", ", load.Warnings.Select(w => w.Kind)));
        }

        // ─────────────────────────────────────────
        // None: 何も検証しない
        // ─────────────────────────────────────────

        [Test]
        public void None_AnyMismatch_NoWarning()
        {
            var data = Save(
                ContentVerificationMode.None,
                new SceneLoadInfo(SceneA, SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAAUL));

            var load = Load(
                data,
                ContentVerificationMode.None,
                new SceneLoadInfo(SceneA, SceneLoadState.Unloaded, SceneRestoreMode.Required, 0xBBBBUL));

            Assert.IsTrue(load.IsSuccess);
            Assert.IsFalse(HasWarning(load, LoadWarningKind.SceneLoadStateMismatch));
            Assert.IsFalse(HasWarning(load, LoadWarningKind.SceneContentHashMismatch));
        }
    }
}
