using System.Collections.Generic;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class SavedViewTests : DebuggerTestBase
    {
        [Test]
        public void SavedViews_ShouldRoundTripReplaceAndDelete()
        {
            var first = new MasterSavedView
            {
                Name = "Balance",
                TableName = "TestSkill",
                Query = "Damage>100",
                ModifiedOnly = true,
                SortKey = "Damage",
                SortDescending = true,
                ColumnLayout = "Damage\t0\t1\t222\t1\n",
            };

            Assert.IsTrue(MasterSavedViews.Save(first));
            var saved = MasterSavedViews.Find("balance");
            Assert.IsNotNull(saved);
            Assert.AreEqual("Balance", saved.Name);
            Assert.AreEqual("TestSkill", saved.TableName);
            Assert.AreEqual("Damage>100", saved.Query);
            Assert.IsTrue(saved.ModifiedOnly);
            Assert.AreEqual("Damage", saved.SortKey);
            Assert.IsTrue(saved.SortDescending);

            first.Query = "Name~fire";
            Assert.IsTrue(MasterSavedViews.Save(first));
            Assert.AreEqual(1, MasterSavedViews.Snapshot().Count);
            Assert.AreEqual("Name~fire", MasterSavedViews.Find("Balance").Query);

            Assert.IsTrue(MasterSavedViews.Delete("BALANCE"));
            Assert.IsEmpty(MasterSavedViews.Snapshot());
        }

        [Test]
        public void SavedViews_Json_ShouldBeVersionTolerantAndSkipBrokenEntries()
        {
            var json = MasterSavedViews.Serialize(new List<MasterSavedView>
            {
                new MasterSavedView
                {
                    Name = "A",
                    TableName = "TestSkill",
                    Query = "Damage>10",
                    ColumnLayout = "Name\t1\t0\t100\t0\n",
                },
            });
            var read = MasterSavedViews.Deserialize(json);

            Assert.AreEqual(1, read.Count);
            Assert.AreEqual("A", read[0].Name);
            Assert.AreEqual("Damage>10", read[0].Query);

            var future = json.Replace("\"formatVersion\":1", "\"formatVersion\":99");
            Assert.IsEmpty(MasterSavedViews.Deserialize(future));
            Assert.IsEmpty(MasterSavedViews.Deserialize("{broken"));
        }

        [Test]
        public void SavedViews_ShouldRejectInvalidNamesAndMissingTables()
        {
            Assert.IsNull(MasterSavedViews.NormalizeName("  "));
            Assert.IsNull(MasterSavedViews.NormalizeName("bad\nname"));
            Assert.IsFalse(MasterSavedViews.Save(new MasterSavedView { Name = "A" }));

            var longName = new string('x', 100);
            Assert.AreEqual(64, MasterSavedViews.NormalizeName(longName).Length);
        }
    }
}
