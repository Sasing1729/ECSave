// Tests/Runtime.Tests/Unit/WarningConversionTests.cs
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using ECSave.Internal;

namespace ECSave.Tests.Runtime
{
    public class WarningConversionTests
    {
        [Test]
        public void WarningRecord_ToLoadWarning()
        {
            var collector = new WarningCollector(Allocator.TempJob);
            try
            {
                collector.Add(WarningRecord.Create(
                    LoadWarningKind.EntityReferenceUnresolved,
                    typeSectionId: 5,
                    saveEntityIndex: 3,
                    sourceEntity: new Entity { Index = 1, Version = 1 }));

                var map = new Dictionary<uint, string> { { 5, "Game.Domain.Health" } };
                var list = new List<LoadWarning>();
                collector.ToLoadWarnings(list, map);

                Assert.AreEqual(1, list.Count);
                Assert.AreEqual(LoadWarningKind.EntityReferenceUnresolved, list[0].Kind);
                Assert.AreEqual("Game.Domain.Health", list[0].TypeName);
                Assert.AreEqual(3, list[0].SaveEntityIndex);
            }
            finally
            {
                collector.Dispose();
            }
        }

        [Test]
        public void ChunkComponentMerged_Fields()
        {
            var collector = new WarningCollector(Allocator.TempJob);
            try
            {
                collector.Add(WarningRecord.Create(
                    LoadWarningKind.ChunkComponentMerged,
                    typeSectionId: 10,
                    saveEntityIndex: -1,
                    sourceEntity: Entity.Null));

                var map = new Dictionary<uint, string> { { 10, "Game.Domain.ChunkData" } };
                var list = new List<LoadWarning>();
                collector.ToLoadWarnings(list, map);

                Assert.AreEqual(1, list.Count);
                Assert.AreEqual(LoadWarningKind.ChunkComponentMerged, list[0].Kind);
                Assert.AreEqual(Entity.Null, list[0].SourceEntity);
                Assert.AreEqual(-1, list[0].SaveEntityIndex);
            }
            finally
            {
                collector.Dispose();
            }
        }
    }
}
