using ECSave.Tests;
using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Debug = UnityEngine.Debug;

namespace ECSave.Tests
{
    /// <summary>
    /// A-01: xxHash3（Unity.Collections）が使えること。
    /// 確認: (1) 公開 API の存在とシグネチャ (2) 既知ベクタ (3) 長さ別の決定性 (4) 非アラインでも同値
    ///       (5) Burst ジョブ内で managed と同値 (6) 速度（§8.2 の 6 の参考値）
    /// </summary>
    public unsafe class A01_XxHash3Tests
    {
        // XXH3_64bits（seed 0）の空入力の既知値
        const ulong EmptyKnown = 0x2D06800538D394C2UL;

        static ulong ToU64Lo(uint2 h) => ((ulong)h.y << 32) | h.x; // x を下位とみなす
        static ulong ToU64Hi(uint2 h) => ((ulong)h.x << 32) | h.y; // x を上位とみなす

        [Test]
        public void EmptyInput_MatchesKnownVector_AndReportsWordOrder()
        {
            byte dummy = 0;
            uint2 h = xxHash3.Hash64(&dummy, 0);
            ulong lo = ToU64Lo(h), hi = ToU64Hi(h);
            Debug.Log($"[A-01] empty: x=0x{h.x:X8} y=0x{h.y:X8} lo-order=0x{lo:X16} hi-order=0x{hi:X16}");
            Assert.IsTrue(lo == EmptyKnown || hi == EmptyKnown,
                "既知ベクタと一致しない。xxHash3 の変種・シードが想定と異なる可能性（内部設計 6.6節、A-01）");
            // Format 層のフッタ 8B の並びを決めるための記録
            Debug.Log(lo == EmptyKnown ? "[A-01] uint2.x = 下位32bit, y = 上位32bit" : "[A-01] uint2.x = 上位32bit, y = 下位32bit");
        }

        [Test]
        public void AllLengthClasses_AreDeterministic_AndDistinct()
        {
            // XXH3 は 0 / 1-3 / 4-8 / 9-16 / 17-128 / 129-240 / 241+ で内部経路が変わる
            var seen = new HashSet<ulong>();
            var buf = new byte[1024];
            for (int i = 0; i < buf.Length; i++) buf[i] = (byte)(i * 31 + 7);
            for (int len = 0; len <= 600; len++)
            {
                ulong a, b;
                fixed (byte* p = buf)
                {
                    a = ToU64Lo(xxHash3.Hash64(p, len));
                    b = ToU64Lo(xxHash3.Hash64(p, len));
                }
                Assert.AreEqual(a, b, $"len={len} で非決定的");
                Assert.IsTrue(seen.Add(a), $"len={len} でハッシュが衝突");
            }
        }

        [Test]
        public void UnalignedInput_GivesSameHash()
        {
            var data = new byte[300];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 13 + 5);
            var raw = new NativeArray<byte>(data.Length + 16, Allocator.Temp, NativeArrayOptions.ClearMemory);
            try
            {
                var basePtr = (byte*)raw.GetUnsafePtr();
                ulong reference = 0;
                for (int offset = 0; offset < 16; offset++)
                {
                    for (int i = 0; i < data.Length; i++) basePtr[offset + i] = data[i];
                    ulong h = ToU64Lo(xxHash3.Hash64(basePtr + offset, data.Length));
                    if (offset == 0) reference = h;
                    Assert.AreEqual(reference, h, $"offset={offset} で値が変わる");
                }
            }
            finally { raw.Dispose(); }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct HashJob : IJob
        {
            [ReadOnly] public NativeArray<byte> Input;
            public NativeArray<ulong> Result;
            public NativeArray<int> WasManaged;

            public void Execute()
            {
                Result[0] = ToU64Lo(xxHash3.Hash64(Input.GetUnsafeReadOnlyPtr(), Input.Length));
                WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
            }
        }

        [Test]
        public void BurstJob_MatchesManagedResult()
        {
            var input = new NativeArray<byte>(100_000, Allocator.TempJob);
            var result = new NativeArray<ulong>(1, Allocator.TempJob);
            var managed = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                for (int i = 0; i < input.Length; i++) input[i] = (byte)(i ^ (i >> 8));
                ulong expected = ToU64Lo(xxHash3.Hash64(input.GetUnsafeReadOnlyPtr(), input.Length));
                new HashJob { Input = input, Result = result, WasManaged = managed }.Run();
                Debug.Log($"[A-01] burst job executed as managed={managed[0] == 1} (BurstCompiler.IsEnabled={BurstCompiler.IsEnabled})");
                Assert.AreEqual(expected, result[0]);
                if (managed[0] == 1) Assert.Inconclusive("Burst で実行されていない（Burst を有効にして再実行）。Burst 内の xxHash3 は未検証");
            }
            finally { input.Dispose(); result.Dispose(); managed.Dispose(); }
        }

        [Test]
        public void Speed_16MiB_LogOnly()
        {
            const int size = 16 * 1024 * 1024;
            var input = new NativeArray<byte>(size, Allocator.TempJob);
            var result = new NativeArray<ulong>(1, Allocator.TempJob);
            var managed = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                for (int i = 0; i < size; i += 4096) input[i] = (byte)i;
                var job = new HashJob { Input = input, Result = result, WasManaged = managed };
                job.Run(); // ウォームアップ
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 5; i++) job.Run();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / 5;
                Debug.Log($"[A-01] xxHash3 16MiB: {ms:F2} ms/回 ({size / 1048576.0 / (ms / 1000.0):F0} MiB/s)");
                Assert.Pass();
            }
            finally { input.Dispose(); result.Dispose(); managed.Dispose(); }
        }
    }
}
