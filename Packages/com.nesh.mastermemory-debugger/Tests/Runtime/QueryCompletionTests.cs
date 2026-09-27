using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class QueryCompletionTests
    {
        static MasterDataTypeDescriptor Skill => MasterDataReflectionCache.Get<TestSkill>();

        static MasterRecordQueryCompletion.Context Context(string text, int caret = -1, MasterDataTypeDescriptor type = null)
        {
            return MasterRecordQueryCompletion.GetContext(text, caret < 0 ? text.Length : caret, type ?? Skill);
        }

        /// <summary>Accepts the first candidate, like Tab / Enter on the default selection.</summary>
        static string AcceptFirst(string text, int caret = -1, MasterDataTypeDescriptor type = null)
        {
            var context = Context(text, caret, type);
            Assert.IsNotNull(context, text);
            return MasterRecordQueryCompletion.Apply(text, context, context.Candidates[0], out _);
        }

        [Test]
        public void FieldNames_ShouldComplete()
        {
            Assert.AreEqual("Damage", AcceptFirst("Da"));
            Assert.AreEqual("Damage", AcceptFirst("damage"), "fixes the case");
            Assert.AreEqual("Damage", AcceptFirst("mag"), "contains when nothing starts with the word");
            Assert.AreEqual("Damage>100 Element", AcceptFirst("Damage>100 el"));
            Assert.AreEqual("Damage Name~x", AcceptFirst("Dam Name~x", 3), "replaces only the word at the caret");
            Assert.AreEqual("Damage>1", AcceptFirst("Dam>1", 2), "completes the field in front of an operator");
        }

        [Test]
        public void Candidates_ShouldListPrefixMatchesInDeclarationOrder()
        {
            CollectionAssert.AreEqual(new[] { "Category", "Cooldown" }, Context("c").Candidates);

            var text = "c";
            var result = MasterRecordQueryCompletion.Apply(text, Context(text), "Cooldown", out var caret);
            Assert.AreEqual("Cooldown", result);
            Assert.AreEqual(8, caret);
        }

        [Test]
        public void PrefixAndContainsMatches_ShouldNotMix()
        {
            var type = MasterDataReflectionCache.Get<TestEnemyLevel>();
            CollectionAssert.AreEqual(new[] { "EnemyId" }, Context("e", type: type).Candidates, "names containing the word are used only when none starts with it");
            CollectionAssert.AreEqual(new[] { "Level" }, Context("l", type: type).Candidates);
        }

        [Test]
        public void EnumAndBoolValues_ShouldComplete()
        {
            Assert.AreEqual("Element=Fire", AcceptFirst("Element=f"));
            Assert.AreEqual("element!=\"Ice\"", AcceptFirst("element!=\"i\"", 11));
            Assert.AreEqual("IsPassive = true", AcceptFirst("IsPassive = t"));
            Assert.AreEqual("Flags=Boss|Flying", AcceptFirst("Flags=Boss|Fl", type: MasterDataReflectionCache.Get<TestEnemyLevel>()));
            CollectionAssert.AreEqual(new[] { "None", "Fire", "Ice" }, Context("Element=").Candidates);
        }

        [Test]
        public void GroupedQueries_ShouldCompleteFieldsAndValues()
        {
            Assert.AreEqual("Damage>100 && (Element", AcceptFirst("Damage>100 && (El"));
            Assert.AreEqual("Damage>100||(Element=Fire)", AcceptFirst("Damage>100||(Element=F)", "Damage>100||(Element=F".Length));
            Assert.AreEqual("Damage>100 || Category", AcceptFirst("Damage>100 || Ca"));
            Assert.AreEqual("Damage>100||Category", AcceptFirst("Damage>100||Ca"));
            Assert.AreEqual("Element=Fire||Category", AcceptFirst("Element=Fire||Ca"));
            Assert.AreEqual("Damage>100 && (Category=1)", AcceptFirst("Damage>100 && (Ca=1)", "Damage>100 && (Ca".Length));
        }

        [Test]
        public void TextTermsAndOtherValues_ShouldNotComplete()
        {
            Assert.IsNull(Context("fireball"));
            Assert.IsNull(Context("Name~fir"));
            Assert.IsNull(Context("Damage>1"));
            Assert.IsNull(Context("Damage > "), "value position of a number field");
            Assert.IsNull(Context("Name=\"Ice B"), "inside a quoted text value");
            Assert.IsNull(MasterRecordQueryCompletion.GetContext("Da", 2, null), "no table selected");
        }

        [Test]
        public void Context_ShouldDescribeThePopup()
        {
            var field = Context("Damage");
            CollectionAssert.AreEqual(new[] { "Damage" }, field.Candidates);
            Assert.AreEqual("Damage", field.Word, "complete: the popup shows the operators");
            Assert.IsFalse(field.IsValue);

            var all = Context("Damage>1 ");
            Assert.AreEqual(Skill.Fields.Count, all.Candidates.Count, "an empty word lists every field");
            Assert.AreEqual("", all.Word);

            var value = Context("Element=");
            Assert.IsTrue(value.IsValue);
            Assert.AreEqual("Element", value.ValueField.Name);
        }
    }
}
