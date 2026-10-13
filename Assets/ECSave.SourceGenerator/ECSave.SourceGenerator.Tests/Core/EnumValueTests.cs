using ECSave.SourceGenerator.Core;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Core
{
    public class EnumValueTests
    {
        [Test]
        public void SchemaHashKind_FixedValues()
        {
            Assert.AreEqual((byte)0, (byte)SchemaHashKind.None);
            Assert.AreEqual((byte)1, (byte)SchemaHashKind.Bool);
            Assert.AreEqual((byte)11, (byte)SchemaHashKind.Float64);
            Assert.AreEqual((byte)12, (byte)SchemaHashKind.Entity);
            Assert.AreEqual((byte)13, (byte)SchemaHashKind.FixedString);
            Assert.AreEqual((byte)14, (byte)SchemaHashKind.FixedList);
            Assert.AreEqual((byte)15, (byte)SchemaHashKind.Raw);
            Assert.AreEqual((byte)16, (byte)SchemaHashKind.Struct);
        }

        [Test]
        public void FieldKind_DistinctValues()
        {
            var values = new[]
            {
                FieldKind.Unknown, FieldKind.Primitive, FieldKind.Entity,
                FieldKind.FixedString, FieldKind.FixedList, FieldKind.Nested, FieldKind.Raw,
            };
            for (int i = 0; i < values.Length; i++)
                for (int j = i + 1; j < values.Length; j++)
                    Assert.AreNotEqual(values[i], values[j]);
        }
    }
}
