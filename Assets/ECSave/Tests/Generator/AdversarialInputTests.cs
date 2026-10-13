using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public class AdversarialInputTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static GenTestScope CreateScope()
            => ScriptableObject.CreateInstance<GenTestScope>();

        private LoadResult Run(byte[] data)
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                return sys.DeserializeAsync(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }
        }

        [UnityTest]
        public IEnumerator EmptyArray_ReturnsCorruptedData()
        {
            var result = Run(new byte[0]);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RandomGarbage_ReturnsCorruptedData()
        {
            var data = new byte[256];
            var rng = new System.Random(42);
            rng.NextBytes(data);

            var result = Run(data);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BadMagic_ReturnsCorruptedData()
        {
            var scope = CreateScope();
            byte[] original;
            try
            {
                var sys = scope.GetSaveSystem();
                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);
                original = save.Data;
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            // Magic を破壊
            original[0] = 0xFF;
            original[1] = 0xFF;

            var result = Run(original);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewerFormatVersion_ReturnsVersionMismatch()
        {
            var scope = CreateScope();
            byte[] original;
            try
            {
                var sys = scope.GetSaveSystem();
                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);
                original = save.Data;
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            // FormatVersion (offset 0x04, u16 LE) を 0xFFFF に
            original[0x04] = 0xFF;
            original[0x05] = 0xFF;

            // チェックサム再計算
            RecomputeChecksum(original);

            var result = Run(original);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.VersionMismatch, result.ErrorCode);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TruncatedData_ReturnsCorruptedData()
        {
            var scope = CreateScope();
            byte[] original;
            try
            {
                var sys = scope.GetSaveSystem();
                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);
                original = save.Data;
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            var truncated = new byte[original.Length / 2];
            System.Array.Copy(original, truncated, truncated.Length);

            var result = Run(truncated);
            Assert.IsFalse(result.IsSuccess);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ModifiedChecksum_ReturnsChecksumMismatch()
        {
            var scope = CreateScope();
            byte[] original;
            try
            {
                var sys = scope.GetSaveSystem();
                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);
                original = save.Data;
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            // 途中のバイトを1つ反転（チェックサムは再計算しない）
            int mid = original.Length / 2;
            original[mid] ^= 0x01;

            var result = Run(original);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ChecksumMismatch, result.ErrorCode);
            yield return null;
        }

        private static unsafe void RecomputeChecksum(byte[] data)
        {
            fixed (byte* p = data)
            {
                int checksumEnd = data.Length - ECSave.Format.ECSaveFormatConstants.FooterSize;
                ulong ck = ChecksumCalculator.Compute(
                    new ECSave.Format.ByteView(p, checksumEnd));
                *(ulong*)(p + checksumEnd) = ck;
            }
        }
    }
}
