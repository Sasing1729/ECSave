using ECSave.SourceGenerator.Core;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Core
{
    public class SchemaHasherTests
    {
        [Test]
        public void Hash_Deterministic()
        {
            var d = new byte[] { 1, 2, 3, 4, 5 };
            Assert.AreEqual(SchemaHashCore.Hash(d), SchemaHashCore.Hash(d));
        }

        [Test]
        public void HashUtf8_MatchesByteVersion()
        {
            var s = "Game.Domain.Health";
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            Assert.AreEqual(SchemaHashCore.Hash(bytes), SchemaHashCore.HashUtf8(s));
        }

        [Test]
        public void Hash_EmptyInput_ReturnsOffsetBasis()
        {
            Assert.AreEqual(SchemaHashCore.FnvOffsetBasis, SchemaHashCore.Hash(new byte[0]));
        }

        [Test]
        public void SchemaHasher_ComputeHash_Deterministic()
        {
            var fields = new[]
            {
                new FieldModel("A", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0),
                new FieldModel("B", FieldKind.Primitive, SchemaHashKind.Int32, 4, 4),
            };
            var model = new TypeModel("Test.Type", 8, fields);
            var h1 = SchemaHasher.ComputeHash(model);
            var h2 = SchemaHasher.ComputeHash(model);
            Assert.AreEqual(h1, h2);
        }

        [Test]
        public void SchemaHasher_DifferentLayouts_DifferentHashes()
        {
            var f1 = new[] { new FieldModel("A", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) };
            var f2 = new[] { new FieldModel("A", FieldKind.Primitive, SchemaHashKind.Int64, 8, 0) };
            var h1 = SchemaHasher.ComputeHash(new TypeModel("Test.Type", 4, f1));
            var h2 = SchemaHasher.ComputeHash(new TypeModel("Test.Type", 8, f2));
            Assert.AreNotEqual(h1, h2);
        }

        [Test]
        public void SchemaHasher_FieldNameDoesNotAffectHash()
        {
            var a = new[] { new FieldModel("A", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) };
            var b = new[] { new FieldModel("B", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) };
            var h1 = SchemaHasher.ComputeHash(new TypeModel("Test.Type", 4, a));
            var h2 = SchemaHasher.ComputeHash(new TypeModel("Test.Type", 4, b));
            Assert.AreEqual(h1, h2);
        }
    }
}
