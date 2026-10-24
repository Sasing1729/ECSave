using System;
using System.Text;
using System.Threading;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Serialize;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

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
            // MoveNext は「次フェーズへ進む」を1呼び出しで完結させる。
            // フレーム境界モードで SaveRuntimeSystem.OnUpdate から駆動される場合、
            // 各フレームでジョブを .Complete() で待機するが、ECS のキャプチャジョブは
            // 典型的には数マイクロ秒で完了するため、フレーム落ちは軽微。
            // テストや同期呼び出し元は MoveNext 1回で次フェーズへ進むことを期待している。
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
                        // 成功時のみ S4 を排出する。DoEncodeAndSeal は例外を投げれば
                        // 外側 catch で Failure に変換されるが、将来 DoEncodeAndSeal が
                        // Failure を返す経路が追加された場合の防御として残す。
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
                if (!_def.Modules[i].ContributesToIdOf) continue;
                var entities = _buffers[i].Entities;
                for (int j = 0; j < entities.Length; j++)
                {
                    var e = entities[j];
                    if (_idOf.TryAdd(e, _entityList.Length))
                        _entityList.Add(e);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (_def.Modules[i].ContributesToIdOf) continue;
                _def.Modules[i].FilterAfterIdOf(ref _buffers[i], _idOf);
            }

            AddSelections(em, _idOf, _entityList, ct);

            return true;
        }

        private void DoEncodeAndSeal(CancellationToken ct)
        {
            var em = _world.EntityManager;
            int n = _def.Modules.Count;
            int m = _def.Identities != null ? _def.Identities.Count : 0;

            var subSceneResult = S2_5_SubSceneCapturePhase.Execute(_def.SubScene);
            var tombstoneResult = S2_6_TombstoneCapturePhase.Execute(_def.SubScene, _world);
            var prefabResult = S2_7_PrefabIdentityCapturePhase.Execute(
                _def.SubScene, _entityList, _idOf);

            int logicalEntityCount = _entityList.Length;

            var layout = ComputeLayout(
                logicalEntityCount, _buffers, em, _idOf,
                subSceneResult, tombstoneResult, prefabResult);

            byte[] result = new byte[layout.TotalSize];
            fixed (byte* p = result)
            {
                WriteSaveData(
                    p, layout, _entityList, _idOf, _buffers, em, ct,
                    subSceneResult, tombstoneResult, prefabResult);
            }

            _sealedBytes = SaveCompression.Compress(result);
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

        private struct Layout
        {
            public int HeaderFixedSize;
            public int WorldIdLength;
            public int SectionTableOffset;
            public int NameHeapOffset;
            public int NameHeapLength;
            public int SectionsStart;
            public int[] SectionOffsets;
            public int[] SectionLengths;
            public int[] NameOffsets;
            public int[] NameLengths;
            public int FooterOffset;
            public int TotalSize;
            public int TotalSections;
            public bool HasSubScene;
            public int TombstoneCount;
            public bool HasPrefabIdentity;
        }

        private Layout ComputeLayout(
            int logicalEntityCount,
            SnapshotBuffer[] buffers,
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf,
            S2_5_SubSceneCapturePhase.Result subSceneResult,
            S2_6_TombstoneCapturePhase.Result tombstoneResult,
            S2_7_PrefabIdentityCapturePhase.Result prefabResult)
        {
            int n = _def.Modules.Count;
            int m = _def.Identities != null ? _def.Identities.Count : 0;
            bool hasSubScene = subSceneResult.HasSection;
            int tombstoneCount = tombstoneResult.Captured.Count;
            bool hasPrefabIdentity = prefabResult.HasSection;
            int total = n + m
                + (hasSubScene ? 1 : 0)
                + tombstoneCount
                + (hasPrefabIdentity ? 1 : 0);

            var layout = new Layout
            {
                HeaderFixedSize = ECSaveFormatConstants.HeaderFixedSize,
                WorldIdLength = Encoding.UTF8.GetByteCount(_def.WorldId),
                SectionOffsets = new int[total],
                SectionLengths = new int[total],
                NameOffsets = new int[total],
                NameLengths = new int[total],
                TotalSections = total,
                HasSubScene = hasSubScene,
                TombstoneCount = tombstoneCount,
                HasPrefabIdentity = hasPrefabIdentity,
            };

            int headerEnd = layout.HeaderFixedSize + layout.WorldIdLength;
            layout.SectionTableOffset = AlignUp(headerEnd, 8);
            layout.NameHeapOffset = layout.SectionTableOffset + total * ECSaveFormatConstants.SectionEntrySize;

            int nameHeapCursor = 0;
            for (int i = 0; i < n; i++)
            {
                layout.NameOffsets[i] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(_def.Modules[i].FullTypeName);
                layout.NameLengths[i] = len;
                nameHeapCursor += len;
            }
            for (int i = 0; i < m; i++)
            {
                int idx = n + i;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(_def.Identities[i].FullTypeName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            if (hasSubScene)
            {
                int idx = n + m;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(SubSceneStateSection.SectionName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            for (int i = 0; i < tombstoneCount; i++)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + i;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(tombstoneResult.Captured[i].SectionName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            if (hasPrefabIdentity)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + tombstoneCount;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(PrefabIdentitySection.SectionName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            layout.NameHeapLength = nameHeapCursor;

            layout.SectionsStart = AlignUp(layout.NameHeapOffset + layout.NameHeapLength, 8);

            int cursor = layout.SectionsStart;
            for (int i = 0; i < n; i++)
            {
                layout.SectionOffsets[i] = cursor;

                var mod = _def.Modules[i];
                int typeDescSize = ComputeTypeDescSize(
                    mod.SchemaHashInput.Length, mod.FieldNames);
                int bodySize = mod.ComputeBodySize(buffers[i]);

                layout.SectionLengths[i] = typeDescSize + bodySize;
                cursor += layout.SectionLengths[i];
                cursor = AlignUp(cursor, 8);
            }
            for (int i = 0; i < m; i++)
            {
                int idx = n + i;
                layout.SectionOffsets[idx] = cursor;

                var im = _def.Identities[i];
                int typeDescSize = ComputeTypeDescSize(
                    im.SchemaHashInput.Length, im.FieldNames);
                int bodySize = im.ComputeBodySize(em, idOf);

                layout.SectionLengths[idx] = typeDescSize + bodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }
            if (hasSubScene)
            {
                int idx = n + m;
                layout.SectionOffsets[idx] = cursor;
                layout.SectionLengths[idx] = subSceneResult.SectionBodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }
            for (int i = 0; i < tombstoneCount; i++)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + i;
                layout.SectionOffsets[idx] = cursor;
                layout.SectionLengths[idx] = tombstoneResult.Captured[i].SectionBodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }
            if (hasPrefabIdentity)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + tombstoneCount;
                layout.SectionOffsets[idx] = cursor;
                layout.SectionLengths[idx] = prefabResult.SectionBodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }

            layout.FooterOffset = cursor;
            layout.TotalSize = layout.FooterOffset + ECSaveFormatConstants.FooterSize;
            return layout;
        }

        private static int ComputeTypeDescSize(int inputLen, string[] fieldNames)
        {
            int utf8Total = 0;
            for (int i = 0; i < fieldNames.Length; i++)
                utf8Total += Encoding.UTF8.GetByteCount(fieldNames[i]);
            return TypeDescriptorBlockWriter.ComputeSize(inputLen, fieldNames.Length, utf8Total);
        }

        private void WriteSaveData(
            byte* p, in Layout layout,
            NativeList<Entity> entities,
            NativeParallelHashMap<Entity, int> idOf,
            SnapshotBuffer[] buffers,
            EntityManager em,
            CancellationToken ct,
            S2_5_SubSceneCapturePhase.Result subSceneResult,
            S2_6_TombstoneCapturePhase.Result tombstoneResult,
            S2_7_PrefabIdentityCapturePhase.Result prefabResult)
        {
            int n = _def.Modules.Count;
            int m = _def.Identities != null ? _def.Identities.Count : 0;

            var header = new SaveHeader
            {
                Magic = ECSaveFormatConstants.Magic,
                FormatVersion = ECSaveFormatConstants.CurrentFormatVersion,
                HeaderFlags = 0,
                HeaderSize = (uint)(layout.HeaderFixedSize + layout.WorldIdLength),
                GameDataVersion = _def.GameDataVersion,
                LogicalEntityCount = entities.Length,
                SectionCount = layout.TotalSections,
                SectionTableOffset = (ulong)layout.SectionTableOffset,
                TotalLength = (ulong)layout.TotalSize,
                NameHeapLength = (uint)layout.NameHeapLength,
                WorldIdLength = (ushort)layout.WorldIdLength,
                Reserved = 0,
            };

            var writer = new FormatWriter(p, layout.TotalSize);
            writer.WriteHeader(header);

            var worldIdBytes = Encoding.UTF8.GetBytes(_def.WorldId);
            if (worldIdBytes.Length > 0)
            {
                fixed (byte* wid = worldIdBytes)
                    writer.WriteBytes(wid, worldIdBytes.Length);
            }

            writer.Seek(layout.SectionTableOffset);
            for (int i = 0; i < n; i++)
            {
                var mod = _def.Modules[i];
                var entry = new SectionEntry
                {
                    SectionId = (uint)i,
                    Kind = mod.SectionKind,
                    Flags = ECSaveFormatConstants.SectionEntryFlags_HasTypeDescriptor,
                    NameLength = (ushort)layout.NameLengths[i],
                    NameHash = SchemaHashCore.HashUtf8(mod.FullTypeName),
                    SchemaHash = mod.SchemaHash,
                    Offset = (ulong)layout.SectionOffsets[i],
                    Length = (ulong)layout.SectionLengths[i],
                    NameOffset = (uint)layout.NameOffsets[i],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            for (int i = 0; i < m; i++)
            {
                int idx = n + i;
                var im = _def.Identities[i];
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.Identity,
                    Flags = ECSaveFormatConstants.SectionEntryFlags_HasTypeDescriptor,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SchemaHashCore.HashUtf8(im.FullTypeName),
                    SchemaHash = im.SchemaHash,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            if (layout.HasSubScene)
            {
                int idx = n + m;
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.SubSceneState,
                    Flags = 0,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SchemaHashCore.HashUtf8(SubSceneStateSection.SectionName),
                    SchemaHash = 0,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            for (int i = 0; i < layout.TombstoneCount; i++)
            {
                int idx = n + m + (layout.HasSubScene ? 1 : 0) + i;
                var cap = tombstoneResult.Captured[i];
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.SubSceneTombstone,
                    Flags = 0,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SubSceneTombstoneSection.NameHashFor(cap.Handler.FullTypeName),
                    SchemaHash = 0,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            if (layout.HasPrefabIdentity)
            {
                int idx = n + m + (layout.HasSubScene ? 1 : 0) + layout.TombstoneCount;
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.PrefabIdentity,
                    Flags = 0,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SchemaHashCore.HashUtf8(PrefabIdentitySection.SectionName),
                    SchemaHash = 0,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }

            writer.Seek(layout.NameHeapOffset);
            for (int i = 0; i < n; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(_def.Modules[i].FullTypeName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            for (int i = 0; i < m; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(_def.Identities[i].FullTypeName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            if (layout.HasSubScene)
            {
                var nameBytes = Encoding.UTF8.GetBytes(SubSceneStateSection.SectionName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            for (int i = 0; i < layout.TombstoneCount; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(tombstoneResult.Captured[i].SectionName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            if (layout.HasPrefabIdentity)
            {
                var nameBytes = Encoding.UTF8.GetBytes(PrefabIdentitySection.SectionName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                writer.Seek(layout.SectionOffsets[i]);
                var mod = _def.Modules[i];

                var input = mod.SchemaHashInput;
                fixed (byte* inputPtr = input)
                {
                    TypeDescriptorBlockWriter.Write(
                        ref writer,
                        entryCount: (ushort)mod.FieldNames.Length,
                        rawEntryCount: 0,
                        schemaHashInput: inputPtr,
                        schemaHashInputLength: input.Length,
                        fieldNames: mod.FieldNames);
                }

                mod.WriteSectionBody(ref writer, buffers[i], idOf);
            }
            for (int i = 0; i < m; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + i;
                writer.Seek(layout.SectionOffsets[idx]);
                var im = _def.Identities[i];

                var input = im.SchemaHashInput;
                fixed (byte* inputPtr = input)
                {
                    TypeDescriptorBlockWriter.Write(
                        ref writer,
                        entryCount: (ushort)im.FieldNames.Length,
                        rawEntryCount: 0,
                        schemaHashInput: inputPtr,
                        schemaHashInputLength: input.Length,
                        fieldNames: im.FieldNames);
                }

                im.WriteSectionBody(ref writer, em, idOf);
            }
            if (layout.HasSubScene)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + m;
                writer.Seek(layout.SectionOffsets[idx]);
                SubSceneStateSection.Write(ref writer, subSceneResult.Scenes);
            }
            for (int i = 0; i < layout.TombstoneCount; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + m + (layout.HasSubScene ? 1 : 0) + i;
                writer.Seek(layout.SectionOffsets[idx]);
                var cap = tombstoneResult.Captured[i];
                SubSceneTombstoneSection.Write(ref writer, cap.Keys, cap.Handler.KeyStride);
            }
            if (layout.HasPrefabIdentity)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + m + (layout.HasSubScene ? 1 : 0) + layout.TombstoneCount;
                writer.Seek(layout.SectionOffsets[idx]);
                PrefabIdentitySection.Write(ref writer, prefabResult.Records);
            }

            int checksumEnd = layout.FooterOffset;
            ulong checksum = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));

            writer.Seek(layout.FooterOffset);
            writer.WriteFooter(checksum);
        }

        private static int AlignUp(int value, int alignment)
        {
            int rem = value % alignment;
            return rem == 0 ? value : value + (alignment - rem);
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
