using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Editor.Tests
{
    public class LinkerProcessorTests
    {
        sealed class Nested
        {
        }

        sealed class Holder
        {
            public Inner Inner { get; set; }
            public (int, int) Pair { get; set; }
            public string Name { get; set; }
        }

        sealed class Inner
        {
            public Deepest Deepest { get; set; }
            public Inner Self { get; set; }
        }

        struct Deepest
        {
            public int Value;
        }

        [Test]
        public void LinkXml_ShouldPreserveEveryTypeByAssembly()
        {
            var xml = MasterMemoryDebuggerLinkerProcessor.CreateLinkXml(new[] { typeof(LinkerProcessorTests), typeof(Nested) });

            StringAssert.StartsWith("<linker>", xml);
            StringAssert.Contains("<assembly fullname=\"Nesh.MasterMemoryDebugger.Editor.Tests\">", xml);
            StringAssert.Contains("<type fullname=\"Nesh.MasterMemoryDebugger.Editor.Tests.LinkerProcessorTests\" preserve=\"all\" />", xml);
            StringAssert.Contains("<type fullname=\"Nesh.MasterMemoryDebugger.Editor.Tests.LinkerProcessorTests/Nested\" preserve=\"all\" />", xml);
        }

        [Test]
        public void NestedObjectTypes_ShouldBeCollected()
        {
            var types = MasterMemoryDebuggerLinkerProcessor.CollectNestedObjectTypes(new[] { typeof(Holder) });
            CollectionAssert.AreEquivalent(new[] { typeof(Inner), typeof(Deepest) }, types, "generic types and simple values are left out");
        }

        [Test]
        public void CollectTypes_ShouldFindRecordsAndGeneratedTypes()
        {
            var types = MasterMemoryDebuggerLinkerProcessor.CollectTypes();

            // the package tests define [MemoryTable] records and their generated database
            Assert.IsTrue(types.Any(x => x.Name == "TestSkill"));
            Assert.IsTrue(types.Any(x => x.Name == "MemoryDatabase"));
            Assert.IsTrue(types.Any(x => x.Name == "ImmutableBuilder"));
            Assert.IsFalse(types.Any(x => x.IsGenericTypeDefinition || x.IsAbstract));
        }
    }
}
