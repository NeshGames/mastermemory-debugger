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
                new ExampleCharacterMaster { Id = 1, Name = "Knight", Hp = 1200, Attack = 80, Defense = 60, MoveSpeed = 1.0f, Element = ExampleElement.None, StartSkillId = 1005 },
                new ExampleCharacterMaster { Id = 2, Name = "Mage", Hp = 700, Attack = 140, Defense = 25, MoveSpeed = 1.1f, Element = ExampleElement.Fire, StartSkillId = 1001 },
                new ExampleCharacterMaster { Id = 3, Name = "Archer", Hp = 850, Attack = 110, Defense = 35, MoveSpeed = 1.3f, Element = ExampleElement.Thunder, StartSkillId = 1004 },
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

            var weapons = CreateWeapons(300);

            // generated tables expect data sorted by primary key
            OriginalDatabase = new MemoryDatabase(
                ExampleCharacterMasterTable: new ExampleCharacterMasterTable(characters.OrderBy(x => x.Id).ToArray()),
                ExampleEffectMasterTable: new ExampleEffectMasterTable(effects.OrderBy(x => x.Id).ToArray()),
                ExampleEnemyLevelMasterTable: new ExampleEnemyLevelMasterTable(enemyLevels.OrderBy(x => x.EnemyId).ThenBy(x => x.Level).ToArray()),
                ExampleGameConfigMasterTable: new ExampleGameConfigMasterTable(configs.OrderBy(x => x.Key, System.StringComparer.Ordinal).ToArray()),
                ExampleItemMasterTable: new ExampleItemMasterTable(items.OrderBy(x => x.Id).ToArray()),
                ExampleShopMasterTable: new ExampleShopMasterTable(shops.OrderBy(x => x.Id).ToArray()),
                ExampleSkillMasterTable: new ExampleSkillMasterTable(skills.OrderBy(x => x.Id).ToArray()),
                ExampleWeaponMasterTable: new ExampleWeaponMasterTable(weapons));
            return OriginalDatabase;
        }

        /// <summary>Deterministic wide records for the scrolling test table.</summary>
        static ExampleWeaponMaster[] CreateWeapons(int count)
        {
            var kinds = new[] { "Sword", "Axe", "Spear", "Bow", "Staff", "Dagger" };
            var skillIds = new[] { 0, 1001, 1002, 1004 };
            var weapons = new ExampleWeaponMaster[count];
            for (var i = 0; i < count; i++)
            {
                var kind = kinds[i % kinds.Length];
                var rarity = (ExampleRarity)(i % 4);
                weapons[i] = new ExampleWeaponMaster
                {
                    Id = 20001 + i,
                    Name = $"{rarity} {kind} {i + 1}",
                    Rarity = rarity,
                    Element = (ExampleElement)(i % 4),
                    RequiredLevel = 1 + i % 60,
                    Attack = 10 + i * 3 % 250,
                    MagicAttack = kind == "Staff" ? 20 + i % 200 : 0,
                    Defense = i % 40,
                    CriticalRate = 0.05f + i % 10 * 0.01f,
                    CriticalDamage = 1.5f + i % 5 * 0.1f,
                    AttackSpeed = 0.8f + i % 7 * 0.05f,
                    Range = kind == "Bow" ? 12f : kind == "Spear" ? 2.5f : 1.5f,
                    Weight = 1f + i % 9 * 0.5f,
                    Durability = 50 + i % 150,
                    MaxUpgrade = 5 + (int)rarity * 5,
                    Price = 100 + i * 37L,
                    SellPrice = (100 + i * 37L) / 4,
                    Tradable = i % 3 != 0,
                    Upgradable = rarity != ExampleRarity.Common,
                    SkillId = skillIds[i % skillIds.Length],
                    SetId = i % 5 == 0 ? 900 + i / 5 % 10 : (int?)null,
                    IconPath = $"Icons/Weapons/{kind.ToLowerInvariant()}_{i % 20:00}",
                    ModelPath = $"Models/Weapons/{kind}/{kind}_{i % 20:00}.prefab",
                    Description = $"A {rarity.ToString().ToLowerInvariant()} {kind.ToLowerInvariant()} for testing wide tables.",
                    SocketTypes = i % 4 == 0 ? new int[0] : new[] { 1, 1 + i % 3 },
                    DropRate = 0.5 / (1 + (int)rarity * 4),
                    ReleaseVersion = $"1.{i / 100}.0",
                };
            }
            return weapons;
        }
    }
}
