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

            var err = D0_InputCopyPhase.Execute(ref _ctx, _data, _def, _world, ct);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D1_StructureValidationPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D2_ChecksumVerificationPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D3_VersionCheckPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D4_WorldIdCheckPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D5_SchemaMatchPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D6_MigrationPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D5_5_PrefabMigrationPhase.Execute(ref _ctx, out var prefabIdentities);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.PrefabIdentities = prefabIdentities;

            err = D4_5_SubSceneMatchPhase.Execute(
                _def.SubScene,
                _ctx.SectionTable,
                _ctx.View,
                _ctx.Warnings,
                out var subSceneState,
                out _);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.SubSceneState = subSceneState;

            err = D7_EntityRefValidationPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D8_RecordValidationPhase.Execute(ref _ctx);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = D8_5_TombstoneValidationPhase.Execute(ref _ctx, out var tombstoneEntries);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.TombstoneSections = tombstoneEntries;

            return true;
        }

        private bool DoPlanAndBackup(CancellationToken ct)
        {
            var em = _world.EntityManager;

            var err = P1_PlanPhase.Execute(ref _ctx, _policy);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }

            err = P1_5_SceneRestorePlanPhase.Execute(
                _def.SubScene,
                _ctx.SubSceneState,
                out var restoreRequests);
            if (err != SaveLoadErrorCode.None) { _result = LoadResult.Failure(err); return false; }
            _ctx.SubSceneRestoreRequests = restoreRequests;

            _destroyTargets = new NativeList<Entity>(16, Allocator.Persistent);
            _destroyTargetsCreated = true;

            err = P1_DeletionPlanPhase.Execute(ref _ctx, _scope, _policy, _destroyTargets);
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
            _journal.Backup(_ctx.Definition, _updateArray, em);

            return true;
        }

        private void DoApply(CancellationToken ct)
        {
            var em = _world.EntityManager;
            bool success = false;
            IReadOnlyList<SceneLoadInfo> sceneSnapshot = null;

            try
            {
                try
                {
                    if (FaultInjector.ShouldFail("P3"))
                        throw new InvalidOperationException("Injected P3");

                    CreateEntitiesForNonChunkSections(ref _ctx, _journal, em);

                    for (int i = 0; i < _ctx.SectionTable.Count; i++)
                    {
                        ref var entry = ref _ctx.SectionTable.Entries[i];
                        if (entry.Kind != SectionKind.Identity) continue;

                        var bodyView = SectionBodyHelper.GetSectionBody(_ctx, i);
                        CreateEntitiesForSection(bodyView, _ctx.PersistentToEntity, _journal, em);
                    }

                    if (ct.IsCancellationRequested)
                        throw new InvalidOperationException("Cancelled");

                    if (FaultInjector.ShouldFail("P4"))
                        throw new InvalidOperationException("Injected P4");

                    var p72 = P7_2_FixedStringScanPhase.Execute(ref _ctx);
                    if (p72 != SaveLoadErrorCode.None)
                        throw new InvalidOperationException("P7.2 failed: " + p72);

                    ApplyNonChunkModules(ref _ctx, em);
                    ApplyIdentityModules(ref _ctx, em);

                    if (P4_StructuralChangePhase.Execute(ref _ctx, _policy, em)
                        != SaveLoadErrorCode.None)
                        throw new InvalidOperationException("P4 removal failed");

                    ApplyChunkModules(ref _ctx, em);

                    if (P6_ChunkComponentRestorePhase.Execute(ref _ctx, em)
                        != SaveLoadErrorCode.None)
                        throw new InvalidOperationException("P6 failed");

                    if (FaultInjector.ShouldFail("P5"))
                        throw new InvalidOperationException("Injected P5");

                    sceneSnapshot = CaptureSceneSnapshot(_def.SubScene);

                    var p35 = P3_5_SceneRestoreExecutionPhase.Execute(
                        _def.SubScene,
                        _ctx.SubSceneRestoreRequests,
                        ref _ctx.Warnings);
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

                    var p65 = P6_5_TombstoneApplyPhase.Execute(ref _ctx, em, _destroyTargets);
                    if (p65 != SaveLoadErrorCode.None)
                        throw new InvalidOperationException("P6.5 failed: " + p65);

                    var p75 = P7_5_PrefabIdentityApplyPhase.Execute(ref _ctx, em);
                    if (p75 != SaveLoadErrorCode.None)
                        throw new InvalidOperationException("P7.5 failed: " + p75);

                    if (FaultInjector.ShouldFail("P7"))
                        throw new InvalidOperationException("Injected P7");

                    if (_destroyTargets.IsCreated && _destroyTargets.Length > 0)
                    {
                        var validTargets = new NativeList<Entity>(_destroyTargets.Length, Allocator.Temp);
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
                        bool entityOk = _journal.Rollback(_updateArray, em);
                        bool sceneOk = TryRollbackSceneState(
                            sceneSnapshot,
                            _ctx.SubSceneRestoreRequests,
                            _def.SubScene,
                            ref _ctx.Warnings);
                        if (!entityOk || !sceneOk)
                            throw new InvalidOperationException("Rollback failed");
                    }
                }

                var warnings = new List<LoadWarning>();
                _ctx.Warnings.ToLoadWarnings(warnings, BuildTypeNameMap(ref _ctx));

                _result = LoadResult.Success(_ctx.Header.LogicalEntityCount, warnings);
            }
            catch (FormatException fe)
            {
                _result = LoadResult.Failure(ErrorInfoMapper.Map(fe.Error));
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException))
                    UnityEngine.Debug.LogError($"[ECSave] DeserializeOperation.DoApply threw: {ex}");
                _result = LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed);
            }
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

        private static void CreateEntitiesForNonChunkSections(
            ref PrephaseContext ctx,
            ApplyJournal journal,
            EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind == SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                CreateEntitiesForSection(bodyView, ctx.PersistentToEntity, journal, em);
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

        private static unsafe void CreateEntitiesForSection(
            ByteView bodyView,
            NativeArray<Entity> persistentToEntity,
            ApplyJournal journal,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(bodyView);
            int count = (int)v.Header.Count;

            for (int k = 0; k < count; k++)
            {
                uint pid = v.EntityIndex[k];
                if (pid >= persistentToEntity.Length)
                    throw new FormatException(
                        FormatError.ValidationFailed,
                        "persistent id out of range");

                if (persistentToEntity[(int)pid] == Entity.Null)
                {
                    var e = em.CreateEntity();
                    persistentToEntity[(int)pid] = e;
                    journal.NoteCreated(e);
                }
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
