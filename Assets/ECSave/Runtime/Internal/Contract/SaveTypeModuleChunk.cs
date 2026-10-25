using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// チャンクコンポーネント（<c>ComponentType.ChunkComponentReadOnly&lt;T&gt;()</c>）のモジュール。
    ///
    /// <para>
    /// <b>チャンクコンポーネントの特性</b>: 値はチャンク（Archetype）単位で共有される。
    /// 同じチャンクに属する複数 Entity は同じ値を持つ。
    /// </para>
    /// <para>
    /// <b>復元</b>: <see cref="RestoreSection"/> で各メンバー Entity に ChunkComponent を
    /// 付与（存在確保）し、<see cref="RestoreChunks"/> で代表メンバーを通じて値を書き込む
    /// （2 フェーズ構成）。
    /// </para>
    /// <para>
    /// <b>Chunk のみを持つ Entity の復元</b>:
    /// <c>P3_EntityGenerationPhase</c> は ChunkComponent セクションの
    /// <c>MemberEntityIndex</c> から pid を収集し、Entity を生成する。
    /// したがって「ChunkComponent のみを持つ Entity」（他コンポーネントを持たない）も
    /// ロード時に正しく復元される。
    /// </para>
    /// <para>
    /// <b>旧仕様</b>: 以前は Chunk のみ Entity は生成されず、値も復元されなかった。
    /// その挙動は <c>LoadWarningKind.ChunkComponentEntityMissing</c> 警告として可視化されていた。
    /// 現在は復元が実装され、警告は「復元失敗の回帰検出」として維持されている。
    /// </para>
    /// </summary>
    public abstract class SaveTypeModuleChunk<T> : SaveTypeModule, SaveTypeModuleChunkMarker
        where T : unmanaged, IComponentData
    {
        public override int RawRecordStride => UnsafeUtility.SizeOf<T>();
        public override int RecordStride => UnsafeUtility.SizeOf<T>() - 4 * EntityRefOffsets.Length;
        public override bool HasEnabledBits => false;
        public override SectionKind SectionKind => SectionKind.ChunkComponent;
        public override bool ContributesToIdOf => false;

        public override EntityQuery BuildQuery(EntityManager em)
            => ModuleQueryCache.GetOrCreate(em, this, () =>
                em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ChunkComponentReadOnly<T>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities,
                }));

        public override unsafe void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            buffer.InitChunkMemberStorage(Allocator.TempJob);
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var entityHandle = em.GetEntityTypeHandle();

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var entities = chunk.GetNativeArray(entityHandle);
                int count = entities.Length;
                if (count == 0) continue;

                T value = em.GetChunkComponentData<T>(entities[0]);
                byte* src = (byte*)&value;
                byte* members = (byte*)entities.GetUnsafeReadOnlyPtr();
                buffer.AppendChunk(src, (Entity*)members, count);
            }
        }

        public override unsafe void FilterAfterIdOf(
    ref SnapshotBuffer buffer,
    NativeParallelHashMap<Entity, int> idOf)
        {
            if (!buffer.HasChunkMembers) return;

            UnityEngine.Debug.Log(
                $"[ChunkFilter] BEGIN " +
                $"Groups={buffer.ChunkMemberOffsets.Length - 1} " +
                $"Members={buffer.ChunkMemberEntities.Length} " +
                $"Entities={buffer.Entities.Length} " +
                $"Values={buffer.Values.Length}");

            var newValues = new NativeList<byte>(buffer.Values.Length, Allocator.TempJob);
            var newEntities = new NativeList<Entity>(buffer.Entities.Length, Allocator.TempJob);
            var newMembers = new NativeList<Entity>(buffer.ChunkMemberEntities.Length, Allocator.TempJob);
            var newOffsets = new NativeList<int>(buffer.ChunkMemberOffsets.Length, Allocator.TempJob);

            newOffsets.Add(0);

            int stride = buffer.Stride;
            byte* vptr = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();

            for (int c = 0; c < buffer.ChunkMemberOffsets.Length - 1; c++)
            {
                int start = buffer.ChunkMemberOffsets[c];
                int end = buffer.ChunkMemberOffsets[c + 1];

                UnityEngine.Debug.Log(
                    $"[ChunkFilter] Group={c} Start={start} End={end}");

                int memberCount = 0;

                for (int i = start; i < end; i++)
                {
                    var entity = buffer.ChunkMemberEntities[i];

                    bool existsInIdOf = idOf.ContainsKey(entity);

                    UnityEngine.Debug.Log(
                        $"[ChunkFilter]   Member[{i}] " +
                        $"Entity={entity} " +
                        $"InIdOf={existsInIdOf}");

                    if (existsInIdOf)
                        memberCount++;
                }

                UnityEngine.Debug.Log(
                    $"[ChunkFilter] Group={c} MatchedMembers={memberCount}");

                if (memberCount == 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[ChunkFilter] DROPPED " +
                        $"Group={c} " +
                        $"Start={start} End={end} " +
                        $"Reason=NoMembersInIdOf");

                    continue;
                }

                byte* src = vptr + c * stride;

                for (int b = 0; b < stride; b++)
                    newValues.Add(src[b]);

                newEntities.Add(buffer.Entities[c]);

                for (int i = start; i < end; i++)
                {
                    var e = buffer.ChunkMemberEntities[i];

                    if (idOf.ContainsKey(e))
                    {
                        newMembers.Add(e);

                        UnityEngine.Debug.Log(
                            $"[ChunkFilter]   KEEP Entity={e}");
                    }
                }

                newOffsets.Add(newMembers.Length);
            }

            UnityEngine.Debug.Log(
                $"[ChunkFilter] END " +
                $"Groups={newOffsets.Length - 1} " +
                $"Members={newMembers.Length}");

            buffer.Values.Dispose();
            buffer.Entities.Dispose();
            buffer.ChunkMemberEntities.Dispose();
            buffer.ChunkMemberOffsets.Dispose();

            buffer.Values = newValues;
            buffer.Entities = newEntities;
            buffer.ChunkMemberEntities = newMembers;
            buffer.ChunkMemberOffsets = newOffsets;
        }

        public override int ComputeBodySize(in SnapshotBuffer buffer)
        {
            int groupCount = buffer.Entities.Length;
            int memberCount = buffer.ChunkMemberEntities.IsCreated ? buffer.ChunkMemberEntities.Length : 0;
            return ChunkComponentSectionWriter.ComputeFixedBodySize(groupCount, memberCount, RecordStride);
        }

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int groupCount = buffer.Entities.Length;
            int dataStride = RecordStride;
            int rawStride = RawRecordStride;
            var refs = EntityRefOffsets;

            using var memberOffset = new NativeArray<uint>(groupCount + 1, Allocator.TempJob);
            using var memberEntityIndex = new NativeArray<uint>(
                buffer.ChunkMemberEntities.Length, Allocator.TempJob);

            uint* mo = (uint*)memberOffset.GetUnsafePtr();
            mo[0] = 0;
            for (int i = 0; i < groupCount; i++)
                mo[i + 1] = (uint)buffer.ChunkMemberOffsets[i + 1];

            uint* mei = (uint*)memberEntityIndex.GetUnsafePtr();
            for (int i = 0; i < buffer.ChunkMemberEntities.Length; i++)
                mei[i] = (uint)idOf[buffer.ChunkMemberEntities[i]];

            ChunkComponentSectionWriter.WriteFixedHeader(
                ref writer, (uint)groupCount, (uint)dataStride, mo,
                buffer.ChunkMemberEntities.Length, mei);

            if (dataStride <= 0 || groupCount == 0) return;

            byte* records = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();

            if (refs.Length == 0)
            {
                writer.WriteBytes(records, groupCount * dataStride);
            }
            else
            {
                for (int g = 0; g < groupCount; g++)
                {
                    EntityRefCodec.WriteRecordWithRefs(
                        ref writer, records + g * rawStride, refs, rawStride, idOf);
                }
            }
        }

        /// <summary>
        /// ChunkComponent セクションの復元（<b>フェーズ 1/2</b>: 存在確保のみ）。
        ///
        /// <para>
        /// <b>命名について</b>: メソッド名は基底クラスから継承した <c>RestoreSection</c>。
        /// 基底クラスの他の実装（Component / Buffer / Shared / Identity）は値を書き込むが、
        /// ChunkComponent は <b>チャンクコンポーネントの存在確保のみ</b> を行う。
        /// 値の書き込みは <b>フェーズ 2</b> の <see cref="RestoreChunks"/> で行う。
        /// </para>
        /// <para>
        /// <b>2 フェーズ構成の理由</b>: ChunkComponent はチャンク（Archetype）単位で値を持ち、
        /// 書き込みにはメンバー Entity が 1 つ以上存在する必要がある。フェーズ 1 で全メンバーに
        /// ChunkComponent の存在を保証し、フェーズ 2 で代表メンバーを通じて値を書き込む。
        /// </para>
        /// <para>
        /// <b>Entity の生成</b>: メンバー Entity の生成は <c>P3_EntityGenerationPhase</c> が
        /// 責務を持つ。本メソッドは <c>persistentToEntity[pid]</c> で解決済みの Entity にのみ
        /// ChunkComponent を付与する。解決できない pid はスキップする。
        /// </para>
        /// </summary>
        public override unsafe void RestoreSection(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = ChunkComponentSectionReader.Read(sectionBody);
            int groupCount = (int)v.Header.GroupCount;
            if (groupCount == 0) return;

            for (int g = 0; g < groupCount; g++)
            {
                int start = (int)v.MemberOffset[g];
                int end = (int)v.MemberOffset[g + 1];
                if (start >= end) continue;

                uint pid = v.MemberEntityIndex[start];
                if (pid >= persistentToEntity.Length) continue;

                Entity target = persistentToEntity[(int)pid];
                if (target == Entity.Null) continue;
                if (!em.Exists(target)) continue;

                if (!em.HasChunkComponent<T>(target))
                    em.AddChunkComponentData<T>(target);
            }
        }

        public override void RemoveFromEntity(Entity entity, EntityManager em) { }

        public override unsafe ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator)
        {
            int n = updateTargets.Length;
            int stride = UnsafeUtility.SizeOf<T>();
            var values = new NativeArray<byte>(n * stride, allocator);
            var presence = new NativeArray<byte>(n, allocator);
            byte* vptr = (byte*)values.GetUnsafePtr();

            var updateSet = new NativeParallelHashSet<Entity>(n > 0 ? n : 16, Allocator.TempJob);
            var anchorList = new NativeList<Entity>(16, Allocator.TempJob);
            var anchorValueList = new NativeList<byte>(16 * stride, Allocator.TempJob);

            try
            {
                for (int i = 0; i < n; i++)
                    if (updateTargets[i] != Entity.Null) updateSet.Add(updateTargets[i]);

                for (int i = 0; i < n; i++)
                {
                    var e = updateTargets[i];
                    if (e == Entity.Null || !em.Exists(e) || !em.HasChunkComponent<T>(e))
                    {
                        presence[i] = 0;
                        continue;
                    }
                    presence[i] = 1;
                    T v = em.GetChunkComponentData<T>(e);
                    byte* src = (byte*)&v;
                    byte* dst = vptr + i * stride;
                    for (int b = 0; b < stride; b++) dst[b] = src[b];
                }

                using (var chunks = BuildQuery(em).ToArchetypeChunkArray(Allocator.TempJob))
                {
                    var entityHandle = em.GetEntityTypeHandle();
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        Entity anchor = Entity.Null;
                        for (int i = 0; i < entities.Length; i++)
                        {
                            if (!updateSet.Contains(entities[i])) { anchor = entities[i]; break; }
                        }
                        if (anchor == Entity.Null) continue;

                        anchorList.Add(anchor);
                        int start = anchorValueList.Length;
                        anchorValueList.ResizeUninitialized(start + stride);
                        T av = em.GetChunkComponentData<T>(anchor);
                        ModuleBytes.Write(av, (byte*)anchorValueList.GetUnsafePtr() + start);
                    }
                }

                return new ModuleBackup
                {
                    Module = this,
                    Values = values,
                    Presence = presence,
                    Enabled = default,
                    Count = n,
                    AnchorEntities = anchorList.ToArray(allocator),
                    AnchorValues = anchorValueList.ToArray(allocator),
                    AnchorCount = anchorList.Length,
                };
            }
            finally
            {
                if (updateSet.IsCreated) updateSet.Dispose();
                if (anchorList.IsCreated) anchorList.Dispose();
                if (anchorValueList.IsCreated) anchorValueList.Dispose();
            }
        }

        public override unsafe void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em)
        {
            int stride = UnsafeUtility.SizeOf<T>();
            byte* vptr = backup.Values.IsCreated
                ? (byte*)backup.Values.GetUnsafeReadOnlyPtr() : null;

            for (int i = 0; i < backup.Count; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e)) continue;

                if (backup.Presence[i] == 0)
                {
                    if (em.HasChunkComponent<T>(e)) em.RemoveChunkComponent<T>(e);
                    continue;
                }

                if (!em.HasChunkComponent<T>(e)) em.AddChunkComponentData<T>(e);
                var chunk = em.GetChunk(e);
                em.SetChunkComponentData<T>(chunk, ModuleBytes.Read<T>(vptr + i * stride, stride));
            }

            if (backup.AnchorCount == 0 || !backup.AnchorValues.IsCreated) return;
            byte* aptr = (byte*)backup.AnchorValues.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < backup.AnchorCount; i++)
            {
                var anchor = backup.AnchorEntities[i];
                if (anchor == Entity.Null || !em.Exists(anchor)) continue;
                if (!em.HasChunkComponent<T>(anchor)) continue;
                var chunk = em.GetChunk(anchor);
                em.SetChunkComponentData<T>(chunk, ModuleBytes.Read<T>(aptr + i * stride, stride));
            }
        }

        /// <summary>
        /// ChunkComponent セクションの復元（<b>フェーズ 2/2</b>: 値の書き込み）。
        ///
        /// <para>
        /// <b>フェーズ 1 との関係</b>: <see cref="RestoreSection"/> で全メンバーに
        /// ChunkComponent の存在を確保した後、本メソッドで代表メンバーを通じて値を書き込む。
        /// </para>
        /// <para>
        /// <b>P1 追加引数</b>: <paramref name="missingEntityCount"/> と
        /// <paramref name="firstMissingPid"/> は、ChunkComponent セクションに現れた pid のうち
        /// <c>persistentToEntity[pid] == Entity.Null</c> だったものの件数と先頭 pid。
        /// 呼び出し側（<see cref="P6_ChunkComponentRestorePhase"/>）が
        /// <see cref="LoadWarningKind.ChunkComponentEntityMissing"/> 警告を発火するために使う。
        /// </para>
        /// <para>
        /// <b>通常は 0 件</b>: <c>P3_EntityGenerationPhase</c> が全メンバー pid の Entity を
        /// 生成するため、通常ケースでは <c>missingEntityCount == 0</c>。
        /// 0 件なら警告は出さない。
        /// </para>
        /// </summary>
        public unsafe SaveLoadErrorCode RestoreChunks(
            in ChunkComponentSectionView view,
            int groupCount,
            int stride,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            ref bool merged,
            out int missingEntityCount,
            out int firstMissingPid)
        {
            missingEntityCount = 0;
            firstMissingPid = -1;

            int memberCount = (int)view.MemberOffset[groupCount];
            var entityToPid = new NativeParallelHashMap<Entity, int>(
                memberCount > 0 ? memberCount : 16, Allocator.TempJob);

            try
            {
                for (int i = 0; i < persistentToEntity.Length; i++)
                {
                    var e = persistentToEntity[i];
                    if (e != Entity.Null) entityToPid.TryAdd(e, i);
                }

                var entityIndexToGroup = new NativeArray<int>(
                    persistentToEntity.Length, Allocator.TempJob);

                try
                {
                    for (int i = 0; i < entityIndexToGroup.Length; i++)
                        entityIndexToGroup[i] = -1;

                    for (int g = 0; g < groupCount; g++)
                    {
                        int start = (int)view.MemberOffset[g];
                        int end = (int)view.MemberOffset[g + 1];
                        for (int k = start; k < end; k++)
                        {
                            uint pid = view.MemberEntityIndex[k];
                            if (pid >= (uint)persistentToEntity.Length) continue;

                            if (persistentToEntity[(int)pid] == Entity.Null)
                            {
                                if (firstMissingPid < 0) firstMissingPid = (int)pid;
                                missingEntityCount++;
                                continue;
                            }

                            if (pid < (uint)entityIndexToGroup.Length)
                                entityIndexToGroup[(int)pid] = g;
                        }
                    }

                    return ApplyChunks(view, groupCount, stride,
                        entityIndexToGroup, entityToPid, persistentToEntity, em, ref merged);
                }
                finally { if (entityIndexToGroup.IsCreated) entityIndexToGroup.Dispose(); }
            }
            finally { if (entityToPid.IsCreated) entityToPid.Dispose(); }
        }

        private unsafe SaveLoadErrorCode ApplyChunks(
            in ChunkComponentSectionView view,
            int groupCount,
            int stride,
            NativeArray<int> entityIndexToGroup,
            NativeParallelHashMap<Entity, int> entityToPid,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            ref bool merged)
        {
            var query = BuildQuery(em);
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var entityHandle = em.GetEntityTypeHandle();

            int maxCapacity = 128;
            for (int c = 0; c < chunks.Length; c++)
            {
                int cap = chunks[c].Capacity;
                if (cap > maxCapacity) maxCapacity = cap;
            }

            var scratchGroups = new NativeArray<int>(maxCapacity, Allocator.TempJob);
            var refs = EntityRefOffsets;
            int rawStride = RawRecordStride;
            byte[] expanded = refs.Length > 0 ? new byte[rawStride] : null;

            try
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    var entities = chunk.GetNativeArray(entityHandle);
                    int count = entities.Length;
                    if (count == 0) continue;

                    int groupTotal = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var e = entities[i];
                        if (!entityToPid.TryGetValue(e, out int pid)) continue;
                        int g = entityIndexToGroup[pid];
                        if (g < 0) continue;
                        scratchGroups[groupTotal++] = g;
                    }
                    if (groupTotal == 0) continue;

                    int representative = SelectRepresentative(scratchGroups, groupTotal);
                    if (representative < 0 || representative * stride + stride > view.RecordsLength)
                        continue;

                    byte* src = view.Records + representative * stride;

                    if (refs.Length == 0)
                    {
                        em.SetChunkComponentData<T>(chunk, ModuleBytes.Read<T>(src, rawStride));
                    }
                    else
                    {
                        fixed (byte* eptr = expanded)
                        {
                            EntityRefCodec.ExpandRecord(
                                src, stride, refs, rawStride, persistentToEntity, eptr);
                            em.SetChunkComponentData<T>(chunk, ModuleBytes.Read<T>(eptr, rawStride));
                        }
                    }

                    int distinctGroups = CountDistinct(scratchGroups, groupTotal);
                    bool hasNonMember = groupTotal < count;
                    if (distinctGroups >= 2 || (distinctGroups == 1 && hasNonMember))
                        merged = true;
                }
                return SaveLoadErrorCode.None;
            }
            finally { if (scratchGroups.IsCreated) scratchGroups.Dispose(); }
        }

        private static int CountDistinct(NativeArray<int> groups, int count)
        {
            if (count <= 1) return count;
            int distinct = 0;
            for (int i = 0; i < count; i++)
            {
                bool seen = false;
                for (int j = 0; j < i; j++)
                    if (groups[j] == groups[i]) { seen = true; break; }
                if (!seen) distinct++;
            }
            return distinct;
        }

        private static int SelectRepresentative(NativeArray<int> groups, int count)
        {
            groups.GetSubArray(0, count).Sort();
            int best = groups[0];
            int bestCount = 0;
            int runStart = 0;

            for (int i = 1; i <= count; i++)
            {
                if (i < count && groups[i] == groups[runStart]) continue;
                int runLength = i - runStart;
                if (runLength > bestCount) { best = groups[runStart]; bestCount = runLength; }
                runStart = i;
            }
            return best;
        }
    }

    /// <summary>
    /// <see cref="SaveTypeModuleChunk{T}"/> の型消去用マーカー。
    /// <see cref="P6_ChunkComponentRestorePhase"/> から呼び出せるよう、
    /// 非ジェネリックで <c>RestoreChunks</c> を公開する。
    /// </summary>
    internal interface SaveTypeModuleChunkMarker
    {
        SaveLoadErrorCode RestoreChunks(
            in ChunkComponentSectionView view,
            int groupCount,
            int stride,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            ref bool merged,
            out int missingEntityCount,
            out int firstMissingPid);
    }
}
