using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class QueryCompletionTests
    {
        static MasterDataTypeDescriptor Skill => MasterDataReflectionCache.Get<TestSkill>();

        static string Complete(MasterRecordQueryCompletion completion, ref string text, ref int caret, MasterDataTypeDescriptor type = null, bool backwards = false)
        {
            if (completion.TryComplete(text, caret, type ?? Skill, backwards, out var newText, out var newCaret))
            {
                text = newText;
                caret = newCaret;
            }
            return text;
        }

        static string CompleteOnce(string text, int caret = -1, MasterDataTypeDescriptor type = null)
        {
            if (caret < 0) caret = text.Length;
            return Complete(new MasterRecordQueryCompletion(), ref text, ref caret, type);
        }

        [Test]
        public void FieldNames_ShouldComplete()
        {
            Assert.AreEqual("Damage", CompleteOnce("Da"));
            Assert.AreEqual("Damage", CompleteOnce("damage"), "fixes the case");
            Assert.AreEqual("Damage", CompleteOnce("mag"), "contains when nothing starts with the word");
            Assert.AreEqual("Damage>100 Element", CompleteOnce("Damage>100 el"));
            Assert.AreEqual("Damage Name~x", CompleteOnce("Dam Name~x", 3), "replaces only the word at the caret");
            Assert.AreEqual("Damage>1", CompleteOnce("Dam>1", 2), "completes the field in front of an operator");
        }

        [Test]
        public void SeveralCandidates_ShouldCycle()
        {
            var completion = new MasterRecordQueryCompletion();
            var text = "c";
            var caret = 1;

            Assert.AreEqual("Category", Complete(completion, ref text, ref caret));
            Assert.AreEqual(8, caret);
            Assert.AreEqual("Cooldown", Complete(completion, ref text, ref caret));
            Assert.AreEqual("Category", Complete(completion, ref text, ref caret), "wraps around");
            Assert.AreEqual("Cooldown", Complete(completion, ref text, ref caret, backwards: true));

            text = "Cooldown<3 Cat";
            caret = text.Length;
            Assert.AreEqual("Cooldown<3 Category", Complete(completion, ref text, ref caret), "editing the text stops the cycle");
        }

        [Test]
        public void PrefixAndContainsMatches_ShouldNotMix()
        {
            var type = MasterDataReflectionCache.Get<TestEnemyLevel>();
            var completion = new MasterRecordQueryCompletion();
            var text = "e";
            var caret = 1;
            Assert.AreEqual("EnemyId", Complete(completion, ref text, ref caret, type), "names containing the word are used only when none starts with it");

            text = "l";
            caret = 1;
            Assert.AreEqual("Level", Complete(new MasterRecordQueryCompletion(), ref text, ref caret, type));
        }

        [Test]
        public void EnumAndBoolValues_ShouldComplete()
        {
            Assert.AreEqual("Element=Fire", CompleteOnce("Element=f"));
            Assert.AreEqual("element!=\"Ice\"", CompleteOnce("element!=\"i\"", 11));
            Assert.AreEqual("IsPassive = true", CompleteOnce("IsPassive = t"));
            Assert.AreEqual("Flags=Boss|Flying", CompleteOnce("Flags=Boss|Fl", type: MasterDataReflectionCache.Get<TestEnemyLevel>()));

            var completion = new MasterRecordQueryCompletion();
            var text = "Element=";
            var caret = text.Length;
            Assert.AreEqual("Element=None", Complete(completion, ref text, ref caret));
            Assert.AreEqual("Element=Fire", Complete(completion, ref text, ref caret));
            Assert.AreEqual("Fire", completion.CurrentCandidate);
            Assert.IsTrue(completion.IsCycling(text, caret));
        }

        [Test]
        public void TextTermsAndOtherValues_ShouldNotComplete()
        {
            Assert.IsNull(MasterRecordQueryCompletion.GetContext("fireball", 8, Skill));
            Assert.IsNull(MasterRecordQueryCompletion.GetContext("Name~fir", 8, Skill));
            Assert.IsNull(MasterRecordQueryCompletion.GetContext("Damage>1", 8, Skill));
            Assert.IsNull(MasterRecordQueryCompletion.GetContext("Damage > ", 9, Skill), "value position of a number field");
            Assert.IsNull(MasterRecordQueryCompletion.GetContext("Name=\"Ice B", 11, Skill), "inside a quoted text value");
            Assert.IsNull(MasterRecordQueryCompletion.GetContext("Da", 2, null), "no table selected");
            Assert.AreEqual("Damage", CompleteOnce("Damage"), "nothing left to complete");
        }

        [Test]
        public void Context_ShouldDescribeTheHint()
        {
            var field = MasterRecordQueryCompletion.GetContext("Damage", 6, Skill);
            CollectionAssert.AreEqual(new[] { "Damage" }, field.Candidates);
            Assert.AreEqual("Damage", field.Word);
            Assert.IsFalse(field.IsValue);

            var all = MasterRecordQueryCompletion.GetContext("Damage>1 ", 9, Skill);
            Assert.AreEqual(Skill.Fields.Count, all.Candidates.Count, "an empty word lists every field");

            var value = MasterRecordQueryCompletion.GetContext("Element=", 8, Skill);
            Assert.IsTrue(value.IsValue);
            Assert.AreEqual("Element", value.ValueField.Name);
            CollectionAssert.AreEqual(new[] { "None", "Fire", "Ice" }, value.Candidates);
        }
    }
}
