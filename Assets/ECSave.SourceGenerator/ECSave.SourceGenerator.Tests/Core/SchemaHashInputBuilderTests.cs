using ECSave.SourceGenerator.Core;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Core
{
    public class SchemaHashInputBuilderTests
    {
        [Test]
        public void EmptyPlan_EmitsTwoByteHeader()
        {
            var plan = new TypePlan("A", 0, new FieldPlan[0], new int[0], 0);
            var input = SchemaHashInputBuilder.Build(plan);

            Assert.AreEqual(2, input.Length);
            Assert.AreEqual(0, input[0]);
            Assert.AreEqual(0, input[1]);
        }

        [Test]
        public void SingleInt32_EmitsCorrectHeaderAndEntry()
        {
            var fields = new[] { new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) };
            var plan = new TypePlan("A", 4, fields, new int[0], 4);
            var input = SchemaHashInputBuilder.Build(plan);

            Assert.AreEqual(2 + 16, input.Length);
            Assert.AreEqual(4, input[0]);
            Assert.AreEqual(0, input[1]);
            Assert.AreEqual((byte)SchemaHashKind.Int32, input[2]);
            Assert.AreEqual(4, input[3]);
            Assert.AreEqual(0, input[4]);
            Assert.AreEqual(0, input[5]);
            Assert.AreEqual(0, input[6]);
        }

        [Test]
        public void StructSize_UsesLittleEndian()
        {
            var plan = new TypePlan("A", 0x1234, new FieldPlan[0], new int[0], 0);
            var input = SchemaHashInputBuilder.Build(plan);

            Assert.AreEqual(0x34, input[0]);
            Assert.AreEqual(0x12, input[1]);
        }

        [Test]
        public void MultipleFields_ConcatenatedInOrder()
        {
            var fields = new[]
            {
                new FieldPlan("A", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0),
                new FieldPlan("B", FieldKind.Primitive, SchemaHashKind.Int64, 8, 4),
            };
            var plan = new TypePlan("T", 12, fields, new int[0], 12);
            var input = SchemaHashInputBuilder.Build(plan);

            Assert.AreEqual(2 + 16 * 2, input.Length);
            Assert.AreEqual((byte)SchemaHashKind.Int32, input[2]);
            Assert.AreEqual((byte)SchemaHashKind.Int64, input[2 + 16]);
        }

        [Test]
        public void ComputeHash_MatchesDirectHash()
        {
            var fields = new[] { new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) };
            var plan = new TypePlan("A", 4, fields, new int[0], 4);

            var direct = SchemaHashCore.Hash(SchemaHashInputBuilder.Build(plan));
            var computed = SchemaHashInputBuilder.ComputeHash(plan);
            Assert.AreEqual(direct, computed);
        }

        [Test]
        public void NullPlan_ReturnsEmptyArray()
        {
            var input = SchemaHashInputBuilder.Build(null);
            Assert.IsNotNull(input);
            Assert.AreEqual(0, input.Length);
        }
    }
}
