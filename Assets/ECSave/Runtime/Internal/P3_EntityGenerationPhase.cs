using ECSave.Format;
using Unity.Collections;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
    /// <summary>
    /// P3: Entity 生成フェーズ。
    ///
    /// <para>
    /// 全セクションを事前スキャンして一意な pid を収集し、一括で空 Entity を生成する。
    /// 生成した Entity は <see cref="PrephaseContext.PersistentToEntity"/> に格納され、
    /// <see cref="ApplyJournal"/> に「生成された」ことが記録される。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>スキャン対象</b>:
    /// <list type="bullet">
    ///   <item>Identity セクション（<c>CommonSectionReader</c> 経由）</item>
    ///   <item>Component / Buffer / Shared セクション（同）</item>
    ///   <item>
    ///     <b>ChunkComponent セクション</b>（<c>ChunkComponentSectionReader</c> 経由）。
    ///     Chunk のみを持つ Entity も復元するため、<c>MemberEntityIndex</c> の pid を収集する。
    ///     通常ケース（Chunk + 他コンポーネント）では、既に他セクションで pid が
    ///     収集済みのため <c>seen</c> と <c>PersistentToEntity[pid] != Entity.Null</c>
    ///     により重複なくスキップされる。
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>重要な不変条件</b>: <c>PersistentToEntity[pid]</c> に既に非 Null が
    /// 入っている pid（<see cref="P1_PlanPhase"/> で識別マッチした既存 Entity）は
    /// <b>スキップ</b>する。これを怠ると、マッチした既存 Entity への参照が
    /// 新規生成 Entity で上書きされ、識別照合の結果が失われる。
    /// </para>
    /// </remarks>
    internal static unsafe class P3_EntityGenerationPhase
    {
        /// <summary>
        /// 全セクションを走査して pid を収集し、一括で空 Entity を生成する。
        /// 不正な pid を検出した場合は <see cref="FormatException"/> を投げる
        /// （呼び出し元 <c>DeserializeOperation.DoApply</c> が捕捉する）。
        /// </summary>
        public static void Execute(
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
                    bool isChunk = m != null && m.SectionKind == SectionKind.ChunkComponent;

                    if (!isNonChunk && !isIdentity && !isChunk) continue;

                    ByteView bodyView;
                    try
                    {
                        bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                    }
                    catch (FormatException)
                    {
                        continue;
                    }

                    if (isChunk)
                        CollectChunkPids(ref ctx, bodyView, pidsToCreate, seen);
                    else
                        CollectCommonPids(ref ctx, bodyView, pidsToCreate, seen);
                }
                UnityEngine.Debug.Log(
                    $"[P3] pidsToCreate.Length={pidsToCreate.Length}");

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

        /// <summary>
        /// CommonSection から pid を収集する。Identity / Component / Buffer / Shared 共通。
        /// </summary>
        private static void CollectCommonPids(
            ref PrephaseContext ctx,
            ByteView bodyView,
            NativeList<uint> pidsToCreate,
            NativeParallelHashSet<uint> seen)
        {
            var v = CommonSectionReader.Read(bodyView);
            int count = (int)v.Header.Count;

            for (int k = 0; k < count; k++)
            {
                uint pid = v.EntityIndex[k];
                if (pid >= (uint)ctx.PersistentToEntity.Length)
                    throw new FormatException(
                        FormatError.ValidationFailed,
                        "persistent id out of range");

                if (ctx.PersistentToEntity[(int)pid] != Entity.Null) continue;

                if (seen.Add(pid))
                    pidsToCreate.Add(pid);
            }
        }

        /// <summary>
        /// ChunkComponentSection から pid を収集する。
        ///
        /// <para>
        /// <b>ChunkComponent-only Entity の復元</b>: 保存時に ChunkComponent セクションの
        /// <c>MemberEntityIndex</c> に記録された pid は、保存対象として確定した Entity の
        /// pid である。通常ケース（Chunk + 他コンポーネント）では他セクションでも
        /// 同じ pid が現れ、先に収集される。<c>Chunk のみ</c> のエッジケースでは
        /// ここでのみ pid が検出され、Entity が新規生成される。
        /// </para>
        /// <para>
        /// <b>ChunkComponent の値の復元</b>は別フェーズ（<see cref="P6_ChunkComponentRestorePhase"/>）で
        /// 行われる。ここでは「Entity を生成する」だけで、値の書き込みは行わない。
        /// </para>
        /// </summary>
        private static void CollectChunkPids(
            ref PrephaseContext ctx,
            ByteView bodyView,
            NativeList<uint> pidsToCreate,
            NativeParallelHashSet<uint> seen)
        {
            var v = ChunkComponentSectionReader.Read(bodyView);
            int groupCount = (int)v.Header.GroupCount;
            if (groupCount <= 0) return;

            int memberCount = (int)v.MemberOffset[groupCount];
            if (memberCount <= 0) return;

            for (int k = 0; k < memberCount; k++)
            {
                uint pid = v.MemberEntityIndex[k];
                if (pid >= (uint)ctx.PersistentToEntity.Length)
                    throw new FormatException(
                        FormatError.ValidationFailed,
                        "persistent id out of range");

                if (ctx.PersistentToEntity[(int)pid] != Entity.Null) continue;

                if (seen.Add(pid))
                    pidsToCreate.Add(pid);
            }
        }
    }
}
