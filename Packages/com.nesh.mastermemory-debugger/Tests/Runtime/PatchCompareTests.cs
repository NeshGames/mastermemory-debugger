using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class PatchCompareTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
        }

        MasterDataPatch Snapshot() => MasterDataPatchSerializer.FromJson(MasterDataPatchService.CreatePatchJson());

        [Test]
        public void Compare_ShouldListFieldsChangedDifferently()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 150, Cooldown = 1.5f });
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Damage = 7 });
            var saved = Snapshot();

            MasterMemoryDebugRuntime.ClearAllOverrides();
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 200, Cooldown = 1.5f });
            MasterMemoryDebugRuntime.SetOverride((2, 1), Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((2, 1)) with { Hp = 1 });
            var current = MasterDataPatchService.CreatePatch();

            var differences = MasterDataPatchCompare.Compare(saved, current);

            Assert.AreEqual(3, differences.Count, string.Join("\n", differences.Select(x => $"{x.TableName} {x.Key} {x.Field} {x.Kind}")));
            var damage = differences.Single(x => x.Key.Contains("1001"));
            Assert.AreEqual(MasterDataPatchDifferenceKind.Different, damage.Kind);
            Assert.AreEqual("Damage", damage.Field);
            Assert.AreEqual("150", MasterDataPatchCompare.Format(damage.ValueA));
            Assert.AreEqual("200", MasterDataPatchCompare.Format(damage.ValueB));
            Assert.AreEqual("120", MasterDataPatchCompare.Format(damage.Original));
            Assert.AreEqual(MasterDataPatchDifferenceKind.OnlyInA, differences.Single(x => x.Key.Contains("1002")).Kind);
            Assert.AreEqual(MasterDataPatchDifferenceKind.OnlyInB, differences.Single(x => x.TableName == nameof(TestEnemyLevel)).Kind);
        }

        [Test]
        public void SamePatches_ShouldHaveNoDifference()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 150, Cooldown = 1.25f, Element = TestElement.Ice });
            Assert.IsEmpty(MasterDataPatchCompare.Compare(Snapshot(), MasterDataPatchService.CreatePatch()), "a saved file equals the overrides it was saved from");
        }

        [Test]
        public void Tsv_ShouldHaveOneLinePerDifference()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 150 });
            var differences = MasterDataPatchCompare.Compare(Snapshot(), new MasterDataPatch());
            var lines = MasterDataPatchCompare.ToTsv(differences, "saved", "empty").TrimEnd('\n').Split('\n');
            Assert.AreEqual("table\tkey\tfield\toriginal\tsaved\tempty", lines[0]);
            Assert.AreEqual(2, lines.Length);
            StringAssert.EndsWith("\tDamage\t120\t150\t", lines[1]);
        }
    }
}
