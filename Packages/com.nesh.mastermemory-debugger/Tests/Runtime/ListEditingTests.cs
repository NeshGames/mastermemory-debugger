using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class ListEditingTests : DebuggerTestBase
    {
        sealed class Nested
        {
            public int Value { get; set; }
        }

        sealed class ClassValue
        {
            public ClassValue(string text)
            {
            }
        }

        [TearDown]
        public void TearDown()
        {
            MasterDataPatchStorage.Delete();
        }

        [Test]
        public void EditableLists_ShouldBeRecognized()
        {
            Assert.IsTrue(MasterDataValueUtility.TryGetEditableListElement(typeof(int[]), out var element));
            Assert.AreEqual(typeof(int), element);
            Assert.IsTrue(MasterDataValueUtility.TryGetEditableListElement(typeof(List<string>), out element));
            Assert.AreEqual(typeof(string), element);
            Assert.IsTrue(MasterDataValueUtility.TryGetEditableListElement(typeof(IReadOnlyList<TestElement>), out _));
            Assert.IsTrue(MasterDataValueUtility.TryGetEditableListElement(typeof(IList<float>), out _));

            Assert.IsFalse(MasterDataValueUtility.TryGetEditableListElement(typeof(string), out _));
            Assert.IsFalse(MasterDataValueUtility.TryGetEditableListElement(typeof(int[,]), out _));
            Assert.IsFalse(MasterDataValueUtility.TryGetEditableListElement(typeof(List<int?>), out _));
            Assert.IsFalse(MasterDataValueUtility.TryGetEditableListElement(typeof(List<Nested>), out _));
            Assert.IsFalse(MasterDataValueUtility.TryGetEditableListElement(typeof(Dictionary<int, int>), out _));
            Assert.IsFalse(MasterDataValueUtility.TryGetEditableListElement(typeof(HashSet<int>), out _));
        }

        [Test]
        public void ListFields_ShouldBeEditable()
        {
            var tags = MasterDataReflectionCache.Get<TestSkill>().Fields.Single(x => x.Name == "Tags");
            Assert.IsTrue(tags.IsList);
            Assert.IsTrue(tags.CanEdit);
            Assert.AreEqual(typeof(int), tags.ElementType);
            Assert.AreEqual(MasterDataValueKind.Int32, tags.ElementKind);
        }

        [Test]
        public void CreateList_ShouldReturnNewCollectionsOfTheMemberType()
        {
            var items = new List<object> { 1, 2 };
            var array = MasterDataValueUtility.CreateList(typeof(int[]), typeof(int), items);
            CollectionAssert.AreEqual(new[] { 1, 2 }, (int[])array);
            Assert.IsInstanceOf<List<int>>(MasterDataValueUtility.CreateList(typeof(List<int>), typeof(int), items));
            Assert.IsInstanceOf<int[]>(MasterDataValueUtility.CreateList(typeof(IReadOnlyList<int>), typeof(int), items));
            Assert.AreEqual("", MasterDataValueUtility.CreateDefaultElement(typeof(string)));
            Assert.AreEqual(TestElement.None, MasterDataValueUtility.CreateDefaultElement(typeof(TestElement)));
        }

        [Test]
        public void ListJson_ShouldRoundTrip()
        {
            var json = MasterDataValueUtility.ToJson(new List<TestElement> { TestElement.Fire, TestElement.Ice });
            CollectionAssert.AreEqual(new object[] { "Fire", "Ice" }, (List<object>)json);

            var parsed = MasterDataJson.Parse(MasterDataJson.Serialize(MasterDataValueUtility.ToJson(new[] { 1, 2, 3 })));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])MasterDataValueUtility.FromJson(parsed, typeof(int[])));
            Assert.IsNull(MasterDataValueUtility.FromJson(null, typeof(int[])));
            Assert.Throws<FormatException>(() => MasterDataValueUtility.FromJson("x", typeof(int[])));
        }

        [Test]
        public void ListsOfCustomValues_ShouldBeEditableAndSavedAsText()
        {
            var curve = MasterDataReflectionCache.Get<TestTuning>().Fields.Single(x => x.Name == "Curve");
            Assert.IsTrue(curve.IsList);
            Assert.IsTrue(curve.CanEdit);
            Assert.AreEqual(MasterDataValueKind.Custom, curve.ElementKind);
            Assert.IsInstanceOf<TestFixedConverter>(curve.ElementConverter);
            using (MasterDataValueConverters.Register(new UnusedConverter(typeof(ClassValue))))
            {
                Assert.IsNull(MasterDataValueUtility.CreateDefaultElement(typeof(ClassValue)), "+ Add of a class without a parameterless constructor");
            }

            var walk = RegisterTunings()[0];
            MasterMemoryDebugRuntime.SetOverride(1, walk with { Curve = new[] { TestFixed.FromRaw(500), TestFixed.FromRaw(750) } });
            var change = MasterDataPatchService.CreatePatch().Tables.Single().Records.Single().Changes.Single();
            CollectionAssert.AreEqual(new object[] { "0.5", "0.75" }, (List<object>)change.Value);

            var json = MasterDataPatchService.CreatePatchJson();
            MasterMemoryDebugRuntime.ClearAllOverrides();
            MasterDataPatchService.Apply(MasterDataPatchSerializer.FromJson(json));
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestTuning, int>(1, out var loaded));
            CollectionAssert.AreEqual(new long[] { 500, 750 }, loaded.Curve.Select(x => x.Raw));
            Assert.AreEqual("[2] 0.5, 0.75", MasterDataValueUtility.Format(loaded.Curve));
        }

        [Test]
        public void ListOverrides_ShouldBeSavedAndLoadedAsPatches()
        {
            RegisterTestDatabase();
            var original = Database.TestSkillTable.FindById(1001);
            MasterMemoryDebugRuntime.SetOverride(1001, original with { Tags = new[] { 1, 2, 7 } });

            var patch = MasterDataPatchService.CreatePatch();
            var change = patch.Tables.Single().Records.Single().Changes.Single();
            Assert.AreEqual("Tags", change.Field);
            CollectionAssert.AreEqual(new object[] { 1, 2, 7 }, ((List<object>)change.Value).Select(x => (int)x).ToArray());

            var json = MasterDataPatchSerializer.ToJson(patch);
            MasterMemoryDebugRuntime.ClearAllOverrides();
            var result = MasterDataPatchService.Apply(MasterDataPatchSerializer.FromJson(json));

            Assert.AreEqual(1, result.AppliedRecords, string.Join("\n", result.Warnings));
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var loaded));
            CollectionAssert.AreEqual(new[] { 1, 2, 7 }, loaded.Tags);
            CollectionAssert.AreEqual(new[] { 1, 2 }, original.Tags, "the MasterMemory record is untouched");
            Assert.AreNotSame(original.Tags, loaded.Tags);
        }
    }
}
