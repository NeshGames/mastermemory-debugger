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

        // complex members are shown read-only
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
}
