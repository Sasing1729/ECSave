using System;
using System.Collections.Generic;
using System.Threading;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Phase.Deserialize.Apply;
using ECSave.Phase.Deserialize.Prephase;
using Unity.Collections;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
    internal sealed class DeserializePhaseException : Exception
    {
        public DeserializePhaseException(string message) : base(message) { }
    }

    /// <summary>
    /// Deserialize のステートマシンと各 Stage のオーケストレーション。
    ///
    /// <para>
    /// <b>責務</b>: Stage 遷移、各 Phase の呼び出し順序、ロールバックの制御のみ。
    /// 各 Phase の実装（D0〜D8.5、P1〜P8）は個別ファイルに分離されている。
    /// </para>
    /// <para>
    /// <b>Stage 構成</b>:
    /// <list type="bullet">
    ///   <item>Start → Validated: <see cref="DoPrephase"/>（D0〜D8.5、検証）</item>
    ///   <item>Validated → Planned: <see cref="DoPlanAndBackup"/>（P1〜P2、計画とバックアップ）</item>
    ///   <item>Planned → Done: <see cref="DoApply"/>（P3〜P8、適用と確定）</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed unsafe class DeserializeOperation : IDisposable
    {
        private enum Stage : byte
        {
            Start = 0,
            Validated = 1,
            Planned = 2,
            Done = 3,
        }

        private readonly SaveDefinition _def;
        private readonly World _world;
        private readonly byte[] _data;
        private readonly RestorationScope _scope;
        private readonly DeletionPolicy _policy;
        private readonly Action<OperationPhase> _onPhaseChanged;

        private Stage _stage;
        private LoadResult _result;
        private bool _disposed;
        private OperationPhase _lastNotifiedPhase = OperationPhase.None;

        private PrephaseContext _ctx;
        private bool _ctxInitialized;

        private NativeList<Entity> _destroyTargets;
        private bool _destroyTargetsCreated;

        private ApplyJournal _journal;
        private NativeArray<Entity> _updateArray;
        private bool _updateArrayCreated;
        private NativeList<Entity> _updateTargets;
        private bool _updateTargetsCreated;

        public bool IsComplete => _stage == Stage.Done;
        public LoadResult Result => _result;

        public OperationPhase CurrentPhase => _stage switch
        {
            Stage.Start => OperationPhase.D1_StructureValidation,
            Stage.Validated => OperationPhase.D9_AwaitApply,
            Stage.Planned => OperationPhase.P3_Generate,
            Stage.Done => OperationPhase.P8_Commit,
            _ => OperationPhase.None,
        };

        public DeserializeOperation(
            SaveDefinition def,
            World world,
            byte[] data,
            RestorationScope scope,
            DeletionPolicy policy,
            Action<OperationPhase> onPhaseChanged = null)
        {
            _def = def;
            _world = world;
            _data = data;
            _scope = scope;
            _policy = policy;
            _onPhaseChanged = onPhaseChanged;
        }

        public LoadResult Execute(CancellationToken ct)
        {
            try
            {
                while (!MoveNext(ct)) { }
                return _result;
            }
            finally
            {
                Dispose();
            }
        }

        public bool MoveNext(CancellationToken ct)
        {
            if (_stage == Stage.Done) return true;

            try
            {
                switch (_stage)
                {
                    case Stage.Start:
                        NotifyPhase(OperationPhase.D1_StructureValidation);
                        if (!DoPrephase(ct)) { _stage = Stage.Done; return true; }
                        _stage = Stage.Validated;
                        return false;

                    case Stage.Validated:
                        NotifyPhase(OperationPhase.D9_AwaitApply);
                        if (!DoPlanAndBackup(ct)) { _stage = Stage.Done; return true; }
                        _stage = Stage.Planned;
                        return false;

                    case Stage.Planned:
                        NotifyPhase(OperationPhase.P3_Generate);
                        DoApply(ct);
                        _stage = Stage.Done;
                        NotifyPhase(OperationPhase.P8_Commit);
                        return true;
                }
            }
            catch (FormatException fe)
            {
                _result = LoadResult.Failure(ErrorInfoMapper.Map(fe.Error));
                _stage = Stage.Done;
                return true;
            }
            catch (OperationCanceledException)
            {
                _result = LoadResult.Failure(SaveLoadErrorCode.Cancelled);
                _stage = Stage.Done;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[ECSave] DeserializeOperation.MoveNext threw in {_stage}: {ex}");
                _result = LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed);
                _stage = Stage.Done;
                return true;
            }

            return true;
        }

        private void NotifyPhase(OperationPhase phase)
        {
            if (phase == _lastNotifiedPhase) return;
            _lastNotifiedPhase = phase;
            _onPhaseChanged?.Invoke(phase);
        }

        // ── Stage: Start → Validated ─────────────────

        private bool DoPrephase(CancellationToken ct)
        {
            _ctx = PrephaseContext.Create(_def, _world);
            _ctxInitialized = true;

            DeserializeDiag.BeginPhase();
            var err = D0_InputCopyPhase.Execute(ref _ctx, _data, _def, _world, ct);
            DeserializeDiag.EndPhase("D0_InputCopy");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D1_StructureValidationPhase.Execute(ref _ctx);
            DeserializeDiag.EndPhase("D1_Structure");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D3_VersionCheckPhase.Execute(ref _ctx);
            DeserializeDiag.EndPhase("D3_Version");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D4_WorldIdCheckPhase.Execute(ref _ctx);
            DeserializeDiag.EndPhase("D4_WorldId");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D5_SchemaMatchPhase.Execute(ref _ctx);
            DeserializeDiag.EndPhase("D5_SchemaMatch");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D6_MigrationPhase.Execute(ref _ctx);
            DeserializeDiag.EndPhase("D6_Migration");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D5_5_PrefabMigrationPhase.Execute(ref _ctx, out var prefabIdentities);
            DeserializeDiag.EndPhase("D5.5_PrefabMigration");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.PrefabIdentities = prefabIdentities;

            DeserializeDiag.BeginPhase();
            err = D4_5_SubSceneMatchPhase.Execute(
                _def.SubScene,
                _ctx.SectionTable,
                _ctx.View,
                _ctx.Warnings,
                out var subSceneState,
                out _);
            DeserializeDiag.EndPhase("D4.5_SubSceneMatch");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.SubSceneState = subSceneState;

            DeserializeDiag.BeginPhase();
            err = D7_EntityRefValidationPhase.Execute(ref _ctx);
            DeserializeDiag.EndPhase("D7_EntityRefValidation");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D8_RecordValidationPhase.Execute(ref _ctx);
            DeserializeDiag.EndPhase("D8_RecordValidation");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = D8_5_TombstoneValidationPhase.Execute(ref _ctx, out var tombstoneEntries);
            DeserializeDiag.EndPhase("D8.5_TombstoneValidation");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.TombstoneSections = tombstoneEntries;

            return true;
        }

        // ── Stage: Validated → Planned ───────────────

        private bool DoPlanAndBackup(CancellationToken ct)
        {
            var em = _world.EntityManager;

            DeserializeDiag.BeginPhase();
            var err = P1_PlanPhase.Execute(ref _ctx, _scope, _policy);
            DeserializeDiag.EndPhase("P1_Plan");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            DeserializeDiag.BeginPhase();
            err = P1_5_SceneRestorePlanPhase.Execute(
                _def.SubScene,
                _ctx.SubSceneState,
                out var restoreRequests);
            DeserializeDiag.EndPhase("P1.5_SceneRestorePlan");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.SubSceneRestoreRequests = restoreRequests;

            _destroyTargets = new NativeList<Entity>(16, Allocator.Persistent);
            _destroyTargetsCreated = true;

            DeserializeDiag.BeginPhase();
            err = P1_DeletionPlanPhase.Execute(ref _ctx, _scope, _policy, _destroyTargets);
            DeserializeDiag.EndPhase("P1_DeletionPlan");
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            if (_policy == DeletionPolicy.ByScope
                && _scope.IsEntireWorld
                && _destroyTargets.Length > 0)
            {
                _ctx.Warnings.Add(WarningRecord.Create(
                    LoadWarningKind.ByScopeDeletionWarning,
                    typeSectionId: 0,
                    saveEntityIndex: -1,
                    sourceEntity: Entity.Null,
                    extraHash: (ulong)_destroyTargets.Length));
            }

            _updateTargets = new NativeList<Entity>(16, Allocator.Persistent);
            _updateTargetsCreated = true;
            for (int i = 0; i < _ctx.PersistentToEntity.Length; i++)
            {
                var e = _ctx.PersistentToEntity[i];
                if (e != Entity.Null) _updateTargets.Add(e);
            }

            _updateArray = new NativeArray<Entity>(_updateTargets.Length, Allocator.Persistent);
            _updateArrayCreated = true;
            for (int i = 0; i < _updateTargets.Length; i++)
                _updateArray[i] = _updateTargets[i];

            _journal = new ApplyJournal(Allocator.Persistent);

            DeserializeDiag.BeginPhase();
            _journal.Backup(_ctx.Definition, _updateArray, em);
            DeserializeDiag.EndPhase("P2_Backup");

            return true;
        }

        // ── Stage: Planned → Done ────────────────────

        private void DoApply(CancellationToken ct)
        {
            var em = _world.EntityManager;

            DeserializeDiag.BeginPhase();
            var staleErr = DeserializeApplyHelper.ValidateUpdateTargets(em, _updateTargets);
            DeserializeDiag.EndPhase("P0_Validate");
            if (staleErr != SaveLoadErrorCode.None)
            {
                _result = LoadResult.Failure(staleErr);
                return;
            }

            bool success = false;
            IReadOnlyList<SceneLoadInfo> sceneSnapshot = null;

            try
            {
                try
                {
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);

                    if (FaultInjector.ShouldFail("P3"))
                        throw new DeserializePhaseException("Injected P3");

                    DeserializeDiag.BeginPhase();
                    P3_EntityGenerationPhase.Execute(ref _ctx, _journal, em);
                    DeserializeDiag.EndPhase("P3_Generate");

                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);

                    if (FaultInjector.ShouldFail("P4"))
                        throw new DeserializePhaseException("Injected P4");

                    DeserializeDiag.BeginPhase();
                    var p72 = P7_2_FixedStringScanPhase.Execute(ref _ctx);
                    DeserializeDiag.EndPhase("P7.2_FixedStringScan");
                    if (p72 != SaveLoadErrorCode.None)
                        throw new DeserializePhaseException("P7.2 failed: " + p72);

                    DeserializeDiag.BeginPhase();
                    P4_ModuleApplyPhase.ExecuteNonChunk(ref _ctx, em);
                    DeserializeDiag.EndPhase("P4_ApplyModules");

                    DeserializeDiag.BeginPhase();
                    P4_ModuleApplyPhase.ExecuteIdentity(ref _ctx, em);
                    DeserializeDiag.EndPhase("P4_ApplyIdentities");

                    DeserializeDiag.BeginPhase();
                    var p4 = P4_StructuralChangePhase.Execute(ref _ctx, _policy, em);
                    DeserializeDiag.EndPhase("P4_StructuralChange");
                    if (p4 != SaveLoadErrorCode.None)
                        throw new DeserializePhaseException("P4 removal failed");

                    DeserializeDiag.BeginPhase();
                    P6_ChunkComponentExistencePhase.Execute(ref _ctx, em);
                    DeserializeDiag.EndPhase("P6_EnsureChunkComponents");

                    DeserializeDiag.BeginPhase();
                    var p6 = P6_ChunkComponentRestorePhase.Execute(ref _ctx, em);
                    DeserializeDiag.EndPhase("P6_RestoreChunkValues");
                    if (p6 != SaveLoadErrorCode.None)
                        throw new DeserializePhaseException("P6 failed");

                    if (FaultInjector.ShouldFail("P5"))
                        throw new DeserializePhaseException("Injected P5");

                    sceneSnapshot = DeserializeApplyHelper.CaptureSceneSnapshot(_def.SubScene);

                    DeserializeDiag.BeginPhase();
                    var p35 = P3_5_SceneRestoreExecutionPhase.Execute(
                        _def.SubScene,
                        _ctx.SubSceneRestoreRequests,
                        ref _ctx.Warnings);
                    DeserializeDiag.EndPhase("P3.5_SceneRestore");
                    if (p35 != SaveLoadErrorCode.None)
                    {
                        bool entityOk = _journal.Rollback(_updateArray, em);
                        bool sceneOk = DeserializeApplyHelper.TryRollbackSceneState(
                            sceneSnapshot,
                            _ctx.SubSceneRestoreRequests,
                            _def.SubScene,
                            ref _ctx.Warnings);
                        success = true;

                        if (!entityOk || !sceneOk)
                            _result = LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed);
                        else
                            _result = LoadResult.Failure(p35);
                        return;
                    }

                    DeserializeDiag.BeginPhase();
                    var p65 = P6_5_TombstoneApplyPhase.Execute(ref _ctx, em, _destroyTargets);
                    DeserializeDiag.EndPhase("P6.5_TombstoneApply");
                    if (p65 != SaveLoadErrorCode.None)
                        throw new DeserializePhaseException("P6.5 failed: " + p65);

                    DeserializeDiag.BeginPhase();
                    var p75 = P7_5_PrefabIdentityApplyPhase.Execute(ref _ctx, em);
                    DeserializeDiag.EndPhase("P7.5_PrefabIdentityApply");
                    if (p75 != SaveLoadErrorCode.None)
                        throw new DeserializePhaseException("P7.5 failed: " + p75);

                    if (FaultInjector.ShouldFail("P7"))
                        throw new DeserializePhaseException("Injected P7");

                    if (_destroyTargets.IsCreated && _destroyTargets.Length > 0)
                    {
                        var validTargets = new NativeList<Entity>(
                            _destroyTargets.Length, Allocator.Temp);
                        try
                        {
                            for (int i = 0; i < _destroyTargets.Length; i++)
                            {
                                var e = _destroyTargets[i];
                                if (e != Entity.Null && em.Exists(e))
                                    validTargets.Add(e);
                            }

                            if (validTargets.Length > 0)
                            {
                                DeserializeDiag.BeginPhase();
                                try
                                {
                                    em.DestroyEntity(validTargets.AsArray());
                                }
                                catch (Exception destroyEx)
                                {
                                    UnityEngine.Debug.LogError(
                                        $"[ECSave] DestroyEntity failed; partial destruction may have occurred " +
                                        $"({validTargets.Length} targets, journal rollback skipped): {destroyEx}");
                                    success = true;
                                    _result = LoadResult.Failure(
                                        SaveLoadErrorCode.WorldModificationFailed,
                                        entityCount: _ctx.Header.LogicalEntityCount);
                                    return;
                                }
                                DeserializeDiag.EndPhase("P7_Destroy");
                            }
                        }
                        finally
                        {
                            if (validTargets.IsCreated) validTargets.Dispose();
                        }
                    }

                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        DeserializeDiag.BeginPhase();
                        bool entityOk = _journal.Rollback(_updateArray, em);
                        bool sceneOk = DeserializeApplyHelper.TryRollbackSceneState(
                            sceneSnapshot,
                            _ctx.SubSceneRestoreRequests,
                            _def.SubScene,
                            ref _ctx.Warnings);
                        DeserializeDiag.EndPhase("Rollback");
                        if (!entityOk || !sceneOk)
                            throw new DeserializePhaseException("Rollback failed");
                    }
                }

                DeserializeDiag.BeginPhase();
                List<LoadWarning> warnings;
                try
                {
                    warnings = P8_WarningCollectionPhase.Execute(ref _ctx);
                }
                catch (Exception warnEx)
                {
                    UnityEngine.Debug.LogError(
                        $"[ECSave] Warning collection failed (data was applied successfully): {warnEx}");
                    warnings = new List<LoadWarning>();
                }
                DeserializeDiag.EndPhase("P8_Warnings");

                _result = LoadResult.Success(_ctx.Header.LogicalEntityCount, warnings);
            }
            catch (DeserializePhaseException)
            {
                _result = LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed);
            }
            catch (OperationCanceledException)
            {
                _result = LoadResult.Failure(SaveLoadErrorCode.Cancelled);
            }
            catch (FormatException fe)
            {
                _result = LoadResult.Failure(ErrorInfoMapper.Map(fe.Error));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[ECSave] DeserializeOperation.DoApply threw: {ex}");
                _result = LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_journal != null)
            {
                try { _journal.Dispose(); } catch { }
                _journal = null;
            }
            if (_updateArrayCreated) { _updateArray.Dispose(); _updateArrayCreated = false; }
            if (_updateTargetsCreated) { _updateTargets.Dispose(); _updateTargetsCreated = false; }
            if (_destroyTargetsCreated) { _destroyTargets.Dispose(); _destroyTargetsCreated = false; }
            if (_ctxInitialized) { _ctx.Dispose(); _ctxInitialized = false; }
        }
    }
}
