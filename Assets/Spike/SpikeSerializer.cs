using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

#if SPIKE_S3_BINARYPRIMITIVES
using System;
using System.Buffers.Binary;
#endif

namespace SaveSpike
{
    public enum SpikeWriterKind
    {
        /// <summary>手書きのLE書き込み（ポインタ）。Burst対応を想定。</summary>
        Manual,
        /// <summary>BinaryPrimitives + Span。S3の検証対象。#define SPIKE_S3_BINARYPRIMITIVES が必要。</summary>
        BinaryPrimitives,
    }

    public struct SerializeTimings
    {
        public double ResolveMs;
        public double WriteMs;
        public double ChecksumMs;
        public double ToArrayMs;
        /// <summary>0 = Burstで実行された / 1 = managedで実行された（Burst失敗または無効）</summary>
        public int RanManaged;
    }

    /// <summary>
    /// スナップショット（コピー済みデータ）を byte[] にする。World には触れない（内部設計書 6.3節）。
    /// </summary>
    public static unsafe class SpikeSerializer
    {
        public static byte[] Serialize(in SpikeColumns s, out SerializeTimings t, SpikeWriterKind kind = SpikeWriterKind.Manual)
        {
            t = default;
            var sw = Stopwatch.StartNew();
            int count = s.Count;

            // ---- Entity参照 → ファイル内インデックス ----
            var map = new NativeParallelHashMap<Entity, int>(math.max(1, count), Allocator.TempJob);
            new BuildMapJob { Entities = s.Entities, Map = map.AsParallelWriter() }.Schedule(count, 256).Complete();
            new ResolveTargetsJob
            {
                Map = map,
                Flags = s.Flags,
                TargetEntity = s.TargetEntity,
                TargetIdx = s.TargetIdx,
            }.Schedule(count, 256).Complete();
            map.Dispose();
            t.ResolveMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            // ---- 書き込み ----
            int invTotal = s.InvOffset[count];
            int bodyLen = SpikeLayout.BodyPrefix + count * SpikeLayout.RecordSize + invTotal * SpikeLayout.InvElemSize;
            int total = SpikeLayout.HeaderSize + bodyLen;

            var buf = new NativeArray<byte>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            byte* basePtr = (byte*)buf.GetUnsafePtr();
            byte* body = basePtr + SpikeLayout.HeaderSize;
            Le.WriteInt(body, count);
            Le.WriteInt(body + 4, invTotal);

            var probe = new NativeArray<int>(1, Allocator.TempJob);
            JobHandle jf;
            if (kind == SpikeWriterKind.BinaryPrimitives)
            {
#if SPIKE_S3_BINARYPRIMITIVES
                jf = new WriteFixedBinaryPrimitivesJob
                {
                    Flags = s.Flags, Ids = s.Ids, Health = s.Health, TargetIdx = s.TargetIdx,
                    InvOffset = s.InvOffset, Pos = s.Pos, Dst = body + SpikeLayout.BodyPrefix, Probe = probe,
                }.Schedule(count, 256);
#else
                UnityEngine.Debug.LogWarning("SPIKE_S3_BINARYPRIMITIVES が未定義のため Manual にフォールバックします");
                jf = ScheduleManual(s, body, probe, count);
#endif
            }
            else
            {
                jf = ScheduleManual(s, body, probe, count);
            }

            var ji = new WriteInvJob
            {
                Elems = s.InvElems,
                Dst = body + SpikeLayout.BodyPrefix + (long)count * SpikeLayout.RecordSize,
            }.Schedule(invTotal, 1024, jf);
            ji.Complete();
            t.RanManaged = probe[0];
            probe.Dispose();
            t.WriteMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            // ---- チェックサム（Body全体）+ Header ----
            var h = xxHash3.Hash64(body, bodyLen);
            ulong checksum = ((ulong)h.y << 32) | h.x;
            Le.WriteInt(basePtr + 0, (int)SpikeLayout.FormatVersion);
            Le.WriteInt(basePtr + 4, (int)SpikeLayout.GameDataVersion);
            Le.WriteULong(basePtr + 8, SpikeLayout.SchemaHash);
            Le.WriteULong(basePtr + 16, SpikeLayout.WorldIdHi);
            Le.WriteULong(basePtr + 24, SpikeLayout.WorldIdLo);
            Le.WriteULong(basePtr + 32, checksum);
            t.ChecksumMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            var arr = buf.ToArray();
            buf.Dispose();
            t.ToArrayMs = sw.Elapsed.TotalMilliseconds;
            return arr;
        }

        static JobHandle ScheduleManual(in SpikeColumns s, byte* body, NativeArray<int> probe, int count)
        {
            return new WriteFixedManualJob
            {
                Flags = s.Flags,
                Ids = s.Ids,
                Health = s.Health,
                TargetIdx = s.TargetIdx,
                InvOffset = s.InvOffset,
                Pos = s.Pos,
                Dst = body + SpikeLayout.BodyPrefix,
                Probe = probe,
            }.Schedule(count, 256);
        }
    }

    // ---- Entity → ファイル内インデックス ----
    [BurstCompile]
    struct BuildMapJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        public NativeParallelHashMap<Entity, int>.ParallelWriter Map;
        public void Execute(int i) { Map.TryAdd(Entities[i], i); }
    }

    [BurstCompile]
    struct ResolveTargetsJob : IJobParallelFor
    {
        [ReadOnly] public NativeParallelHashMap<Entity, int> Map;
        [ReadOnly] public NativeArray<byte> Flags;
        [ReadOnly] public NativeArray<Entity> TargetEntity;
        public NativeArray<int> TargetIdx;

        public void Execute(int i)
        {
            // -1 = Entity.Null（Warningなし）／-2 = 保存対象外への参照（ロード時に Entity.Null + Warning）
            int idx = -1;
            if ((Flags[i] & SpikeFlags.HasTarget) != 0)
            {
                var te = TargetEntity[i];
                if (te == Entity.Null) idx = -1;
                else if (Map.TryGetValue(te, out var found)) idx = found;
                else idx = -2;
            }
            TargetIdx[i] = idx;
        }
    }

    // ---- S3: フィールド単位Writer（手書きLE） ----
    [BurstCompile]
    unsafe struct WriteFixedManualJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Flags;
        [ReadOnly] public NativeArray<int> Ids;
        [ReadOnly] public NativeArray<int2> Health;
        [ReadOnly] public NativeArray<int> TargetIdx;
        [ReadOnly] public NativeArray<int> InvOffset;
        [ReadOnly] public NativeArray<float3> Pos;
        [NativeDisableUnsafePtrRestriction] public byte* Dst;
        [NativeDisableParallelForRestriction] public NativeArray<int> Probe;

        public void Execute(int i)
        {
            byte* p = Dst + (long)i * SpikeLayout.RecordSize;
            p[0] = Flags[i];
            Le.WriteInt(p + 1, Ids[i]);
            var h = Health[i];
            Le.WriteInt(p + 5, h.x);
            Le.WriteInt(p + 9, h.y);
            Le.WriteInt(p + 13, TargetIdx[i]);
            Le.WriteInt(p + 17, InvOffset[i + 1] - InvOffset[i]);
            var pos = Pos[i];
            Le.WriteFloat(p + 21, pos.x);
            Le.WriteFloat(p + 25, pos.y);
            Le.WriteFloat(p + 29, pos.z);

            if (i == 0)
            {
                bool managed = false;
                BurstProbe.MarkManaged(ref managed);   // Burstなら呼ばれない
                Probe[0] = managed ? 1 : 0;
            }
        }
    }

    [BurstCompile]
    unsafe struct WriteInvJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> Elems;
        [NativeDisableUnsafePtrRestriction] public byte* Dst;

        public void Execute(int i)
        {
            byte* p = Dst + (long)i * SpikeLayout.InvElemSize;
            var e = Elems[i];
            Le.WriteInt(p, e.x);
            Le.WriteInt(p + 4, e.y);
        }
    }

#if SPIKE_S3_BINARYPRIMITIVES
    // ---- S3 の検証対象：BinaryPrimitives + Span が Burst でコンパイルできるか ----
    // 失敗するとプレイヤービルドが Burst エラーで止まるため、既定では #define しない。
    [BurstCompile(CompileSynchronously = true)]
    unsafe struct WriteFixedBinaryPrimitivesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Flags;
        [ReadOnly] public NativeArray<int> Ids;
        [ReadOnly] public NativeArray<int2> Health;
        [ReadOnly] public NativeArray<int> TargetIdx;
        [ReadOnly] public NativeArray<int> InvOffset;
        [ReadOnly] public NativeArray<float3> Pos;
        [NativeDisableUnsafePtrRestriction] public byte* Dst;
        [NativeDisableParallelForRestriction] public NativeArray<int> Probe;

        public void Execute(int i)
        {
            byte* p = Dst + (long)i * SpikeLayout.RecordSize;
            var span = new Span<byte>(p, SpikeLayout.RecordSize);
            span[0] = Flags[i];
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(1), Ids[i]);
            var h = Health[i];
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5), h.x);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9), h.y);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(13), TargetIdx[i]);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(17), InvOffset[i + 1] - InvOffset[i]);
            var pos = Pos[i];
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(21), math.asint(pos.x));
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(25), math.asint(pos.y));
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(29), math.asint(pos.z));

            if (i == 0)
            {
                bool managed = false;
                BurstProbe.MarkManaged(ref managed);
                Probe[0] = managed ? 1 : 0;
            }
        }
    }
#endif
}
