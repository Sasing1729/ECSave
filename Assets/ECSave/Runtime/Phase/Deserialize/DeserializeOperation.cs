using System;
using System.Collections.Generic;
using System.Threading;
using ECSave.Format;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
    internal sealed unsafe class DeserializeOperation
    {
        private readonly SaveDefinition _def;
        private readonly World _world;
        private readonly byte[] _data;
        private readonly RestorationScope _scope;
        private readonly DeletionPolicy _policy;

        public DeserializeOperation(
            SaveDefinition def,
            World world,
            byte[] data,
            RestorationScope scope,
            DeletionPolicy policy)
        {
            _def = def;
            _world = world;
            _data = data;
            _scope = scope;
            _policy = policy;
        }

        public LoadResult Execute(CancellationToken ct)
        {
            var ctx = PrephaseContext.Create(_def, _world);
            try
            {
                var err = D0_InputCopyPhase.Execute(ref ctx, _data, _def, _world, ct);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D1_StructureValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D2_ChecksumVerificationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D3_VersionCheckPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D4_WorldIdCheckPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D5_SchemaMatchPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D6_MigrationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D7_EntityRefValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D8_RecordValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = P1_PlanPhase.Execute(ref ctx, _policy);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                var destroyTargets = new NativeList<Entity>(16, Allocator.TempJob);
                try
                {
                    err = P1_DeletionPlanPhase.Execute(
                        ref ctx, _scope, _policy, destroyTargets);
                    if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                    return ApplyPhase(ref ctx, destroyTargets, ct);
                }
                finally
                {
                    if (destroyTargets.IsCreated) destroyTargets.Dispose();
                }
            }
            finally
            {
                ctx.Dispose();
            }
        }

        private LoadResult ApplyPhase(
            ref PrephaseContext ctx,
            NativeList<Entity> destroyTargets,
            CancellationToken ct)
        {
            var em = _world.EntityManager;
            var journal = new ApplyJournal(Allocator.Persistent);
            var updateTargets = new NativeList<Entity>(16, Allocator.TempJob);

            try
            {
                for (int i = 0; i < ctx.PersistentToEntity.Length; i++)
                {
                    var e = ctx.PersistentToEntity[i];
                    if (e != Entity.Null) updateTargets.Add(e);
                }

                var updateArray = new NativeArray<Entity>(
                    updateTargets.Length, Allocator.TempJob);
                for (int i = 0; i < updateTargets.Length; i++)
                    updateArray[i] = updateTargets[i];

                try
                {
                    journal.Backup(ctx.Definition, updateArray, em);

                    bool success = false;
                    try
                    {
                        if (FaultInjector.ShouldFail("P3"))
                            throw new InvalidOperationException("Injected P3");

                        for (int i = 0; i < ctx.SectionTable.Count; i++)
                        {
                            var m = ctx.ModuleBySection[i];
                            if (m == null) continue;

                            var bodyView = GetSectionBody(ref ctx, i);
                            CreateEntitiesForSection(
                                bodyView, ctx.PersistentToEntity, journal, em);
                        }

                        for (int i = 0; i < ctx.SectionTable.Count; i++)
                        {
                            ref var entry = ref ctx.SectionTable.Entries[i];
                            if (entry.Kind != SectionKind.Identity) continue;

                            var bodyView = GetSectionBody(ref ctx, i);
                            CreateEntitiesForSection(
                                bodyView, ctx.PersistentToEntity, journal, em);
                        }

                        if (ct.IsCancellationRequested)
                            throw new InvalidOperationException("Cancelled");

                        if (FaultInjector.ShouldFail("P4"))
                            throw new InvalidOperationException("Injected P4");

                        for (int i = 0; i < ctx.SectionTable.Count; i++)
                        {
                            var m = ctx.ModuleBySection[i];
                            if (m == null) continue;

                            var bodyView = GetSectionBody(ref ctx, i);
                            m.ApplySectionBody(bodyView, ctx.PersistentToEntity, em);
                        }

                        for (int i = 0; i < ctx.SectionTable.Count; i++)
                        {
                            ref var entry = ref ctx.SectionTable.Entries[i];
                            if (entry.Kind != SectionKind.Identity) continue;

                            var im = FindIdentity(ctx.Definition, entry.NameHash);
                            if (im == null) continue;

                            var bodyView = GetSectionBody(ref ctx, i);
                            im.ApplySectionBody(bodyView, ctx.PersistentToEntity, em);
                        }

                        if (P4_StructuralChangePhase.Execute(ref ctx, _policy, em)
                            != SaveLoadErrorCode.None)
                            throw new InvalidOperationException("P4 removal failed");

                        if (FaultInjector.ShouldFail("P5"))
                            throw new InvalidOperationException("Injected P5");

                        if (FaultInjector.ShouldFail("P7"))
                            throw new InvalidOperationException("Injected P7");

                        if (destroyTargets.IsCreated && destroyTargets.Length > 0)
                            em.DestroyEntity(destroyTargets.AsArray());

                        success = true;
                    }
                    finally
                    {
                        if (!success)
                        {
                            if (!journal.Rollback(updateArray, em))
                                throw new InvalidOperationException("Rollback failed");
                        }
                    }

                    var warnings = new List<LoadWarning>();
                    ctx.Warnings.ToLoadWarnings(warnings, BuildTypeNameMap(ref ctx));

                    return LoadResult.Success(ctx.Header.LogicalEntityCount, warnings);
                }
                finally
                {
                    if (updateArray.IsCreated) updateArray.Dispose();
                }
            }
            catch (FormatException fe)
            {
                return LoadResult.Failure(ErrorInfoMapper.Map(fe.Error));
            }
            catch
            {
                return LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed);
            }
            finally
            {
                journal.Dispose();
                if (updateTargets.IsCreated) updateTargets.Dispose();
            }
        }

        private static ByteView GetSectionBody(ref PrephaseContext ctx, int sectionIndex)
        {
            ref var entry = ref ctx.SectionTable.Entries[sectionIndex];
            var sectionView = new ByteView(
                ctx.View.Pointer + (int)entry.Offset, (int)entry.Length);

            int tdSize = (int)(*(uint*)sectionView.Pointer);
            return sectionView.Slice(tdSize, (int)entry.Length - tdSize);
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
                    throw new FormatException(FormatError.ValidationFailed,
                        "persistent id out of range");

                if (persistentToEntity[(int)pid] == Entity.Null)
                {
                    var e = em.CreateEntity();
                    persistentToEntity[(int)pid] = e;
                    journal.NoteCreated(e);
                }
            }
        }

        private static IdentityModule FindIdentity(SaveDefinition def, ulong nameHash)
        {
            if (def.Identities == null) return null;
            for (int i = 0; i < def.Identities.Count; i++)
            {
                var m = def.Identities[i];
                if (SchemaHashCore.HashUtf8(m.FullTypeName) == nameHash)
                    return m;
            }
            return null;
        }

        private static Dictionary<uint, string> BuildTypeNameMap(ref PrephaseContext ctx)
        {
            var map = new Dictionary<uint, string>();
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m != null) map[(uint)i] = m.FullTypeName;
            }
            return map;
        }
    }
}
