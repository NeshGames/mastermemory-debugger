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
            var manyColumns = CreateManyColumns(120);

            // generated tables expect data sorted by primary key
            OriginalDatabase = new MemoryDatabase(
                ExampleCharacterMasterTable: new ExampleCharacterMasterTable(characters.OrderBy(x => x.Id).ToArray()),
                ExampleEffectMasterTable: new ExampleEffectMasterTable(effects.OrderBy(x => x.Id).ToArray()),
                ExampleEnemyLevelMasterTable: new ExampleEnemyLevelMasterTable(enemyLevels.OrderBy(x => x.EnemyId).ThenBy(x => x.Level).ToArray()),
                ExampleGameConfigMasterTable: new ExampleGameConfigMasterTable(configs.OrderBy(x => x.Key, System.StringComparer.Ordinal).ToArray()),
                ExampleItemMasterTable: new ExampleItemMasterTable(items.OrderBy(x => x.Id).ToArray()),
                ExampleShopMasterTable: new ExampleShopMasterTable(shops.OrderBy(x => x.Id).ToArray()),
                ExampleSkillMasterTable: new ExampleSkillMasterTable(skills.OrderBy(x => x.Id).ToArray()),
                ExampleManyColumnsMasterTable: new ExampleManyColumnsMasterTable(manyColumns),
                ExampleWeaponMasterTable: new ExampleWeaponMasterTable(weapons));
            return OriginalDatabase;
        }

        /// <summary>Deterministic rows for the 75 column table.</summary>
        static ExampleManyColumnsMaster[] CreateManyColumns(int count)
        {
            var rows = new ExampleManyColumnsMaster[count];
            for (var i = 0; i < count; i++)
            {
                rows[i] = new ExampleManyColumnsMaster
                {
                    Id = 30001 + i,
                    Name = $"Row {i + 1:000}",
                    VeryLongFieldNameToCheckAutomaticColumnWidth = i % 2 == 0 ? "short" : "a considerably longer value that should be cut with an ellipsis in the grid",
                    LongDescription = $"Row {i + 1}: " + string.Join(" ", Enumerable.Repeat("this text is intentionally long to check that the inspector wraps it and shows the whole value.", 1 + i % 4)),
                    Param01 = (i * 1 + 7) % 1000,
                    Param02 = (i * 2 + 14) % 1000,
                    Param03 = (i * 3 + 21) % 1000,
                    Param04 = (i * 4 + 28) % 1000,
                    Param05 = (i * 5 + 35) % 1000,
                    Param06 = (i * 6 + 42) % 1000,
                    Param07 = (i * 7 + 49) % 1000,
                    Param08 = (i * 8 + 56) % 1000,
                    Param09 = (i * 9 + 63) % 1000,
                    Param10 = (i * 10 + 70) % 1000,
                    Param11 = (i * 11 + 77) % 1000,
                    Param12 = (i * 12 + 84) % 1000,
                    Param13 = (i * 13 + 91) % 1000,
                    Param14 = (i * 14 + 98) % 1000,
                    Param15 = (i * 15 + 105) % 1000,
                    Param16 = (i * 16 + 112) % 1000,
                    Param17 = (i * 17 + 119) % 1000,
                    Param18 = (i * 18 + 126) % 1000,
                    Param19 = (i * 19 + 133) % 1000,
                    Param20 = (i * 20 + 140) % 1000,
                    Param21 = (i * 21 + 147) % 1000,
                    Param22 = (i * 22 + 154) % 1000,
                    Param23 = (i * 23 + 161) % 1000,
                    Param24 = (i * 24 + 168) % 1000,
                    Param25 = (i * 25 + 175) % 1000,
                    Param26 = (i * 26 + 182) % 1000,
                    Param27 = (i * 27 + 189) % 1000,
                    Param28 = (i * 28 + 196) % 1000,
                    Param29 = (i * 29 + 203) % 1000,
                    Param30 = (i * 30 + 210) % 1000,
                    Rate01 = (i % 4) * 0.25f,
                    Rate02 = (i % 5) * 0.25f,
                    Rate03 = (i % 6) * 0.25f,
                    Rate04 = (i % 7) * 0.25f,
                    Rate05 = (i % 8) * 0.25f,
                    Rate06 = (i % 9) * 0.25f,
                    Rate07 = (i % 10) * 0.25f,
                    Rate08 = (i % 11) * 0.25f,
                    Rate09 = (i % 12) * 0.25f,
                    Rate10 = (i % 13) * 0.25f,
                    Rate11 = (i % 14) * 0.25f,
                    Rate12 = (i % 15) * 0.25f,
                    Rate13 = (i % 16) * 0.25f,
                    Rate14 = (i % 17) * 0.25f,
                    Rate15 = (i % 18) * 0.25f,
                    Flag01 = (i + 1) % 3 == 0,
                    Flag02 = (i + 2) % 3 == 0,
                    Flag03 = (i + 3) % 3 == 0,
                    Flag04 = (i + 4) % 3 == 0,
                    Flag05 = (i + 5) % 3 == 0,
                    Flag06 = (i + 6) % 3 == 0,
                    Flag07 = (i + 7) % 3 == 0,
                    Flag08 = (i + 8) % 3 == 0,
                    Flag09 = (i + 9) % 3 == 0,
                    Flag10 = (i + 10) % 3 == 0,
                    Note01 = $"note 1 of row {i + 1}",
                    Note02 = $"note 2 of row {i + 1}",
                    Note03 = $"note 3 of row {i + 1}",
                    Note04 = $"note 4 of row {i + 1}",
                    Note05 = $"note 5 of row {i + 1}",
                    Note06 = $"note 6 of row {i + 1}",
                    Note07 = $"note 7 of row {i + 1}",
                    Note08 = $"note 8 of row {i + 1}",
                    Note09 = $"note 9 of row {i + 1}",
                    Note10 = $"note 10 of row {i + 1}",
                    TotalScoreAcrossAllSeasons = 1_000_000_000L + i * 123_456_789L,
                    Ratio = i / 7.0,
                    Element = (ExampleElement)(i % 4),
                    Rarity = (ExampleRarity)(i % 4),
                    OptionalLimit = i % 3 == 0 ? (int?)null : i * 10,
                    ValueList = Enumerable.Range(i % 5, i % 4).ToArray(),
                };
            }
            return rows;
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
