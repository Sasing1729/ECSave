using System;
using NUnit.Framework;
using ECSave;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class LoadWarningTests
    {
        [Test]
        public void ToString_AllKinds_DoesNotThrow()
        {
            foreach (LoadWarningKind kind in Enum.GetValues(typeof(LoadWarningKind)))
            {
                var w = new LoadWarning(kind, Entity.Null, "Test.Type", 0, 0x1234UL);
                Assert.DoesNotThrow(() => w.ToString(), $"Kind={kind}");
                Assert.IsFalse(string.IsNullOrEmpty(w.ToString()), $"Kind={kind}");
            }
        }

        [Test]
        public void ToString_EntityReferenceUnresolved_IncludesTypeAndIndex()
        {
            var w = new LoadWarning(
                LoadWarningKind.EntityReferenceUnresolved,
                Entity.Null, "Game.Domain.Health", saveEntityIndex: 42);
            var s = w.ToString();

            StringAssert.Contains("Game.Domain.Health", s);
            StringAssert.Contains("42", s);
        }

        [Test]
        public void ToString_ByScopeDeletionWarning_IncludesCount()
        {
            var w = new LoadWarning(
                LoadWarningKind.ByScopeDeletionWarning,
                Entity.Null, null, saveEntityIndex: -1, extraHash: 12345);
            var s = w.ToString();

            StringAssert.Contains("12345", s);
        }

        [Test]
        public void ToString_PrefabSchemaNotRegistered_IncludesGuid()
        {
            var w = new LoadWarning(
                LoadWarningKind.PrefabSchemaNotRegistered,
                Entity.Null, null, saveEntityIndex: -1,
                extraHash: 0xDEADBEEFCAFEBABEUL);
            var s = w.ToString();

            StringAssert.Contains("DEADBEEFCAFEBABE", s);
        }

        [Test]
        public void ToString_ChunkComponentEntityMissing_IncludesCountAndPid()
        {
            var w = new LoadWarning(
                LoadWarningKind.ChunkComponentEntityMissing,
                Entity.Null, "ChunkData", saveEntityIndex: 7, extraHash: 3);
            var s = w.ToString();

            StringAssert.Contains("3", s);
            StringAssert.Contains("7", s);
            StringAssert.Contains("ChunkData", s);
        }

        [Test]
        public void ToString_NullTypeName_DoesNotThrow()
        {
            var w = new LoadWarning(
                LoadWarningKind.EntityReferenceUnresolved,
                Entity.Null, null, saveEntityIndex: 0);
            Assert.DoesNotThrow(() => w.ToString());
        }
    }
}
