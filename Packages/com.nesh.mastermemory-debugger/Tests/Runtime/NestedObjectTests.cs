using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class NestedObjectTests : DebuggerTestBase
    {
        public struct Range
        {
            public int Min;
            public int Max;
        }

        public sealed class Stats
        {
            public int Attack { get; set; }
            public float Speed { get; set; }
            public TestElement Element { get; set; }
            public Dictionary<string, int> Extra { get; set; }
            public Range Range { get; set; }
        }

        public sealed record NestedItem
        {
            public int Id { get; init; }
            public string Name { get; init; }
            public Stats Stats { get; init; }
            public Range Range { get; init; }
            public Range? OptionalRange { get; init; }
        }

        sealed class Computed
        {
            public int Value => 1;
        }

        abstract class AbstractThing
        {
            public int Value { get; set; }
        }

        sealed class Node
        {
            public int Value { get; set; }
            public Node Next { get; set; }
        }

        List<NestedItem> items;

        [SetUp]
        public void SetUp()
        {
            items = new List<NestedItem>
            {
                new NestedItem
                {
                    Id = 1,
                    Name = "Sword",
                    Stats = new Stats { Attack = 10, Speed = 1.5f, Element = TestElement.Fire, Extra = new Dictionary<string, int> { { "crit", 5 } }, Range = new Range { Min = 1, Max = 3 } },
                    Range = new Range { Min = 2, Max = 4 },
                },
                new NestedItem { Id = 2, Name = "Empty" },
            };
            MasterMemoryDebugRegistry.RegisterTable<NestedItem, int>("NestedItemMaster", () => items, x => x.Id, x => x.Name);
        }

        [TearDown]
        public void TearDown()
        {
            MasterDataPatchStorage.Delete();
        }

        static MasterMemoryFieldDescriptor Field(string name)
        {
            Assert.IsTrue(MasterDataReflectionCache.Get<NestedItem>().TryGetField(name, out var field), name);
            return field;
        }

        static Stats CopyStats(Stats stats, Action<Stats> change)
        {
            var copy = MasterDataCloneUtility.Clone(stats);
            change(copy);
            return copy;
        }

        [Test]
        public void EditableObjects_ShouldBeRecognized()
        {
            Assert.IsTrue(MasterDataValueUtility.IsEditableObject(typeof(Stats)));
            Assert.IsTrue(MasterDataValueUtility.IsEditableObject(typeof(Range)));
            Assert.IsTrue(MasterDataValueUtility.IsEditableObject(typeof(Node)));

            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(int)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(string)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(TestElement)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(UnityEngine.Vector3)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(int[])));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(List<Stats>)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(Dictionary<string, int>)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(DateTime)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(TimeSpan)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(decimal)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(Guid)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(object)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(IDisposable)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(AbstractThing)));
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(Computed)), "no writable member");
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(MasterMemoryDebuggerSettings)), "UnityEngine.Object");
            Assert.IsFalse(MasterDataValueUtility.IsEditableObject(typeof(Range?)), "the field descriptor passes the underlying type");
        }

        [Test]
        public void ObjectFields_ShouldBeEditable()
        {
            foreach (var name in new[] { "Stats", "Range", "OptionalRange" })
            {
                var field = Field(name);
                Assert.AreEqual(MasterDataValueKind.Complex, field.Kind, name);
                Assert.IsTrue(field.IsObject, name);
                Assert.IsFalse(field.IsList, name);
                Assert.IsTrue(field.CanEdit, name);
            }
            Assert.IsTrue(Field("OptionalRange").IsNullable);
            Assert.IsFalse(Field("Name").IsObject);
            Assert.IsFalse(MasterDataReflectionCache.Get<Stats>().Fields.Single(x => x.Name == "Extra").CanEdit);
        }

        [Test]
        public void Format_ShouldListTheMembers()
        {
            Assert.AreEqual("{Attack: 10, Speed: 1.5, Element: Fire, Extra: {1} crit: 5, Range: {Min: 1, Max: 3}}", MasterDataValueUtility.Format(items[0].Stats));
            Assert.AreEqual("{Min: 0, Max: 0}", MasterDataValueUtility.Format(new Range()));
            StringAssert.StartsWith("NestedItem {", MasterDataValueUtility.Format(items[0]), "types with their own ToString keep it");

            var node = new Node { Value = 1 };
            node.Next = node;
            StringAssert.Contains("{...}", MasterDataValueUtility.Format(node), "cycles stop at the depth limit");
        }

        [Test]
        public void AreEqual_ShouldCompareMembers()
        {
            var stats = items[0].Stats;
            Assert.IsTrue(MasterDataValueUtility.AreEqual(stats, MasterDataCloneUtility.Clone(stats)));
            Assert.IsFalse(MasterDataValueUtility.AreEqual(stats, CopyStats(stats, x => x.Attack = 11)));
            Assert.IsFalse(MasterDataValueUtility.AreEqual(stats, CopyStats(stats, x => x.Range = new Range { Min = 1, Max = 4 })));
            Assert.IsFalse(MasterDataValueUtility.AreEqual(stats, null));
            Assert.IsTrue(MasterDataValueUtility.AreEqual(new Range { Min = 1 }, new Range { Min = 1 }));

            var a = new Node { Value = 1 };
            a.Next = a;
            var b = new Node { Value = 1 };
            b.Next = b;
            Assert.IsFalse(MasterDataValueUtility.AreEqual(a, b), "cycles stop at the depth limit");
        }

        [Test]
        public void Json_ShouldRoundTripIntoACopyOfTheBase()
        {
            var original = items[0].Stats;
            var json = (MasterDataJsonObject)MasterDataValueUtility.ToJson(CopyStats(original, x => x.Attack = 99));
            CollectionAssert.AreEqual(new[] { "Attack", "Speed", "Element", "Range" }, json.Select(x => x.Key), "read-only members are not written");
            Assert.AreEqual("Fire", json["Element"]);

            var parsed = MasterDataJson.Parse(MasterDataJson.Serialize(json));
            var read = (Stats)MasterDataValueUtility.FromJson(parsed, typeof(Stats), original);
            Assert.AreNotSame(original, read);
            Assert.AreEqual(99, read.Attack);
            Assert.AreEqual(1.5f, read.Speed);
            Assert.AreEqual(3, read.Range.Max);
            Assert.AreSame(original.Extra, read.Extra, "members missing in the JSON keep the base value");
            Assert.AreEqual(10, original.Attack, "the base is never modified");

            var created = (Stats)MasterDataValueUtility.FromJson(parsed, typeof(Stats));
            Assert.AreEqual(99, created.Attack);
            Assert.IsNull(created.Extra);

            Assert.IsNull(MasterDataValueUtility.FromJson(null, typeof(Stats)));
            Assert.IsNull(MasterDataValueUtility.FromJson(null, typeof(Range?)));
            Assert.Throws<FormatException>(() => MasterDataValueUtility.FromJson(null, typeof(Range)));
            Assert.Throws<FormatException>(() => MasterDataValueUtility.FromJson(MasterDataJson.Parse("{\"Unknown\": 1}"), typeof(Stats)));
            Assert.Throws<FormatException>(() => MasterDataValueUtility.FromJson(MasterDataJson.Parse("{\"Extra\": null}"), typeof(Stats)));
            Assert.Throws<FormatException>(() => MasterDataValueUtility.FromJson(MasterDataJson.Parse("[1]"), typeof(Stats)));

            var range = (Range)MasterDataValueUtility.FromJson(MasterDataJson.Parse("{\"Max\": 7}"), typeof(Range), new Range { Min = 2, Max = 3 });
            Assert.AreEqual(2, range.Min);
            Assert.AreEqual(7, range.Max);
        }

        [Test]
        public void ObjectOverrides_ShouldBeSavedAndLoadedAsPatches()
        {
            var original = items[0];
            var stats = CopyStats(original.Stats, x =>
            {
                x.Attack = 25;
                x.Range = new Range { Min = 1, Max = 9 };
            });
            MasterMemoryDebugRuntime.SetOverride(1, original with { Stats = stats, OptionalRange = new Range { Min = 5, Max = 6 } });

            var warnings = new List<string>();
            var patch = MasterDataPatchService.CreatePatch(warnings);
            CollectionAssert.IsEmpty(warnings);
            var changes = patch.Tables.Single().Records.Single().Changes;
            CollectionAssert.AreEqual(new[] { "Stats", "OptionalRange" }, changes.Select(x => x.Field));
            Assert.AreEqual(
                "{\"Attack\":25,\"Speed\":1.5,\"Element\":\"Fire\",\"Range\":{\"Min\":1,\"Max\":9}}",
                MasterDataJson.Serialize(changes[0].Value, false));
            Assert.AreEqual(10, ((MasterDataJsonObject)changes[0].Original)["Attack"]);
            Assert.IsNull(changes[1].Original);

            MasterDataPatchStorage.Save(patch);
            MasterMemoryDebugRuntime.ClearAllOverrides();
            var result = MasterDataPatchService.Apply(MasterDataPatchStorage.Load());
            Assert.AreEqual(MasterDataPatchApplyStatus.Applied, result.Status);
            CollectionAssert.IsEmpty(result.Warnings);
            Assert.AreEqual(2, result.AppliedFields);

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<NestedItem, int>(1, out var loaded));
            Assert.AreEqual(25, loaded.Stats.Attack);
            Assert.AreEqual(9, loaded.Stats.Range.Max);
            Assert.AreSame(original.Stats.Extra, loaded.Stats.Extra, "read-only members come from the original");
            Assert.AreEqual(6, loaded.OptionalRange.Value.Max);
            Assert.AreEqual(10, original.Stats.Attack, "the original record is never modified");
            Assert.IsNull(original.OptionalRange);
        }

        [Test]
        public void PartialObjectValues_ShouldKeepTheOtherMembers()
        {
            var patch = MasterDataPatchSerializer.FromJson(@"{
  ""formatVersion"": 1,
  ""tables"": [{ ""tableName"": ""NestedItemMaster"", ""records"": [{
    ""primaryKey"": { ""key"": 1 },
    ""changes"": [{ ""field"": ""Stats"", ""original"": { ""Attack"": 12 }, ""value"": { ""Attack"": 30 } }]
  }] }]
}");
            var result = MasterDataPatchService.Apply(patch, force: true);
            Assert.AreEqual(1, result.AppliedFields, string.Join("\n", result.Warnings));
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("original value changed")), string.Join("\n", result.Warnings));

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<NestedItem, int>(1, out var loaded));
            Assert.AreEqual(30, loaded.Stats.Attack);
            Assert.AreEqual(TestElement.Fire, loaded.Stats.Element);
            Assert.AreEqual(3, loaded.Stats.Range.Max);
        }

        [Test]
        public void InvalidObjectValues_ShouldBeSkippedWithAWarning()
        {
            var patch = MasterDataPatchSerializer.FromJson(@"{
  ""formatVersion"": 1,
  ""tables"": [{ ""tableName"": ""NestedItemMaster"", ""records"": [{
    ""primaryKey"": { ""key"": 1 },
    ""changes"": [{ ""field"": ""Stats"", ""value"": { ""Power"": 30 } }, { ""field"": ""Name"", ""value"": ""Axe"" }]
  }] }]
}");
            var result = MasterDataPatchService.Apply(patch, force: true);
            Assert.AreEqual(1, result.AppliedFields);
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("'Stats' has an invalid value") && x.Contains("Power")), string.Join("\n", result.Warnings));
        }

        [Test]
        public void ChangesOfObjects_ShouldBeListedAndLogged()
        {
            var original = items[0];
            MasterMemoryDebugRuntime.SetOverride(1, original with { Stats = CopyStats(original.Stats, x => x.Speed = 2f) });

            var changes = MasterDataDiffUtility.GetChanges(original, MasterDataCloneUtility.Clone(original));
            CollectionAssert.IsEmpty(changes, "a clone has no change");

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<NestedItem, int>(1, out var current));
            var change = MasterDataDiffUtility.GetChanges(original, current).Single();
            Assert.AreEqual("Stats", change.Name);
            StringAssert.Contains("Speed: 2", MasterDataValueUtility.Format(change.NewValue));
        }
    }
}
