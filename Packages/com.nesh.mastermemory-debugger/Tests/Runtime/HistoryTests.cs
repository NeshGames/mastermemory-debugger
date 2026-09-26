using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class HistoryTests : DebuggerTestBase
    {
        TestSkill Skill(int id) => Database.TestSkillTable.FindById(id);

        static int Damage(int id) => MasterMemoryDebugRuntime.Store.TryGet(typeof(TestSkill), id, out var value) ? ((TestSkill)value).Damage : -1;

        [Test]
        public void UndoRedo_ShouldRestoreThePreviousOverrides()
        {
            using (MasterMemoryDebugHistory.Record("first"))
            {
                MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = 10 });
            }
            using (MasterMemoryDebugHistory.Record("second"))
            {
                MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = 20 });
                MasterMemoryDebugRuntime.SetOverride(1002, Skill(1002) with { Damage = 30 });
                MasterMemoryDebugRuntime.SetOverride(1002, Skill(1002) with { Damage = 31 });
            }
            Assert.AreEqual("second", MasterMemoryDebugHistory.UndoLabel);

            Assert.AreEqual("second", MasterMemoryDebugHistory.Undo());
            Assert.AreEqual(10, Damage(1001));
            Assert.IsFalse(MasterMemoryDebugRuntime.Store.IsOverridden(typeof(TestSkill), 1002), "the override did not exist before");
            Assert.AreEqual("second", MasterMemoryDebugHistory.RedoLabel);

            Assert.AreEqual("first", MasterMemoryDebugHistory.Undo());
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
            Assert.IsNull(MasterMemoryDebugHistory.Undo());

            MasterMemoryDebugHistory.Redo();
            MasterMemoryDebugHistory.Redo();
            Assert.AreEqual(20, Damage(1001));
            Assert.AreEqual(31, Damage(1002), "several changes of a record in one step end with the last one");
            Assert.IsFalse(MasterMemoryDebugHistory.CanRedo);
        }

        [Test]
        public void ResetAll_ShouldBeUndoable()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = 10 });
            MasterMemoryDebugRuntime.SetOverride(1002, Skill(1002) with { Damage = 20 });
            using (MasterMemoryDebugHistory.Record("Reset All")) MasterMemoryDebugRuntime.ClearAllOverrides();

            MasterMemoryDebugHistory.Undo();
            Assert.AreEqual(10, Damage(1001));
            Assert.AreEqual(20, Damage(1002));
        }

        [Test]
        public void NewStep_ShouldDropTheRedoSteps()
        {
            using (MasterMemoryDebugHistory.Record("a")) MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = 1 });
            MasterMemoryDebugHistory.Undo();
            Assert.IsTrue(MasterMemoryDebugHistory.CanRedo);

            using (MasterMemoryDebugHistory.Record("b")) MasterMemoryDebugRuntime.SetOverride(1002, Skill(1002) with { Damage = 2 });
            Assert.IsFalse(MasterMemoryDebugHistory.CanRedo);
        }

        [Test]
        public void StepsWithoutChanges_ShouldNotBeRecorded()
        {
            using (MasterMemoryDebugHistory.Record("nothing"))
            {
            }
            using (MasterMemoryDebugHistory.Record("remove missing")) MasterMemoryDebugRuntime.Store.Remove(typeof(TestSkill), 1001);
            Assert.IsFalse(MasterMemoryDebugHistory.CanUndo);
        }

        [Test]
        public void ChangesOutsideSteps_ShouldClearTheHistory()
        {
            var changed = 0;
            void OnChanged() => changed++;
            MasterMemoryDebugHistory.Changed += OnChanged;
            try
            {
                using (MasterMemoryDebugHistory.Record("a")) MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = 1 });
                Assert.AreEqual(1, changed);

                // game code: undoing across it would overwrite it
                MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = 5 });
                Assert.IsFalse(MasterMemoryDebugHistory.CanUndo);
                Assert.AreEqual(2, changed);
            }
            finally
            {
                MasterMemoryDebugHistory.Changed -= OnChanged;
            }
        }

        [Test]
        public void NestedSteps_ShouldJoinTheOutermostOne()
        {
            using (MasterMemoryDebugHistory.Record("outer"))
            {
                using (MasterMemoryDebugHistory.Record("inner")) MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = 1 });
                Assert.IsFalse(MasterMemoryDebugHistory.CanUndo, "not finished yet");
                MasterMemoryDebugRuntime.SetOverride(1002, Skill(1002) with { Damage = 2 });
            }
            Assert.AreEqual("outer", MasterMemoryDebugHistory.Undo());
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
        }

        [Test]
        public void History_ShouldKeepAtMostMaxSteps()
        {
            for (var i = 0; i < MasterMemoryDebugHistory.MaxSteps + 5; i++)
            {
                using (MasterMemoryDebugHistory.Record("step " + i)) MasterMemoryDebugRuntime.SetOverride(1001, Skill(1001) with { Damage = i });
            }
            var undone = 0;
            while (MasterMemoryDebugHistory.Undo() != null) undone++;
            Assert.AreEqual(MasterMemoryDebugHistory.MaxSteps, undone);
            Assert.AreEqual(4, Damage(1001), "the oldest steps were dropped");
        }
    }
}
