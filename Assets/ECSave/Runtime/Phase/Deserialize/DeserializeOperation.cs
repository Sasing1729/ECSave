using System;
using System.Collections.Generic;
using System.Threading;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
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

        private void DoApply(CancellationToken ct)
        {
            var em = _world.EntityManager;

            DeserializeDiag.BeginPhase();
            var staleErr = ValidateUpdateTargets(em, _updateTargets);
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
                    CreateEntitiesForAllSections(ref _ctx, _journal, em);
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
                    ApplyNonChunkModules(ref _ctx, em);
                    DeserializeDiag.EndPhase("P4_ApplyModules");

                    DeserializeDiag.BeginPhase();
                    ApplyIdentityModules(ref _ctx, em);
                    DeserializeDiag.EndPhase("P4_ApplyIdentities");

                    DeserializeDiag.BeginPhase();
                    var p4 = P4_StructuralChangePhase.Execute(ref _ctx, _policy, em);
                    DeserializeDiag.EndPhase("P4_StructuralChange");
                    if (p4 != SaveLoadErrorCode.None)
                        throw new DeserializePhaseException("P4 removal failed");

                    DeserializeDiag.BeginPhase();
                    ApplyChunkModules(ref _ctx, em);
                    DeserializeDiag.EndPhase("P6_AddChunkComponents");

                    DeserializeDiag.BeginPhase();
                    var p6 = P6_ChunkComponentRestorePhase.Execute(ref _ctx, em);
                    DeserializeDiag.EndPhase("P6_RestoreChunks");
                    if (p6 != SaveLoadErrorCode.None)
                        throw new DeserializePhaseException("P6 failed");

                    if (FaultInjector.ShouldFail("P5"))
                        throw new DeserializePhaseException("Injected P5");

                    sceneSnapshot = CaptureSceneSnapshot(_def.SubScene);

                    DeserializeDiag.BeginPhase();
                    var p35 = P3_5_SceneRestoreExecutionPhase.Execute(
                        _def.SubScene,
                        _ctx.SubSceneRestoreRequests,
                        ref _ctx.Warnings);
                    DeserializeDiag.EndPhase("P3.5_SceneRestore");
                    if (p35 != SaveLoadErrorCode.None)
                    {
                        bool entityOk = _journal.Rollback(_updateArray, em);
                        bool sceneOk = TryRollbackSceneState(
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
                        bool sceneOk = TryRollbackSceneState(
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
                var warnings = new List<LoadWarning>();
                _ctx.Warnings.ToLoadWarnings(warnings, BuildTypeNameMap(ref _ctx));
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

        private static SaveLoadErrorCode ValidateUpdateTargets(
            EntityManager em, NativeList<Entity> updateTargets)
        {
            if (!updateTargets.IsCreated) return SaveLoadErrorCode.None;

            for (int i = 0; i < updateTargets.Length; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null) continue;
                if (!em.Exists(e))
                    return SaveLoadErrorCode.WorldModificationFailed;
            }
            return SaveLoadErrorCode.None;
        }

        private static IReadOnlyList<SceneLoadInfo> CaptureSceneSnapshot(SubSceneDefinition def)
        {
            if (def == null) return null;
            if (!def.WithSceneStateRollback) return null;
            if (def.SceneStateProvider == null) return null;
            if (def.SceneRestoreCallback == null) return null;

            try
            {
                return def.SceneStateProvider() ?? Array.Empty<SceneLoadInfo>();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ECSave] Failed to capture scene snapshot: {ex}");
                return null;
            }
        }

        private static bool TryRollbackSceneState(
            IReadOnlyList<SceneLoadInfo> snapshot,
            SceneLoadRequest[] executedRequests,
            SubSceneDefinition def,
            ref WarningCollector warnings)
        {
            if (def == null || !def.WithSceneStateRollback) return true;
            if (def.SceneRestoreCallback == null) return true;

            try
            {
                IReadOnlyList<SceneLoadInfo> target = snapshot;
                if (target == null && executedRequests != null && executedRequests.Length > 0)
                {
                    var inverse = new SceneLoadInfo[executedRequests.Length];
                    for (int i = 0; i < executedRequests.Length; i++)
                    {
                        var r = executedRequests[i];
                        var invState = r.TargetState == SceneLoadState.Loaded
                            ? SceneLoadState.Unloaded
                            : SceneLoadState.Loaded;
                        inverse[i] = new SceneLoadInfo(r.Guid, invState, r.Mode, r.ContentHash);
                    }
                    target = inverse;
                }

                if (target == null || target.Count == 0) return true;

                var current = new Dictionary<SceneGuid, SceneLoadState>();
                if (def.SceneStateProvider != null)
                {
                    try
                    {
                        var cur = def.SceneStateProvider();
                        if (cur != null)
                        {
                            for (int i = 0; i < cur.Count; i++)
                                current[cur[i].Guid] = cur[i].State;
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError(
                            $"[ECSave] SceneStateProvider threw during rollback: {ex}");
                    }
                }

                var restore = new List<SceneLoadRequest>();
                for (int i = 0; i < target.Count; i++)
                {
                    var s = target[i];
                    if (current.TryGetValue(s.Guid, out var curState) && curState == s.State) continue;
                    restore.Add(new SceneLoadRequest(s.Guid, s.State, s.RestoreMode, s.ContentHash));
                }

                if (restore.Count == 0) return true;

                var outcomes = def.SceneRestoreCallback(restore.ToArray());
                if (outcomes == null) return true;

                bool allOk = true;
                int limit = Math.Min(outcomes.Length, restore.Count);
                for (int i = 0; i < limit; i++)
                {
                    if (!outcomes[i].Success)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[ECSave] Scene rollback failed for {restore[i].Guid}: {outcomes[i].FailureReason}");
                        allOk = false;
                    }
                }

                if (allOk)
                {
                    warnings.Add(WarningRecord.Create(
                        LoadWarningKind.SceneStateRolledBack, 0, -1, Entity.Null));
                }
                return allOk;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ECSave] Scene rollback threw: {ex}");
                return false;
            }
        }

        /// <summary>
        /// P-1: 全セクション（Identity 含む、ChunkComponent 除く）を事前スキャンして
        /// 一意な pid を収集し、一括で空 Entity を生成する。
        ///
        /// <para>
        /// <b>重要な不変条件</b>: <c>PersistentToEntity[pid]</c> に既に非 Null が
        /// 入っている pid（<see cref="P1_PlanPhase"/> で識別マッチした既存 Entity）は
        /// <b>スキップ</b>する。これを怠ると、マッチした既存 Entity への参照が
        /// 新規生成 Entity で上書きされ、識別照合の結果が失われる。
        /// </para>
        /// </summary>
        private static unsafe void CreateEntitiesForAllSections(
            ref PrephaseContext ctx,
            ApplyJournal journal,
            EntityManager em)
        {
            int maxPids = ctx.Header.LogicalEntityCount;
            int initialCap = maxPids > 0 ? maxPids : 16;

            var pidsToCreate = new NativeList<uint>(initialCap, Allocator.TempJob);
            var seen = new NativeParallelHashSet<uint>(initialCap, Allocator.TempJob);

            try
            {
                for (int i = 0; i < ctx.SectionTable.Count; i++)
                {
                    ref var entry = ref ctx.SectionTable.Entries[i];
                    var m = ctx.ModuleBySection[i];

                    bool isNonChunk = m != null && m.SectionKind != SectionKind.ChunkComponent;
                    bool isIdentity = entry.Kind == SectionKind.Identity;

                    if (!isNonChunk && !isIdentity) continue;

                    ByteView bodyView;
                    try
                    {
                        bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                    }
                    catch (FormatException)
                    {
                        continue;
                    }

                    var v = CommonSectionReader.Read(bodyView);
                    int count = (int)v.Header.Count;

                    for (int k = 0; k < count; k++)
                    {
                        uint pid = v.EntityIndex[k];
                        if (pid >= ctx.PersistentToEntity.Length)
                            throw new FormatException(
                                FormatError.ValidationFailed,
                                "persistent id out of range");

                        // 識別照合で既に既存 Entity が割り当てられている pid はスキップ。
                        // ここで上書きするとマッチ結果が失われる。
                        if (ctx.PersistentToEntity[(int)pid] != Entity.Null) continue;

                        if (seen.Add(pid))
                            pidsToCreate.Add(pid);
                    }
                }

                if (pidsToCreate.Length == 0) return;

                var emptyArchetype = em.CreateArchetype();
                var entities = em.CreateEntity(emptyArchetype, pidsToCreate.Length, Allocator.TempJob);
                try
                {
                    for (int i = 0; i < pidsToCreate.Length; i++)
                    {
                        uint pid = pidsToCreate[i];
                        ctx.PersistentToEntity[(int)pid] = entities[i];
                        journal.NoteCreated(entities[i]);
                    }
                }
                finally
                {
                    entities.Dispose();
                }
            }
            finally
            {
                pidsToCreate.Dispose();
                seen.Dispose();
            }
        }

        private static void ApplyNonChunkModules(ref PrephaseContext ctx, EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind == SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                m.ApplySectionBody(bodyView, ctx.PersistentToEntity, em);
            }
        }

        private static void ApplyIdentityModules(ref PrephaseContext ctx, EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                ref var entry = ref ctx.SectionTable.Entries[i];
                if (entry.Kind != SectionKind.Identity) continue;

                var im = ctx.Definition.FindIdentity(entry.NameHash);
                if (im == null) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                im.ApplySectionBody(bodyView, ctx.PersistentToEntity, em);
            }
        }

        private static void ApplyChunkModules(ref PrephaseContext ctx, EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind != SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                m.ApplySectionBody(bodyView, ctx.PersistentToEntity, em);
            }
        }

        private static unsafe Dictionary<uint, string> BuildTypeNameMap(ref PrephaseContext ctx)
        {
            var map = new Dictionary<uint, string>();
            var table = ctx.SectionTable;
            if (table.Entries == null) return map;

            for (int i = 0; i < table.Count; i++)
            {
                var m = ctx.ModuleBySection != null ? ctx.ModuleBySection[i] : null;
                if (m != null)
                {
                    map[(uint)i] = m.FullTypeName;
                    continue;
                }

                ref var entry = ref table.Entries[i];
                if (entry.NameLength == 0) continue;
                if (table.NameHeap == null) continue;

                long nameStart = (long)entry.NameOffset;
                long nameEnd = nameStart + entry.NameLength;
                if (nameStart < 0 || nameEnd > table.NameHeapLength) continue;

                try
                {
                    var name = System.Text.Encoding.UTF8.GetString(
                        table.NameHeap + nameStart, entry.NameLength);
                    if (!string.IsNullOrEmpty(name))
                        map[(uint)i] = name;
                }
                catch
                {
                }
            }

            return map;
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
