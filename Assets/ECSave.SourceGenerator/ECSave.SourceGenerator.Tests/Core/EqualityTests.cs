using ECSave.SourceGenerator.Core;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Core
{
    public class EqualityTests
    {
        [Test]
        public void FieldPlan_SameValues_Equal()
        {
            var a = new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0);
            var b = new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void FieldPlan_DifferentName_NotEqual()
        {
            var a = new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0);
            var b = new FieldPlan("Y", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void FieldModel_SameValues_Equal()
        {
            var a = new FieldModel("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0, 0);
            var b = new FieldModel("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0, 0);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void TypePlan_SameValues_Equal()
        {
            var fields = new[] { new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) };
            var a = new TypePlan("A", 4, fields, new int[0], 4);
            var b = new TypePlan("A", 4, fields, new int[0], 4);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void TypePlan_DifferentStructSize_NotEqual()
        {
            var fields = new[] { new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) };
            var a = new TypePlan("A", 4, fields, new int[0], 4);
            var b = new TypePlan("A", 8, fields, new int[0], 8);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void TypeModel_SameValues_Equal()
        {
            var fields = new[] { new FieldModel("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0, 0) };
            var a = new TypeModel("A", 4, fields);
            var b = new TypeModel("A", 4, fields);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }
    }
}
