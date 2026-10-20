using System;
using NUnit.Framework;
using ECSave;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class SceneGuidTests
    {
        [Test]
        public void ParseHex_ValidString_ReturnsCorrectValues()
        {
            var g = SceneGuid.ParseHex("00000000000000010000000000000002");
            Assert.That(g.Hi, Is.EqualTo(1UL));
            Assert.That(g.Lo, Is.EqualTo(2UL));
        }

        [Test]
        public void ParseHex_UppercaseHex_Accepted()
        {
            var g = SceneGuid.ParseHex("FFFFFFFFFFFFFFFF0000000000000000");
            Assert.That(g.Hi, Is.EqualTo(ulong.MaxValue));
            Assert.That(g.Lo, Is.EqualTo(0UL));
        }

        [Test]
        public void ParseHex_LowercaseHex_Accepted()
        {
            var g = SceneGuid.ParseHex("ffffffffffffffff0000000000000000");
            Assert.That(g.Hi, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void ParseHex_MixedCase_Accepted()
        {
            var g = SceneGuid.ParseHex("aBcDeF0123456789fedcba9876543210");
            Assert.That(g.Hi, Is.Not.EqualTo(0UL));
            Assert.That(g.Lo, Is.Not.EqualTo(0UL));
        }

        [TestCase(null)]
        [TestCase("")]
        public void ParseHex_NullOrEmpty_Throws(string input)
        {
            Assert.Throws<FormatException>(() => SceneGuid.ParseHex(input));
        }

        [TestCase("0000000000000000000000000000000")]  // 31 chars
        [TestCase("000000000000000000000000000000000")] // 33 chars
        public void ParseHex_WrongLength_Throws(string input)
        {
            Assert.Throws<FormatException>(() => SceneGuid.ParseHex(input));
        }

        [Test]
        public void ParseHex_InvalidChar_Throws()
        {
            Assert.Throws<FormatException>(
                () => SceneGuid.ParseHex("000000000000000G0000000000000000"));
        }

        [Test]
        public void FromString_Deterministic_SameInput()
        {
            var a = SceneGuid.FromString("hello");
            var b = SceneGuid.FromString("hello");
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void FromString_DifferentInputs_Differ()
        {
            var a = SceneGuid.FromString("hello");
            var b = SceneGuid.FromString("world");
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [TestCase(null)]
        [TestCase("")]
        public void FromString_NullOrEmpty_ReturnsDefault(string input)
        {
            var g = SceneGuid.FromString(input);
            Assert.That(g, Is.EqualTo(default(SceneGuid)));
        }

        [Test]
        public void Equality_OperatorsWork()
        {
            var a = new SceneGuid(1, 2);
            var b = new SceneGuid(1, 2);
            var c = new SceneGuid(1, 3);

            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a == c, Is.False);
        }

        [Test]
        public void GetHashCode_EqualInstances_SameHash()
        {
            var a = new SceneGuid(1, 2);
            var b = new SceneGuid(1, 2);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void ToString_FormatsHex()
        {
            var g = new SceneGuid(0xABCDEF0123456789UL, 0x0FEDCBA987654321UL);
            Assert.That(g.ToString(), Is.EqualTo("ABCDEF01234567890FEDCBA987654321"));
        }

        [Test]
        public void ParseHex_RoundTrip()
        {
            var original = new SceneGuid(0x1234567890ABCDEFUL, 0x0FEDCBA098765432UL);
            var parsed = SceneGuid.ParseHex(original.ToString());
            Assert.That(parsed, Is.EqualTo(original));
        }
    }

    [TestFixture]
    public sealed class SubSceneEnumsTests
    {
        [Test]
        public void ContentVerificationMode_Values()
        {
            Assert.That((byte)ContentVerificationMode.None, Is.EqualTo(0));
            Assert.That((byte)ContentVerificationMode.GuidOnly, Is.EqualTo(1));
            Assert.That((byte)ContentVerificationMode.GuidAndContentHash, Is.EqualTo(2));
        }

        [Test]
        public void StreamingConsistencyMode_Values()
        {
            Assert.That((byte)StreamingConsistencyMode.TimeBased, Is.EqualTo(0));
            Assert.That((byte)StreamingConsistencyMode.FrameBoundary, Is.EqualTo(1));
        }

        [Test]
        public void SceneLoadState_Values()
        {
            Assert.That((byte)SceneLoadState.Unloaded, Is.EqualTo(0));
            Assert.That((byte)SceneLoadState.Loaded, Is.EqualTo(1));
            Assert.That((byte)SceneLoadState.Loading, Is.EqualTo(2));
        }

        [Test]
        public void SceneRestoreMode_Values()
        {
            Assert.That((byte)SceneRestoreMode.Required, Is.EqualTo(0));
            Assert.That((byte)SceneRestoreMode.Optional, Is.EqualTo(1));
        }
    }

    [TestFixture]
    public sealed class SceneLoadInfoTests
    {
        [Test]
        public void Constructor_StoresAllValues()
        {
            var guid = new SceneGuid(1, 2);
            var info = new SceneLoadInfo(guid, SceneLoadState.Loaded, SceneRestoreMode.Optional);
            Assert.That(info.Guid, Is.EqualTo(guid));
            Assert.That(info.State, Is.EqualTo(SceneLoadState.Loaded));
            Assert.That(info.RestoreMode, Is.EqualTo(SceneRestoreMode.Optional));
        }

        [Test]
        public void Constructor_DefaultRestoreMode_IsRequired()
        {
            var info = new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded);
            Assert.That(info.RestoreMode, Is.EqualTo(SceneRestoreMode.Required));
        }
    }

    [TestFixture]
    public sealed class SceneLoadRequestTests
    {
        [Test]
        public void Constructor_StoresAllValues()
        {
            var guid = new SceneGuid(10, 20);
            var req = new SceneLoadRequest(guid, SceneLoadState.Loaded, SceneRestoreMode.Optional);
            Assert.That(req.Guid, Is.EqualTo(guid));
            Assert.That(req.TargetState, Is.EqualTo(SceneLoadState.Loaded));
            Assert.That(req.Mode, Is.EqualTo(SceneRestoreMode.Optional));
        }
    }

    [TestFixture]
    public sealed class SceneLoadOutcomeTests
    {
        [Test]
        public void Ok_ReturnsSuccess()
        {
            var o = SceneLoadOutcome.Ok();
            Assert.That(o.Success, Is.True);
            Assert.That(o.FailureReason, Is.Null);
        }

        [Test]
        public void Fail_ReturnsFailureWithReason()
        {
            var o = SceneLoadOutcome.Fail("missing");
            Assert.That(o.Success, Is.False);
            Assert.That(o.FailureReason, Is.EqualTo("missing"));
        }

        [Test]
        public void Fail_WithNullReason_StillFails()
        {
            var o = SceneLoadOutcome.Fail(null);
            Assert.That(o.Success, Is.False);
            Assert.That(o.FailureReason, Is.Null);
        }
    }

    [TestFixture]
    public sealed class SaveLoadErrorCodeSubSceneTests
    {
        [Test]
        public void SceneLoadFailed_HasCorrectValue()
        {
            Assert.That((int)SaveLoadErrorCode.SceneLoadFailed, Is.EqualTo(17));
        }

        [Test]
        public void SceneContentMismatch_HasCorrectValue()
        {
            Assert.That((int)SaveLoadErrorCode.SceneContentMismatch, Is.EqualTo(18));
        }

        [Test]
        public void PrefabMigrationFailed_HasCorrectValue()
        {
            Assert.That((int)SaveLoadErrorCode.PrefabMigrationFailed, Is.EqualTo(19));
        }

        [Test]
        public void LegacyValues_Unchanged()
        {
            Assert.That((int)SaveLoadErrorCode.None, Is.EqualTo(0));
            Assert.That((int)SaveLoadErrorCode.SerializationFailed, Is.EqualTo(16));
        }
    }

    [TestFixture]
    public sealed class LoadWarningKindSubSceneTests
    {
        [TestCase(LoadWarningKind.SceneLoadStateMismatch, 3)]
        [TestCase(LoadWarningKind.SceneRestoreFailed, 4)]
        [TestCase(LoadWarningKind.SceneContentHashMismatch, 5)]
        [TestCase(LoadWarningKind.PrefabMigrated, 6)]
        [TestCase(LoadWarningKind.SceneStateRolledBack, 7)]
        public void NewKinds_HaveCorrectValue(LoadWarningKind kind, int expected)
        {
            Assert.That((int)kind, Is.EqualTo(expected));
        }

        [Test]
        public void LegacyValues_Unchanged()
        {
            Assert.That((byte)LoadWarningKind.EntityReferenceUnresolved, Is.EqualTo(0));
            Assert.That((byte)LoadWarningKind.UnknownSectionIgnored, Is.EqualTo(1));
            Assert.That((byte)LoadWarningKind.ChunkComponentMerged, Is.EqualTo(2));
        }
    }
}
