using System;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class PrefabIdentityLayoutTests
    {
        [Test]
        public void Constants_HaveExpectedValues()
        {
            Assert.That(PrefabIdentityLayout.HeaderSize, Is.EqualTo(8));
            Assert.That(PrefabIdentityLayout.RecordSize, Is.EqualTo(24));
            Assert.That(PrefabIdentityLayout.EntityIndexOffset, Is.EqualTo(0));
            Assert.That(PrefabIdentityLayout.PrefabGuidOffset, Is.EqualTo(8));
            Assert.That(PrefabIdentityLayout.SchemaHashOffset, Is.EqualTo(16));
        }

        [TestCase(0, 8)]
        [TestCase(1, 32)]
        [TestCase(3, 80)]
        [TestCase(10, 248)]
        public void ComputeBodySize_ReturnsExpected(int count, int expected)
        {
            Assert.That(PrefabIdentityLayout.ComputeBodySize(count), Is.EqualTo(expected));
        }
    }

    [TestFixture]
    public unsafe class PrefabIdentitySectionTests
    {
        private static PrefabIdentityRecord[] MakeRecords(int count)
        {
            var records = new PrefabIdentityRecord[count];
            for (int i = 0; i < count; i++)
            {
                records[i] = new PrefabIdentityRecord(
                    (uint)(i + 1),
                    (ulong)(0x1000 + i),
                    (ulong)(0x2000 + i));
            }
            return records;
        }

        [Test]
        public void WriteRead_Empty_Roundtrip()
        {
            var buffer = new NativeArray<byte>(32, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 32);
                PrefabIdentitySection.Write(ref w, Array.Empty<PrefabIdentityRecord>());

                var v = PrefabIdentitySection.Read(p, 8);
                Assert.That(v.Count, Is.EqualTo(0u));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_Null_RoundtripAsEmpty()
        {
            var buffer = new NativeArray<byte>(32, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 32);
                PrefabIdentitySection.Write(ref w, null);

                var v = PrefabIdentitySection.Read(p, 8);
                Assert.That(v.Count, Is.EqualTo(0u));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_SingleRecord_AllFieldsPreserved()
        {
            var records = new[]
            {
                new PrefabIdentityRecord(0xCAFE, 0xDEADBEEFUL, 0x12345678UL),
            };

            int size = PrefabIdentityLayout.ComputeBodySize(1);
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 64);
                PrefabIdentitySection.Write(ref w, records);

                var v = PrefabIdentitySection.Read(p, size);
                Assert.That(v.Count, Is.EqualTo(1u));
                Assert.That(PrefabIdentitySection.GetEntityIndex(v, 0), Is.EqualTo(0xCAFEu));
                Assert.That(PrefabIdentitySection.GetPrefabGuid(v, 0), Is.EqualTo(0xDEADBEEFUL));
                Assert.That(PrefabIdentitySection.GetSchemaHash(v, 0), Is.EqualTo(0x12345678UL));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_MultipleRecords_AllPreserved()
        {
            var records = MakeRecords(5);
            int size = PrefabIdentityLayout.ComputeBodySize(5);

            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 256);
                PrefabIdentitySection.Write(ref w, records);

                var v = PrefabIdentitySection.Read(p, size);
                Assert.That(v.Count, Is.EqualTo(5u));

                for (int i = 0; i < 5; i++)
                {
                    Assert.That(PrefabIdentitySection.GetEntityIndex(v, i),
                        Is.EqualTo(records[i].EntityIndex));
                    Assert.That(PrefabIdentitySection.GetPrefabGuid(v, i),
                        Is.EqualTo(records[i].PrefabGuid));
                    Assert.That(PrefabIdentitySection.GetSchemaHash(v, i),
                        Is.EqualTo(records[i].SchemaHash));
                }
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_NullPointer_Throws()
        {
            Assert.Throws<FormatException>(() => PrefabIdentitySection.Read(null, 100));
        }

        [Test]
        public void Read_TooShort_Throws()
        {
            var buffer = new NativeArray<byte>(4, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                Assert.Throws<FormatException>(() => PrefabIdentitySection.Read(p, 4));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_TruncatedBody_Throws()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 256);
                PrefabIdentitySection.Write(ref w, MakeRecords(3));

                // Declares 3 records (8 + 3*24 = 80 bytes) but only 32 supplied
                Assert.Throws<FormatException>(() => PrefabIdentitySection.Read(p, 32));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_CountOverflow_Throws()
        {
            var buffer = new NativeArray<byte>(8, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                *(uint*)p = 1000000;
                Assert.Throws<FormatException>(() => PrefabIdentitySection.Read(p, 8));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_CountTampered_Throws()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 256);
                PrefabIdentitySection.Write(ref w, MakeRecords(2));

                // Tamper count from 2 → 100 (body only holds 2 records)
                *(uint*)p = 100u;

                Assert.Throws<FormatException>(() => PrefabIdentitySection.Read(p, 8 + 2 * 24));
            }
            finally { buffer.Dispose(); }
        }
    }
}
