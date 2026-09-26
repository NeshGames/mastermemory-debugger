using MasterMemory;
using MessagePack;

// Each assembly gets its own generated MemoryDatabase. Give the sample its own namespace so it never
// collides with the project's database.
[assembly: MasterMemoryGeneratorOptions(Namespace = "Nesh.MasterMemoryDebugger.Samples.BasicExample")]

namespace System.Runtime.CompilerServices
{
    // enables `init` on Unity's .NET Standard profile
    internal sealed class IsExternalInit
    {
    }
}

namespace Nesh.MasterMemoryDebugger.Samples.BasicExample
{
    public enum ExampleElement
    {
        None,
        Fire,
        Ice,
        Thunder,
    }

    public enum ExampleRarity
    {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    [MemoryTable("example_skill"), MessagePackObject(true)]
    public sealed record ExampleSkillMaster
    {
        [PrimaryKey]
        public int Id { get; init; }

        // read-only in the debugger: MasterMemory secondary indexes are not updated by overrides
        [SecondaryKey(0), NonUnique]
        public int Category { get; init; }

        public string Name { get; init; }
        public int Damage { get; init; }
        public float Cooldown { get; init; }
        public ExampleElement Element { get; init; }
        public bool IsPassive { get; init; }
        public int? UnlockLevel { get; init; }

        // arrays / Lists of simple values are edited element by element (a new array is created on every change)
        public int[] EffectIds { get; init; }
    }

    [MemoryTable("example_item"), MessagePackObject(true)]
    public sealed record ExampleItemMaster
    {
        [PrimaryKey]
        public int Id { get; init; }

        public string Name { get; init; }
        public long Price { get; init; }
        public ExampleRarity Rarity { get; init; }
        public bool Stackable { get; init; }
        public double DropRate { get; init; }
    }

    /// <summary>
    /// MasterMemory validation (IValidatable). The debugger reads the Exists() calls to offer a jump from
    /// StartSkillId to the skill, and AutoRebuild reports overrides that break them (e.g. StartSkillId = 99).
    /// </summary>
    [MemoryTable("example_character"), MessagePackObject(true)]
    public sealed record ExampleCharacterMaster : IValidatable<ExampleCharacterMaster>
    {
        [PrimaryKey]
        public int Id { get; init; }

        public string Name { get; init; }
        public int Hp { get; init; }
        public int Attack { get; init; }
        public int Defense { get; init; }
        public float MoveSpeed { get; init; }
        public ExampleElement Element { get; init; }
        public int StartSkillId { get; init; }

        void IValidatable<ExampleCharacterMaster>.Validate(IValidator<ExampleCharacterMaster> validator)
        {
            validator.GetReferenceSet<ExampleSkillMaster>().Exists(x => x.StartSkillId, skill => skill.Id);
            validator.Validate(x => x.Hp > 0);
        }
    }

    [MemoryTable("example_effect"), MessagePackObject(true)]
    public sealed record ExampleEffectMaster
    {
        [PrimaryKey]
        public int Id { get; init; }

        public string Name { get; init; }
        public float Duration { get; init; }
        public int Value { get; init; }
        public bool IsDebuff { get; init; }
    }

    [MemoryTable("example_shop"), MessagePackObject(true)]
    public sealed record ExampleShopMaster : IValidatable<ExampleShopMaster>
    {
        [PrimaryKey]
        public int Id { get; init; }

        [SecondaryKey(0), NonUnique]
        public int ItemId { get; init; }

        public long Price { get; init; }
        public int Stock { get; init; }

        void IValidatable<ExampleShopMaster>.Validate(IValidator<ExampleShopMaster> validator)
        {
            validator.GetReferenceSet<ExampleItemMaster>().Exists(x => x.ItemId, item => item.Id);
        }
    }

    /// <summary>String primary key example. Left without a group, so it is listed under "Other".</summary>
    [MemoryTable("example_game_config"), MessagePackObject(true)]
    public sealed record ExampleGameConfigMaster
    {
        [PrimaryKey]
        public string Key { get; init; }

        public string Value { get; init; }
    }

    /// <summary>Composite primary key example: FindByEnemyIdAndLevel((enemyId, level)).</summary>
    [MemoryTable("example_enemy_level"), MessagePackObject(true)]
    public sealed record ExampleEnemyLevelMaster
    {
        [PrimaryKey(0)]
        public int EnemyId { get; init; }

        [PrimaryKey(1)]
        public int Level { get; init; }

        public int Hp { get; init; }
        public int Attack { get; init; }
        public float MoveSpeed { get; init; }
    }

    /// <summary>
    /// Wide table (27 columns, 300 records) to check horizontal scrolling, frozen columns and the Columns popup.
    /// </summary>
    [MemoryTable("example_weapon"), MessagePackObject(true)]
    public sealed record ExampleWeaponMaster : IValidatable<ExampleWeaponMaster>
    {
        [PrimaryKey]
        public int Id { get; init; }

        public string Name { get; init; }
        public ExampleRarity Rarity { get; init; }
        public ExampleElement Element { get; init; }
        public int RequiredLevel { get; init; }
        public int Attack { get; init; }
        public int MagicAttack { get; init; }
        public int Defense { get; init; }
        public float CriticalRate { get; init; }
        public float CriticalDamage { get; init; }
        public float AttackSpeed { get; init; }
        public float Range { get; init; }
        public float Weight { get; init; }
        public int Durability { get; init; }
        public int MaxUpgrade { get; init; }
        public long Price { get; init; }
        public long SellPrice { get; init; }
        public bool Tradable { get; init; }
        public bool Upgradable { get; init; }
        public int SkillId { get; init; }
        public int? SetId { get; init; }
        public string IconPath { get; init; }
        public string ModelPath { get; init; }
        public string Description { get; init; }
        public int[] SocketTypes { get; init; }
        public double DropRate { get; init; }
        public string ReleaseVersion { get; init; }

        void IValidatable<ExampleWeaponMaster>.Validate(IValidator<ExampleWeaponMaster> validator)
        {
            if (SkillId != 0) validator.GetReferenceSet<ExampleSkillMaster>().Exists(x => x.SkillId, skill => skill.Id);
        }
    }

    /// <summary>
    /// Very wide table (75 columns, 120 records) to check horizontal scrolling beyond one screen, frozen columns,
    /// automatic column widths (long names and values) and text wrapping in the inspector.
    /// </summary>
    [MemoryTable("example_many_columns"), MessagePackObject(true)]
    public sealed record ExampleManyColumnsMaster
    {
        [PrimaryKey]
        public int Id { get; init; }
        public string Name { get; init; }
        public string VeryLongFieldNameToCheckAutomaticColumnWidth { get; init; }
        public string LongDescription { get; init; }
        public int Param01 { get; init; }
        public int Param02 { get; init; }
        public int Param03 { get; init; }
        public int Param04 { get; init; }
        public int Param05 { get; init; }
        public int Param06 { get; init; }
        public int Param07 { get; init; }
        public int Param08 { get; init; }
        public int Param09 { get; init; }
        public int Param10 { get; init; }
        public int Param11 { get; init; }
        public int Param12 { get; init; }
        public int Param13 { get; init; }
        public int Param14 { get; init; }
        public int Param15 { get; init; }
        public int Param16 { get; init; }
        public int Param17 { get; init; }
        public int Param18 { get; init; }
        public int Param19 { get; init; }
        public int Param20 { get; init; }
        public int Param21 { get; init; }
        public int Param22 { get; init; }
        public int Param23 { get; init; }
        public int Param24 { get; init; }
        public int Param25 { get; init; }
        public int Param26 { get; init; }
        public int Param27 { get; init; }
        public int Param28 { get; init; }
        public int Param29 { get; init; }
        public int Param30 { get; init; }
        public float Rate01 { get; init; }
        public float Rate02 { get; init; }
        public float Rate03 { get; init; }
        public float Rate04 { get; init; }
        public float Rate05 { get; init; }
        public float Rate06 { get; init; }
        public float Rate07 { get; init; }
        public float Rate08 { get; init; }
        public float Rate09 { get; init; }
        public float Rate10 { get; init; }
        public float Rate11 { get; init; }
        public float Rate12 { get; init; }
        public float Rate13 { get; init; }
        public float Rate14 { get; init; }
        public float Rate15 { get; init; }
        public bool Flag01 { get; init; }
        public bool Flag02 { get; init; }
        public bool Flag03 { get; init; }
        public bool Flag04 { get; init; }
        public bool Flag05 { get; init; }
        public bool Flag06 { get; init; }
        public bool Flag07 { get; init; }
        public bool Flag08 { get; init; }
        public bool Flag09 { get; init; }
        public bool Flag10 { get; init; }
        public string Note01 { get; init; }
        public string Note02 { get; init; }
        public string Note03 { get; init; }
        public string Note04 { get; init; }
        public string Note05 { get; init; }
        public string Note06 { get; init; }
        public string Note07 { get; init; }
        public string Note08 { get; init; }
        public string Note09 { get; init; }
        public string Note10 { get; init; }
        public long TotalScoreAcrossAllSeasons { get; init; }
        public double Ratio { get; init; }
        public ExampleElement Element { get; init; }
        public ExampleRarity Rarity { get; init; }
        public int? OptionalLimit { get; init; }
        public int[] ValueList { get; init; }
    }
}
