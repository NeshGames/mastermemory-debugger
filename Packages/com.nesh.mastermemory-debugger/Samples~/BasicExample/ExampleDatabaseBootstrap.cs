using System.Collections.Generic;
using System.Linq;
using Nesh.MasterMemoryDebugger.Samples.BasicExample.Tables;

namespace Nesh.MasterMemoryDebugger.Samples.BasicExample
{
    /// <summary>
    /// Loads the master data. A real project usually does <c>new MemoryDatabase(binaryBytes)</c> with the binary
    /// produced by <c>DatabaseBuilder</c>; the sample builds the tables in code to stay self-contained.
    /// </summary>
    public static class ExampleDatabaseBootstrap
    {
        public const string MasterVersion = "example-2026.09.26.001";

        /// <summary>The immutable database as loaded. The debugger always compares against this one.</summary>
        public static MemoryDatabase OriginalDatabase { get; private set; }

        public static bool IsLoaded => OriginalDatabase != null;

        public static MemoryDatabase Load()
        {
            if (OriginalDatabase != null) return OriginalDatabase;

            var skills = new List<ExampleSkillMaster>
            {
                new ExampleSkillMaster { Id = 1001, Category = 1, Name = "Fireball", Damage = 120, Cooldown = 2.5f, Element = ExampleElement.Fire, UnlockLevel = 1, EffectIds = new[] { 10, 11 } },
                new ExampleSkillMaster { Id = 1002, Category = 1, Name = "Ice Blast", Damage = 100, Cooldown = 3.0f, Element = ExampleElement.Ice, UnlockLevel = 3, EffectIds = new[] { 20 } },
                new ExampleSkillMaster { Id = 1003, Category = 2, Name = "Heal", Damage = 0, Cooldown = 5.0f, IsPassive = false, EffectIds = new int[0] },
                new ExampleSkillMaster { Id = 1004, Category = 1, Name = "Thunder Strike", Damage = 180, Cooldown = 6.0f, Element = ExampleElement.Thunder, UnlockLevel = 10, EffectIds = new[] { 30, 31, 32 } },
                new ExampleSkillMaster { Id = 1005, Category = 3, Name = "Iron Skin", Damage = 0, Cooldown = 0f, IsPassive = true, EffectIds = new int[0] },
            };
            // generate a larger table to see ListView virtualization and the search limit
            for (var i = 0; i < 2000; i++)
            {
                skills.Add(new ExampleSkillMaster { Id = 5000 + i, Category = 9, Name = "Generated Skill " + i, Damage = i, Cooldown = 1f, EffectIds = new int[0] });
            }

            var items = new[]
            {
                new ExampleItemMaster { Id = 1, Name = "Potion", Price = 50, Rarity = ExampleRarity.Common, Stackable = true, DropRate = 0.25 },
                new ExampleItemMaster { Id = 2, Name = "Ether", Price = 120, Rarity = ExampleRarity.Rare, Stackable = true, DropRate = 0.1 },
                new ExampleItemMaster { Id = 3, Name = "Dragon Sword", Price = 99999, Rarity = ExampleRarity.Legendary, Stackable = false, DropRate = 0.001 },
            };

            var enemyLevels = new[]
            {
                new ExampleEnemyLevelMaster { EnemyId = 1, Level = 1, Hp = 100, Attack = 10, MoveSpeed = 1.0f },
                new ExampleEnemyLevelMaster { EnemyId = 1, Level = 2, Hp = 150, Attack = 14, MoveSpeed = 1.1f },
                new ExampleEnemyLevelMaster { EnemyId = 2, Level = 1, Hp = 400, Attack = 30, MoveSpeed = 0.8f },
            };

            // generated tables expect data sorted by primary key
            OriginalDatabase = new MemoryDatabase(
                ExampleEnemyLevelMasterTable: new ExampleEnemyLevelMasterTable(enemyLevels.OrderBy(x => x.EnemyId).ThenBy(x => x.Level).ToArray()),
                ExampleItemMasterTable: new ExampleItemMasterTable(items.OrderBy(x => x.Id).ToArray()),
                ExampleSkillMasterTable: new ExampleSkillMasterTable(skills.OrderBy(x => x.Id).ToArray()));
            return OriginalDatabase;
        }
    }
}
