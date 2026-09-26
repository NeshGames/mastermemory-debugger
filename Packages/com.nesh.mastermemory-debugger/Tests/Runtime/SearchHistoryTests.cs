using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class SearchHistoryTests : DebuggerTestBase
    {
        [Test]
        public void History_ShouldKeepTheNewestDistinctQueries()
        {
            MasterSearchHistory.Add("Damage>100");
            MasterSearchHistory.Add("  Element=Fire ");
            MasterSearchHistory.Add("Damage>100");
            MasterSearchHistory.Add("");
            MasterSearchHistory.Add(null);

            CollectionAssert.AreEqual(new[] { "Damage>100", "Element=Fire" }, MasterSearchHistory.Entries.ToArray());
        }

        [Test]
        public void History_ShouldBeLimited()
        {
            for (var i = 0; i < MasterSearchHistory.MaxEntries + 3; i++) MasterSearchHistory.Add("Id=" + i);

            Assert.AreEqual(MasterSearchHistory.MaxEntries, MasterSearchHistory.Entries.Count);
            Assert.AreEqual("Id=" + (MasterSearchHistory.MaxEntries + 2), MasterSearchHistory.Entries[0]);
        }

        [Test]
        public void Find_ShouldMatchPartsOfTheQueries()
        {
            MasterSearchHistory.Add("Damage>100");
            MasterSearchHistory.Add("Element=Fire Damage<50");
            MasterSearchHistory.Add("Name~ice");

            Assert.AreEqual(3, MasterSearchHistory.Find("").Count);
            CollectionAssert.AreEqual(new[] { "Element=Fire Damage<50", "Damage>100" }, MasterSearchHistory.Find("damage"));
            Assert.IsEmpty(MasterSearchHistory.Find("Name~ice"), "the query already typed is not offered again");
        }
    }
}
