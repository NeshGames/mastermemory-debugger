using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class ReflectionTests : DebuggerTestBase
    {
        static MasterRecordQuery Q(string text) => MasterRecordQuery.Parse(text, MasterDataReflectionCache.Get<TestSkill>());

        static MasterMemoryFieldDescriptor Field<T>(string name)
        {
            Assert.IsTrue(MasterDataReflectionCache.Get<T>().TryGetField(name, out var field), name);
            return field;
        }

        [Test]
        public void PrimaryKey_ShouldBeReadonly()
        {
            var id = Field<TestSkill>("Id");
            Assert.IsTrue(id.IsPrimaryKey);
            Assert.IsFalse(id.CanEdit);

            var copy = MasterDataCloneUtility.Clone(Database.TestSkillTable.FindById(1001));
            Assert.Throws<InvalidOperationException>(() => id.SetValue(copy, 5));

            var descriptor = MasterDataReflectionCache.Get<TestEnemyLevel>();
            CollectionAssert.AreEqual(new[] { "EnemyId", "Level" }, new[] { descriptor.PrimaryKeyFields[0].Name, descriptor.PrimaryKeyFields[1].Name });
            Assert.AreEqual("EnemyId", descriptor.Fields[0].Name, "primary keys are listed first");
        }

        [Test]
        public void SecondaryKey_ShouldBeReadonly()
        {
            var category = Field<TestSkill>("Category");
            Assert.IsTrue(category.IsSecondaryKey);
            Assert.IsFalse(category.CanEdit);
            var copy = MasterDataCloneUtility.Clone(Database.TestSkillTable.FindById(1001));
            Assert.Throws<InvalidOperationException>(() => category.SetValue(copy, 9));
        }

        [Test]
        public void PrimitiveField_ShouldBeEditable()
        {
            var original = Database.TestSkillTable.FindById(1001);
            var copy = MasterDataCloneUtility.Clone(original);
            Assert.AreNotSame(original, copy);

            Field<TestSkill>("Damage").SetValue(copy, 185);
            Field<TestSkill>("Name").SetValue(copy, "Meteor");
            Field<TestSkill>("Cooldown").SetValue(copy, 1.5f);
            Field<TestSkill>("Element").SetValue(copy, TestElement.Ice);
            Field<TestSkill>("IsPassive").SetValue(copy, true);
            Field<TestSkill>("UnlockLevel").SetValue(copy, null);

            Assert.AreEqual(185, copy.Damage);
            Assert.AreEqual("Meteor", copy.Name);
            Assert.AreEqual(1.5f, copy.Cooldown);
            Assert.AreEqual(TestElement.Ice, copy.Element);
            Assert.IsTrue(copy.IsPassive);
            Assert.IsNull(copy.UnlockLevel);

            Assert.AreEqual(120, original.Damage, "the original record must never change");
            Assert.AreEqual("Fireball", original.Name);
            Assert.AreEqual(3, original.UnlockLevel);
        }

        [Test]
        public void FieldKinds_ShouldBeDetected()
        {
            Assert.AreEqual(MasterDataValueKind.Int32, Field<TestSkill>("Damage").Kind);
            Assert.AreEqual(MasterDataValueKind.Single, Field<TestSkill>("Cooldown").Kind);
            Assert.AreEqual(MasterDataValueKind.Enum, Field<TestSkill>("Element").Kind);
            Assert.AreEqual(MasterDataValueKind.UInt64, Field<TestSkill>("BigValue").Kind);
            Assert.AreEqual(MasterDataValueKind.FlagsEnum, Field<TestEnemyLevel>("Flags").Kind);

            var unlock = Field<TestSkill>("UnlockLevel");
            Assert.IsTrue(unlock.IsNullable);
            Assert.AreEqual(MasterDataValueKind.Int32, unlock.Kind);
            Assert.IsTrue(unlock.CanEdit);
        }

        sealed class ComplexItem
        {
            public System.Collections.Generic.Dictionary<string, int> Map { get; set; }
            public ComplexItem Child { get; set; }
        }

        [Test]
        public void ListOfSimpleValues_ShouldBeEditable()
        {
            var tags = Field<TestSkill>("Tags");
            Assert.AreEqual(MasterDataValueKind.Complex, tags.Kind);
            Assert.IsTrue(tags.IsList);
            Assert.IsTrue(tags.CanEdit);
            Assert.AreEqual("[2] 1, 2", MasterDataValueUtility.Format(tags.GetValue(Database.TestSkillTable.FindById(1001))));
        }

        [Test]
        public void OtherComplexFields_ShouldBeReadonly()
        {
            foreach (var field in MasterDataReflectionCache.Get<ComplexItem>().Fields)
            {
                Assert.AreEqual(MasterDataValueKind.Complex, field.Kind, field.Name);
                Assert.IsFalse(field.IsList, field.Name);
                Assert.IsFalse(field.CanEdit, field.Name);
            }
        }

        [Test]
        public void PrivateSetterAndGetOnly_ShouldBeHandled()
        {
            Assert.IsTrue(Field<ManualItem>("Price").CanEdit, "private set");
            Assert.IsTrue(Field<ManualItem>("Title").CanEdit, "get-only auto property uses the backing field");

            var item = new ManualItem(1, "Potion", 50);
            var copy = MasterDataCloneUtility.Clone(item);
            Field<ManualItem>("Title").SetValue(copy, "Hi-Potion");
            Assert.AreEqual("Hi-Potion", copy.Title);
            Assert.AreEqual("Potion", item.Title);
        }

        sealed class ComputedItem
        {
            public int Value { get; set; }
            public int[] Values { get; set; }
            public string Broken => throw new InvalidOperationException("boom");
        }

        [Test]
        public void ComputedProperty_ShouldBeReadonlyAndNeverThrow()
        {
            var broken = Field<ComputedItem>("Broken");
            Assert.IsFalse(broken.CanEdit);
            StringAssert.Contains("boom", (string)broken.GetValue(new ComputedItem()));
        }

        [Test]
        public void AreEqual_ShouldCompareCollectionsByElement()
        {
            Assert.IsTrue(MasterDataValueUtility.AreEqual(new[] { 1, 2 }, new[] { 1, 2 }));
            Assert.IsFalse(MasterDataValueUtility.AreEqual(new[] { 1, 2 }, new[] { 1, 3 }));
            Assert.IsFalse(MasterDataValueUtility.AreEqual(new[] { 1 }, new[] { 1, 2 }));
            Assert.IsFalse(MasterDataValueUtility.AreEqual("ab", new[] { 'a', 'b' }));
        }

        [Test]
        public void Diff_ShouldListChangedFieldsWithOldAndNewValues()
        {
            var original = Database.TestSkillTable.FindById(1001);
            var changed = original with { Damage = 185, Name = "Meteor" };

            var changes = MasterDataDiffUtility.GetChanges(original, changed);
            CollectionAssert.AreEquivalent(new[] { "Name", "Damage" }, changes.Select(x => x.Name));
            var damage = changes.Single(x => x.Name == "Damage");
            Assert.AreEqual(120, damage.OldValue);
            Assert.AreEqual(185, damage.NewValue);

            var plain = MasterDataDiffUtility.Format("SkillMaster 1001", changes, false);
            StringAssert.Contains("Damage: 120 → 185", plain);
            StringAssert.DoesNotContain("<color", plain);

            var rich = MasterDataDiffUtility.Format("SkillMaster 1001", changes, true);
            StringAssert.Contains("<color=", rich);
            StringAssert.Contains("Damage", rich);

            CollectionAssert.IsEmpty(MasterDataDiffUtility.GetChanges(original, original));
        }

        [Test]
        public void CloneProvider_ShouldBeUsed()
        {
            var calls = 0;
            MasterMemoryDebugRegistry.RegisterCloneProvider<ManualItem>(x =>
            {
                calls++;
                return new ManualItem(x.Code, x.Title, x.Price);
            });
            MasterDataCloneUtility.Clone(new ManualItem(1, "a", 1));
            Assert.AreEqual(1, calls);

            MasterMemoryDebugRegistry.RegisterCloneProvider<ManualItem>(x => x);
            Assert.Throws<InvalidOperationException>(() => MasterDataCloneUtility.Clone(new ManualItem(1, "a", 1)), "returning the source is rejected");
        }

        [Test]
        public void ModifiedOnly_ShouldFilterRecords()
        {
            RegisterTestDatabase();
            var table = Table<TestSkill>();
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Damage = 7 });

            var snapshot = table.CreateRecordSnapshot();
            var result = new List<MasterMemoryRecordDescriptor>();

            Assert.AreEqual(3, MasterRecordListController.Filter(snapshot, Q(""), false, 500, result));
            Assert.AreEqual(1, MasterRecordListController.Filter(snapshot, Q(""), true, 500, result));
            Assert.AreEqual(1002, result[0].PrimaryKey);
            Assert.IsTrue(result[0].IsModified);
            Assert.AreEqual(7, ((TestSkill)result[0].Current).Damage);
        }

        [Test]
        public void Search_ShouldMatchKeyNameAndStrings()
        {
            RegisterTestDatabase();
            var snapshot = Table<TestSkill>().CreateRecordSnapshot();
            var result = new List<MasterMemoryRecordDescriptor>();

            Assert.AreEqual(1, MasterRecordListController.Filter(snapshot, Q("1003"), false, 500, result));
            Assert.AreEqual(1, MasterRecordListController.Filter(snapshot, Q("ice"), false, 500, result));
            Assert.AreEqual(1002, result[0].PrimaryKey);
            Assert.AreEqual(0, MasterRecordListController.Filter(snapshot, Q("nothing"), false, 500, result));

            // max results: matches are counted, the list is capped
            Assert.AreEqual(3, MasterRecordListController.Filter(snapshot, Q(""), false, 2, result));
            Assert.AreEqual(2, result.Count);

            // overridden names are searchable
            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Name = "Mega Heal" });
            Assert.AreEqual(1, MasterRecordListController.Filter(snapshot, Q("mega"), false, 500, result));
        }
    }
}
