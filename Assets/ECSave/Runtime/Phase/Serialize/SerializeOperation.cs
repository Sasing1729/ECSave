using System;
using System.Threading;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Serialize;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
    internal sealed unsafe class SerializeOperation : IDisposable
    {
        private enum Stage : byte
        {
            Start = 0,
            AsyncScheduled = 1,
            W1W2Done = 2,
            Done = 3,
        }

        private readonly SaveDefinition _def;
        private readonly World _world;
        private readonly Action<OperationPhase> _onPhaseChanged;

        private Stage _stage;
        private SaveResult _result;
        private bool _disposed;
        private OperationPhase _lastNotifiedPhase = OperationPhase.None;

        private SnapshotBuffer[] _buffers;
        private NativeParallelHashMap<Entity, int> _idOf;
        private NativeList<Entity> _entityList;
        private byte[] _sealedBytes;

        private JobHandle _pendingHandle;
        private bool _hasPendingJobs;

        public bool IsComplete => _stage == Stage.Done;
        public SaveResult Result => _result;

        public OperationPhase CurrentPhase => _stage switch
        {
            Stage.Start => OperationPhase.S1_AwaitSnapshot,
            Stage.AsyncScheduled => OperationPhase.S2_Encode,
            Stage.W1W2Done => OperationPhase.S3_Seal,
            Stage.Done => OperationPhase.S4_Complete,
            _ => OperationPhase.None,
        };

        public SerializeOperation(
            SaveDefinition def,
            World world,
            Action<OperationPhase> onPhaseChanged = null)
        {
            _def = def;
            _world = world;
            _onPhaseChanged = onPhaseChanged;
        }

        private void NotifyPhase(OperationPhase phase)
        {
            if (phase == _lastNotifiedPhase) return;
            _lastNotifiedPhase = phase;
            _onPhaseChanged?.Invoke(phase);
        }

        public SaveResult Execute(CancellationToken ct)
        {
            try
            {
                while (!MoveNextCore(ct, blockOnAsync: true)) { }
                return _result;
            }
            finally
            {
                Dispose();
            }
        }

        public bool MoveNext(CancellationToken ct)
        {
            return MoveNextCore(ct, blockOnAsync: true);
        }

        private bool MoveNextCore(CancellationToken ct, bool blockOnAsync)
        {
            if (_stage == Stage.Done) return true;

            try
            {
                switch (_stage)
                {
                    case Stage.Start:
                        NotifyPhase(OperationPhase.S1_AwaitSnapshot);
                        DoScheduleAsync(ct);
                        _stage = Stage.AsyncScheduled;
                        return false;

                    case Stage.AsyncScheduled:
                        NotifyPhase(OperationPhase.S2_Encode);
                        if (!DoCompleteAsyncAndFilter(ct, blockOnAsync)) return false;
                        _stage = Stage.W1W2Done;
                        return false;

                    case Stage.W1W2Done:
                        NotifyPhase(OperationPhase.S3_Seal);
                        DoEncodeAndSeal(ct);
                        _stage = Stage.Done;
                        if (_result.IsSuccess)
                            NotifyPhase(OperationPhase.S4_Complete);
                        return true;
                }
            }
            catch (Format.FormatException fe)
            {
                CancelPendingAsync();
                _result = SaveResult.Failure(ErrorInfoMapper.Map(fe.Error));
                _stage = Stage.Done;
                return true;
            }
            catch (OperationCanceledException)
            {
                CancelPendingAsync();
                _result = SaveResult.Failure(SaveLoadErrorCode.Cancelled);
                _stage = Stage.Done;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[ECSave] SerializeOperation.MoveNext threw in {_stage}: {ex}");
                CancelPendingAsync();
                _result = SaveResult.Failure(SaveLoadErrorCode.SerializationFailed);
                _stage = Stage.Done;
                return true;
            }

            return true;
        }

        private void DoScheduleAsync(CancellationToken ct)
        {
            var em = _world.EntityManager;
            int n = _def.Modules.Count;

            _buffers = new SnapshotBuffer[n];

            JobHandle combined = default;
            bool anyAsyncScheduled = false;

            for (int i = 0; i < n; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                var mod = _def.Modules[i];
                _buffers[i] = new SnapshotBuffer(256, mod.RawRecordStride, Allocator.Persistent);

                if (mod.HasEnabledBits)
                    _buffers[i].InitEnabledStates(Allocator.Persistent);

                var query = mod.BuildQuery(em);

                if (mod.SupportsAsyncCapture)
                {
                    combined = mod.ScheduleAsyncCapture(query, em, ref _buffers[i], combined);
                    anyAsyncScheduled = true;
                }
                else
                {
                    mod.Capture(query, em, ref _buffers[i]);
                }
            }

            _pendingHandle = combined;
            _hasPendingJobs = anyAsyncScheduled;
        }

        private bool DoCompleteAsyncAndFilter(CancellationToken ct, bool blockOnAsync)
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            if (_hasPendingJobs)
            {
                if (blockOnAsync)
                {
                    _pendingHandle.Complete();
                }
                else
                {
                    if (!_pendingHandle.IsCompleted)
                        return false;
                    _pendingHandle.Complete();
                }
                _hasPendingJobs = false;
            }

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            var em = _world.EntityManager;
            int n = _def.Modules.Count;

            for (int i = 0; i < n; i++)
            {
                var mod = _def.Modules[i];
                if (!mod.SupportsAsyncCapture) continue;
                mod.CompleteAsyncCapture(em, ref _buffers[i]);
            }

            int totalUpperBound = 0;
            for (int i = 0; i < n; i++)
            {
                if (!_def.Modules[i].ContributesToIdOf) continue;
                totalUpperBound += _buffers[i].Count;
            }

            int initialCap = totalUpperBound > 0 ? totalUpperBound : 16;

            _idOf = new NativeParallelHashMap<Entity, int>(initialCap, Allocator.Persistent);
            _entityList = new NativeList<Entity>(initialCap, Allocator.Persistent);

            for (int i = 0; i < n; i++)
            {
                var buffer = _buffers[i];

                // 通常Entity
                var entities = buffer.Entities;

                for (int j = 0; j < entities.Length; j++)
                {
                    var e = entities[j];

                    if (_idOf.TryAdd(e, _entityList.Length))
                        _entityList.Add(e);
                }

                // ChunkComponent メンバー
                if (buffer.HasChunkMembers)
                {
                    var members = buffer.ChunkMemberEntities;

                    for (int j = 0; j < members.Length; j++)
                    {
                        var e = members[j];

                        if (_idOf.TryAdd(e, _entityList.Length))
                            _entityList.Add(e);
                    }
                }
            }

            // Review #10: Selection で追加された Entity も _idOf に載せる。
            AddSelections(em, _idOf, _entityList, ct);

            // ChunkComponent 等 (ContributesToIdOf=false) は _idOf でフィルタ
            for (int i = 0; i < n; i++)
            {
                if (_def.Modules[i].ContributesToIdOf) continue;
                _def.Modules[i].FilterAfterIdOf(ref _buffers[i], _idOf);
            }

            return true;
        }

        private void DoEncodeAndSeal(CancellationToken ct)
        {
            var em = _world.EntityManager;
            int n = _def.Modules.Count;
            int m = _def.Identities != null ? _def.Identities.Count : 0;

            var subSceneResult = S2_5_SubSceneCapturePhase.Execute(_def.SubScene);
            var tombstoneResult = S2_6_TombstoneCapturePhase.Execute(
                _def.SubScene, _world, _def.Identities);
            var prefabResult = S2_7_PrefabIdentityCapturePhase.Execute(
                _def.SubScene, _entityList, _idOf);

            int logicalEntityCount = _entityList.Length;

            var layout = SaveLayoutComputer.Compute(
                _def, _world, logicalEntityCount, _buffers, em, _idOf,
                subSceneResult, tombstoneResult, prefabResult);

            // P-3: LOH 圧力削減のため、素の SaveData は NativeArray に書き、
            // 圧縮結果のみ byte[] にコピーする。
            using var rawNative = new NativeArray<byte>(layout.TotalSize, Allocator.TempJob);
            byte* p = (byte*)rawNative.GetUnsafePtr();
            SaveDataWriter.Write(
                p, in layout, _def, _world, _entityList, _idOf, _buffers, em, ct,
                subSceneResult, tombstoneResult, prefabResult);

            _sealedBytes = SaveCompression.Compress(p, layout.TotalSize);
            _result = SaveResult.Success(_sealedBytes);
        }

        private void CancelPendingAsync()
        {
            if (_hasPendingJobs)
            {
                try { _pendingHandle.Complete(); } catch { }
                _hasPendingJobs = false;
            }

            if (_buffers != null)
            {
                for (int i = 0; i < _buffers.Length; i++)
                {
                    if (_buffers[i].IsCreated)
                    {
                        try { _def.Modules[i].CancelAsyncCapture(); } catch { }
                        try { _buffers[i].Dispose(); } catch { }
                    }
                }
                _buffers = null;
            }
        }

        private void AddSelections(
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf,
            NativeList<Entity> entityList,
            CancellationToken ct)
        {
            var selections = _def.Selections;
            if (selections == null || selections.Count == 0) return;

            var selected = new NativeParallelHashSet<Entity>(64, Allocator.TempJob);
            try
            {
                for (int i = 0; i < selections.Count; i++)
                {
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);

                    var sel = selections[i];
                    var q = sel.BuildQuery(em);
                    sel.ExtractSelected(q, em, selected);
                }

                foreach (var e in selected)
                {
                    if (e == Entity.Null) continue;
                    if (!em.Exists(e)) continue;

                    if (idOf.TryAdd(e, entityList.Length))
                        entityList.Add(e);
                }
            }
            finally
            {
                if (selected.IsCreated) selected.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancelPendingAsync();

            if (_idOf.IsCreated) _idOf.Dispose();
            if (_entityList.IsCreated) _entityList.Dispose();

            if (_buffers != null)
            {
                for (int i = 0; i < _buffers.Length; i++)
                {
                    if (_buffers[i].IsCreated)
                        _buffers[i].Dispose();
                }
                _buffers = null;
            }
        }
    }
}
