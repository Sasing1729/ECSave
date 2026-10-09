using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SaveSpike
{
    public enum SpikeResult
    {
        Success, CorruptedData, ChecksumMismatch, VersionMismatch,
        WorldMismatch, ValidationFailed,
    }

    public enum SpikeDeletionPolicy : byte { None = 0, ByRegistration = 1, ByScope = 2 }

    public struct DeserializeTimings
    {
        public double DecodeMs, IdentityMapMs, PlanMs, DestroyMs,
                      CreateMs, AddComponentsMs, WriteValuesMs, TotalMs;
    }

    public static unsafe class SpikeDeserializer
    {
        public static SpikeResult Apply(
            EntityManager em, byte[] data, SpikeDeletionPolicy policy,
            out DeserializeTimings t, out int warningCount, out int finalCount)
        {
            t = default; warningCount = 0; finalCount = 0;
            var totalSw = Stopwatch.StartNew();

            if (data == null || data.Length < SpikeLayout.HeaderSize + SpikeLayout.BodyPrefix)
                return SpikeResult.CorruptedData;

            int count, invTotal;
            fixed (byte* p = data)
            {
                byte* body = p + SpikeLayout.HeaderSize;
                int bodyLen = data.Length - SpikeLayout.HeaderSize;

                if ((uint)Le.ReadInt(p + 0) != SpikeLayout.FormatVersion ||
                    (uint)Le.ReadInt(p + 4) != SpikeLayout.GameDataVersion)
                    return SpikeResult.VersionMismatch;
                if (Le.ReadULong(p + 8) != SpikeLayout.SchemaHash)
                    return SpikeResult.VersionMismatch;
                if (Le.ReadULong(p + 16) != SpikeLayout.WorldIdHi ||
                    Le.ReadULong(p + 24) != SpikeLayout.WorldIdLo)
                    return SpikeResult.WorldMismatch;

                var h = xxHash3.Hash64(body, bodyLen);
                if ((((ulong)h.y << 32) | h.x) != Le.ReadULong(p + 32))
                    return SpikeResult.ChecksumMismatch;

                count = Le.ReadInt(body);
                invTotal = Le.ReadInt(body + 4);
                if (count < 0 || invTotal < 0) return SpikeResult.CorruptedData;
                int need = SpikeLayout.HeaderSize + SpikeLayout.BodyPrefix
                    + count * SpikeLayout.RecordSize + invTotal * SpikeLayout.InvElemSize;
                if (data.Length < need) return SpikeResult.CorruptedData;
            }

            var sw = Stopwatch.StartNew();
            var cols = SpikeColumns.Allocate(count, math.max(1, invTotal), false);
            fixed (byte* p = data)
            {
                byte* records = p + SpikeLayout.HeaderSize + SpikeLayout.BodyPrefix;
                new DecodeFixedJob
                {
                    Src = records,
                    Flags = cols.Flags,
                    Ids = cols.Ids,
                    Health = cols.Health,
                    TargetIdx = cols.TargetIdx,
                    InvOffset = cols.InvOffset,
                    Pos = cols.Pos,
                }.Schedule(count, 256).Complete();

                new DecodeInvJob
                {
                    Src = records + (long)count * SpikeLayout.RecordSize,
                    Elems = cols.InvElems,
                }.Schedule(invTotal, 1024).Complete();

                new PrefixJob { Count = count, InvOffset = cols.InvOffset }.Schedule().Complete();
            }
            t.DecodeMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            // 3. Identity map + duplicate detection
            var idMap = new NativeParallelHashMap<int, Entity>(math.max(1, count), Allocator.TempJob);
            bool dupFound = false;
            {
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<SpikeId>());
                using var ents = q.ToEntityArray(Allocator.TempJob);
                using var ids = q.ToComponentDataArray<SpikeId>(Allocator.TempJob);
                for (int i = 0; i < ids.Length; i++)
                    if (!idMap.TryAdd(ids[i].Value, ents[i])) { dupFound = true; break; }
            }
            t.IdentityMapMs = sw.Elapsed.TotalMilliseconds;

            if (dupFound)
            {
                idMap.Dispose(); cols.Dispose();
                return SpikeResult.ValidationFailed;
            }

            // 4. Plan
            sw.Restart();
            var idxToEntity = new NativeArray<Entity>(count, Allocator.TempJob);
            var createMask = new NativeArray<byte>(count, Allocator.TempJob);
            new BuildPlanJob
            {
                Flags = cols.Flags,
                Ids = cols.Ids,
                IdMap = idMap,
                ForceCreate = policy == SpikeDeletionPolicy.ByScope ? (byte)1 : (byte)0,
                OutEntity = idxToEntity,
                OutCreate = createMask,
            }.Schedule(count, 256).Complete();
            t.PlanMs = sw.Elapsed.TotalMilliseconds;

            // 5. B1 Destroy
            sw.Restart();
            if (policy != SpikeDeletionPolicy.None)
            {
                var keep = new NativeParallelHashSet<Entity>(math.max(1, count), Allocator.TempJob);
                for (int i = 0; i < count; i++)
                    if (createMask[i] == 0) keep.Add(idxToEntity[i]);

                using var q = em.CreateEntityQuery(new EntityQueryDesc
                {
                    Any = new ComponentType[]
                    {
                        ComponentType.ReadOnly<SpikeId>(),
                        ComponentType.ReadOnly<SpikeHealth>(),
                        ComponentType.ReadOnly<SpikeTarget>(),
                        ComponentType.ReadOnly<SpikeStunned>(),
                        ComponentType.ReadOnly<SpikeInventory>(),
                        ComponentType.ReadOnly<SpikePosition>(),
                    }
                });

                var toDestroy = new NativeList<Entity>(Allocator.TempJob);
                new CollectToDestroyJob
                {
                    EntityH = em.GetEntityTypeHandle(),
                    Keep = keep,
                    PolicyByte = (byte)policy,
                    ToDestroy = toDestroy.AsParallelWriter(),   // 修正
                }.Schedule(q, default).Complete();

                if (toDestroy.Length > 0)
                {
                    using var ecb = new EntityCommandBuffer(Allocator.TempJob);
                    for (int i = 0; i < toDestroy.Length; i++) ecb.DestroyEntity(toDestroy[i]);
                    ecb.Playback(em);
                }
                toDestroy.Dispose();
                keep.Dispose();
            }
            t.DestroyMs = sw.Elapsed.TotalMilliseconds;

            // 6. B2 Create
            sw.Restart();
            {
                var buckets = new System.Collections.Generic.Dictionary<byte, System.Collections.Generic.List<int>>();
                for (int i = 0; i < count; i++)
                {
                    if (createMask[i] == 0) continue;
                    byte f = cols.Flags[i];
                    if (!buckets.TryGetValue(f, out var list))
                        buckets[f] = list = new System.Collections.Generic.List<int>();
                    list.Add(i);
                }

                foreach (var kv in buckets)
                {
                    var arch = BuildArchetype(em, kv.Key);
                    var created = em.CreateEntity(arch, kv.Value.Count, Allocator.TempJob);
                    for (int k = 0; k < kv.Value.Count; k++)
                        idxToEntity[kv.Value[k]] = created[k];
                    created.Dispose();
                }
            }
            t.CreateMs = sw.Elapsed.TotalMilliseconds;

            // 7. B2.5 Add missing
            sw.Restart();
            for (int i = 0; i < count; i++)
            {
                if (createMask[i] != 0) continue;
                AddMissing(em, idxToEntity[i], cols.Flags[i]);
            }
            t.AddComponentsMs = sw.Elapsed.TotalMilliseconds;

            // 8. B3 Write values
            sw.Restart();
            for (int i = 0; i < count; i++)
            {
                var e = idxToEntity[i];
                var f = cols.Flags[i];

                if ((f & SpikeFlags.HasId) != 0 && em.HasComponent<SpikeId>(e))
                    em.SetComponentData(e, new SpikeId { Value = cols.Ids[i] });
                if ((f & SpikeFlags.HasHealth) != 0 && em.HasComponent<SpikeHealth>(e))
                    em.SetComponentData(e, new SpikeHealth { Current = cols.Health[i].x, Max = cols.Health[i].y });
                if ((f & SpikeFlags.HasStunned) != 0 && em.HasComponent<SpikeStunned>(e))
                    em.SetComponentEnabled<SpikeStunned>(e, (f & SpikeFlags.StunnedEnabled) != 0);
                if ((f & SpikeFlags.HasPosition) != 0 && em.HasComponent<SpikePosition>(e))
                    em.SetComponentData(e, new SpikePosition { Value = cols.Pos[i] });
            }

            // Entity参照は全Entity生成後に解決
            for (int i = 0; i < count; i++)
            {
                if ((cols.Flags[i] & SpikeFlags.HasTarget) == 0) continue;
                var e = idxToEntity[i];
                if (!em.HasComponent<SpikeTarget>(e)) continue;
                int ti = cols.TargetIdx[i];
                Entity target = (ti >= 0 && ti < count) ? idxToEntity[ti] : Entity.Null;
                em.SetComponentData(e, new SpikeTarget { Value = target });
            }
            t.WriteValuesMs = sw.Elapsed.TotalMilliseconds;

            idxToEntity.Dispose();
            createMask.Dispose();
            idMap.Dispose();
            cols.Dispose();

            t.TotalMs = totalSw.Elapsed.TotalMilliseconds;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<SpikeId>()))
                finalCount = q.CalculateEntityCount();
            return SpikeResult.Success;
        }

        static EntityArchetype BuildArchetype(EntityManager em, byte flags)
        {
            using var types = new NativeList<ComponentType>(Allocator.Temp);
            types.Add(ComponentType.ReadWrite<SpikeId>());
            if ((flags & SpikeFlags.HasHealth) != 0) types.Add(ComponentType.ReadWrite<SpikeHealth>());
            if ((flags & SpikeFlags.HasTarget) != 0) types.Add(ComponentType.ReadWrite<SpikeTarget>());
            if ((flags & SpikeFlags.HasStunned) != 0) types.Add(ComponentType.ReadWrite<SpikeStunned>());
            if ((flags & SpikeFlags.HasInventory) != 0) types.Add(ComponentType.ReadWrite<SpikeInventory>());
            if ((flags & SpikeFlags.HasPosition) != 0) types.Add(ComponentType.ReadWrite<SpikePosition>());
            if ((flags & SpikeFlags.IsPlayer) != 0) types.Add(ComponentType.ReadWrite<SpikePlayer>());
            var arch = em.CreateArchetype(types.AsArray());
            types.Dispose();
            return arch;
        }

        static void AddMissing(EntityManager em, Entity e, byte flags)
        {
            if ((flags & SpikeFlags.HasHealth) != 0 && !em.HasComponent<SpikeHealth>(e)) em.AddComponent<SpikeHealth>(e);
            if ((flags & SpikeFlags.HasTarget) != 0 && !em.HasComponent<SpikeTarget>(e)) em.AddComponent<SpikeTarget>(e);
            if ((flags & SpikeFlags.HasStunned) != 0 && !em.HasComponent<SpikeStunned>(e)) em.AddComponent<SpikeStunned>(e);
            if ((flags & SpikeFlags.HasInventory) != 0 && !em.HasBuffer<SpikeInventory>(e)) em.AddBuffer<SpikeInventory>(e);
            if ((flags & SpikeFlags.HasPosition) != 0 && !em.HasComponent<SpikePosition>(e)) em.AddComponent<SpikePosition>(e);
            if ((flags & SpikeFlags.IsPlayer) != 0 && !em.HasComponent<SpikePlayer>(e)) em.AddComponent<SpikePlayer>(e);
        }
    }

    // ============================================================
    // Jobs (WriteValuesJob / ApplyTargetsJob は削除)
    // ============================================================

    [BurstCompile]
    unsafe struct DecodeFixedJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public byte* Src;
        [WriteOnly] public NativeArray<byte> Flags;
        [WriteOnly] public NativeArray<int> Ids;
        [WriteOnly] public NativeArray<int2> Health;
        [WriteOnly] public NativeArray<int> TargetIdx;
        [WriteOnly] public NativeArray<int> InvOffset;
        [WriteOnly] public NativeArray<float3> Pos;

        public void Execute(int i)
        {
            byte* p = Src + (long)i * SpikeLayout.RecordSize;
            Flags[i] = p[0];
            Ids[i] = Le.ReadInt(p + 1);
            Health[i] = new int2(Le.ReadInt(p + 5), Le.ReadInt(p + 9));
            TargetIdx[i] = Le.ReadInt(p + 13);
            InvOffset[i] = Le.ReadInt(p + 17);
            Pos[i] = new float3(Le.ReadFloat(p + 21), Le.ReadFloat(p + 25), Le.ReadFloat(p + 29));
        }
    }

    [BurstCompile]
    unsafe struct DecodeInvJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public byte* Src;
        [WriteOnly] public NativeArray<int2> Elems;

        public void Execute(int i)
        {
            byte* p = Src + (long)i * SpikeLayout.InvElemSize;
            Elems[i] = new int2(Le.ReadInt(p), Le.ReadInt(p + 4));
        }
    }

    [BurstCompile]
    struct BuildPlanJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Flags;
        [ReadOnly] public NativeArray<int> Ids;
        [ReadOnly] public NativeParallelHashMap<int, Entity> IdMap;
        public byte ForceCreate;
        [WriteOnly] public NativeArray<Entity> OutEntity;
        [WriteOnly] public NativeArray<byte> OutCreate;

        public void Execute(int i)
        {
            Entity e = Entity.Null;
            byte create = 1;
            if (ForceCreate == 0
                && (Flags[i] & SpikeFlags.HasId) != 0
                && IdMap.TryGetValue(Ids[i], out var found))
            {
                e = found; create = 0;
            }
            OutEntity[i] = e;
            OutCreate[i] = create;
        }
    }

    [BurstCompile]
    struct CollectToDestroyJob : IJobChunk
    {
        [ReadOnly] public EntityTypeHandle EntityH;
        [ReadOnly] public NativeParallelHashSet<Entity> Keep;
        public byte PolicyByte;
        [WriteOnly] public NativeList<Entity>.ParallelWriter ToDestroy;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var ents = chunk.GetNativeArray(EntityH);
            bool byScope = PolicyByte == 2;
            for (int i = 0; i < chunk.Count; i++)
                if (byScope || !Keep.Contains(ents[i]))
                    ToDestroy.AddNoResize(ents[i]);
        }
    }
}
