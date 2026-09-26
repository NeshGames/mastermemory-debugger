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

            var characters = new[]
            {
                new ExampleCharacterMaster { Id = 1, Name = "Knight", Hp = 1200, Attack = 80, Defense = 60, MoveSpeed = 1.0f, Element = ExampleElement.None },
                new ExampleCharacterMaster { Id = 2, Name = "Mage", Hp = 700, Attack = 140, Defense = 25, MoveSpeed = 1.1f, Element = ExampleElement.Fire },
                new ExampleCharacterMaster { Id = 3, Name = "Archer", Hp = 850, Attack = 110, Defense = 35, MoveSpeed = 1.3f, Element = ExampleElement.Thunder },
            };

            var effects = new[]
            {
                new ExampleEffectMaster { Id = 10, Name = "Burn", Duration = 3f, Value = 15, IsDebuff = true },
                new ExampleEffectMaster { Id = 11, Name = "Knockback", Duration = 0f, Value = 2, IsDebuff = true },
                new ExampleEffectMaster { Id = 20, Name = "Freeze", Duration = 1.5f, Value = 0, IsDebuff = true },
                new ExampleEffectMaster { Id = 30, Name = "Shock", Duration = 2f, Value = 20, IsDebuff = true },
            };

            var shops = new[]
            {
                new ExampleShopMaster { Id = 1, ItemId = 1, Price = 50, Stock = 99 },
                new ExampleShopMaster { Id = 2, ItemId = 2, Price = 150, Stock = 20 },
                new ExampleShopMaster { Id = 3, ItemId = 3, Price = 120000, Stock = 1 },
            };

            var configs = new[]
            {
                new ExampleGameConfigMaster { Key = "MaxLevel", Value = "60" },
                new ExampleGameConfigMaster { Key = "StartGold", Value = "500" },
            };

            // generated tables expect data sorted by primary key
            OriginalDatabase = new MemoryDatabase(
                ExampleCharacterMasterTable: new ExampleCharacterMasterTable(characters.OrderBy(x => x.Id).ToArray()),
                ExampleEffectMasterTable: new ExampleEffectMasterTable(effects.OrderBy(x => x.Id).ToArray()),
                ExampleEnemyLevelMasterTable: new ExampleEnemyLevelMasterTable(enemyLevels.OrderBy(x => x.EnemyId).ThenBy(x => x.Level).ToArray()),
                ExampleGameConfigMasterTable: new ExampleGameConfigMasterTable(configs.OrderBy(x => x.Key, System.StringComparer.Ordinal).ToArray()),
                ExampleItemMasterTable: new ExampleItemMasterTable(items.OrderBy(x => x.Id).ToArray()),
                ExampleShopMasterTable: new ExampleShopMasterTable(shops.OrderBy(x => x.Id).ToArray()),
                ExampleSkillMasterTable: new ExampleSkillMasterTable(skills.OrderBy(x => x.Id).ToArray()));
            return OriginalDatabase;
        }
    }
}
