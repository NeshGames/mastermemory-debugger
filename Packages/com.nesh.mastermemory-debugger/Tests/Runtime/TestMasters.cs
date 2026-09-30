using System;
using System.Collections;
using System.Globalization;
using MasterMemory;
using MessagePack;
using MessagePack.Formatters;
using Nesh.MasterMemoryDebugger.Tests.Generated;
using Nesh.MasterMemoryDebugger.Tests.Generated.Tables;

// MasterMemory generates MemoryDatabase / tables for the records of this assembly into this namespace.
// CS0436: the generator also emits this internal attribute into the package assemblies, which are visible
// through InternalsVisibleTo; the local definition is the one used.
#pragma warning disable CS0436
[assembly: MasterMemoryGeneratorOptions(Namespace = "Nesh.MasterMemoryDebugger.Tests.Generated")]
#pragma warning restore CS0436

namespace System.Runtime.CompilerServices
{
    // enables `init` on Unity's .NET Standard profile
    internal sealed class IsExternalInit
    {
    }
}

namespace Nesh.MasterMemoryDebugger.Tests
{
    public enum TestElement
    {
        None,
        Fire,
        Ice,
    }

    [Flags]
    public enum TestFlags
    {
        None = 0,
        Boss = 1,
        Flying = 2,
    }

    [MemoryTable("test_skill")]
    public sealed record TestSkill : IValidatable<TestSkill>
    {
        [PrimaryKey]
        public int Id { get; init; }

        [SecondaryKey(0), NonUnique]
        public int Category { get; init; }

        public string Name { get; init; }
        public int Damage { get; init; }
        public float Cooldown { get; init; }
        public TestElement Element { get; init; }
        public bool IsPassive { get; init; }
        public int? UnlockLevel { get; init; }
        public ulong BigValue { get; init; }
        public int[] Tags { get; init; }

        /// <summary>0 = none; otherwise an EnemyId of <see cref="TestEnemyLevel"/>.</summary>
        public int SummonEnemyId { get; init; }

        void IValidatable<TestSkill>.Validate(IValidator<TestSkill> validator)
        {
            if (SummonEnemyId != 0)
            {
                validator.GetReferenceSet<TestEnemyLevel>().Exists(x => x.SummonEnemyId, y => y.EnemyId);
            }
            validator.Validate(x => x.Damage >= 0);
        }
    }

    /// <summary>Composite primary key.</summary>
    [MemoryTable("test_enemy_level")]
    public sealed record TestEnemyLevel
    {
        [PrimaryKey(0)]
        public int EnemyId { get; init; }

        [PrimaryKey(1)]
        public int Level { get; init; }

        public int Hp { get; init; }
        public TestFlags Flags { get; init; }
    }

    /// <summary>Plain class without MasterMemory attributes, registered manually.</summary>
    public sealed class ManualItem
    {
        public ManualItem(int code, string title, int price)
        {
            Code = code;
            Title = title;
            Price = price;
        }

        public int Code { get; }
        public string Title { get; }
        public int Price { get; private set; }
    }

    /// <summary>
    /// Fixed-point number in thousandths with a private raw, like a game's Fix64: no writable member, no ToString, not
    /// comparable, so only <see cref="TestFixedConverter"/> makes it editable.
    /// </summary>
    public readonly struct TestFixed
    {
        readonly long raw;

        TestFixed(long raw)
        {
            this.raw = raw;
        }

        public static TestFixed FromRaw(long raw) => new TestFixed(raw);

        public long Raw => raw;
    }

    /// <summary>Canonical decimal text with at most 3 decimals; values are ordered by their raw.</summary>
    public sealed class TestFixedConverter : MasterDataValueConverter<TestFixed>, IComparer
    {
        const decimal Scale = 1000m;

        public override string Format(TestFixed value) => (value.Raw / Scale).ToString("0.###", CultureInfo.InvariantCulture);

        public override bool TryParse(string text, out TestFixed value, out string error)
        {
            value = default;
            if (!decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                error = $"'{text}' is not a number";
                return false;
            }
            var scaled = number * Scale;
            if (scaled != decimal.Truncate(scaled) || scaled < long.MinValue || scaled > long.MaxValue)
            {
                error = $"'{text}' is not a TestFixed (3 decimals)";
                return false;
            }
            value = TestFixed.FromRaw((long)scaled);
            error = null;
            return true;
        }

        public int Compare(object x, object y) => ((TestFixed)x).Raw.CompareTo(((TestFixed)y).Raw);
    }

    /// <summary>Writes the raw, as a game's resolver does for its fixed-point type.</summary>
    public sealed class TestFixedFormatter : IMessagePackFormatter<TestFixed>
    {
        public void Serialize(ref MessagePackWriter writer, TestFixed value, MessagePackSerializerOptions options) => writer.Write(value.Raw);

        public TestFixed Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => TestFixed.FromRaw(reader.ReadInt64());
    }

    /// <summary>A converter whose conversions are never called: for the registration rules.</summary>
    public sealed class UnusedConverter : IMasterDataValueConverter
    {
        public UnusedConverter(Type valueType)
        {
            ValueType = valueType;
        }

        public Type ValueType { get; }
        public string Format(object value) => throw new NotSupportedException();
        public bool TryParse(string text, out object value, out string error) => throw new NotSupportedException();
        public object ToJson(object value) => throw new NotSupportedException();
        public object FromJson(object json) => throw new NotSupportedException();
    }

    /// <summary>Registered manually (<see cref="DebuggerTestBase"/>): members of a type with a converter.</summary>
    public sealed record TestTuning
    {
        [PrimaryKey]
        public int Id { get; init; }

        public string Name { get; init; }
        public TestFixed Speed { get; init; }
        public TestFixed? Limit { get; init; }
        public TestFixed[] Curve { get; init; }
    }

    public static class TestData
    {
        static TestFixed Fixed(long raw) => TestFixed.FromRaw(raw);

        public static TestTuning[] CreateTunings() => new[]
        {
            new TestTuning { Id = 1, Name = "Walk", Speed = Fixed(2500), Curve = new[] { Fixed(500), Fixed(1000) } },
            new TestTuning { Id = 2, Name = "Run", Speed = Fixed(10000), Limit = Fixed(12250), Curve = new[] { Fixed(1000), Fixed(2500) } },
            new TestTuning { Id = 3, Name = "Back", Speed = Fixed(-1000), Curve = new TestFixed[0] },
        };

        public static MemoryDatabase CreateDatabase()
        {
            // tables expect data sorted by primary key
            var skills = new[]
            {
                new TestSkill { Id = 1001, Category = 1, Name = "Fireball", Damage = 120, Cooldown = 2.5f, Element = TestElement.Fire, UnlockLevel = 3, BigValue = ulong.MaxValue - 1, Tags = new[] { 1, 2 }, SummonEnemyId = 2 },
                new TestSkill { Id = 1002, Category = 1, Name = "Ice Blast", Damage = 100, Cooldown = 3f, Element = TestElement.Ice },
                new TestSkill { Id = 1003, Category = 2, Name = "Heal", Damage = 0, Cooldown = 5f, IsPassive = true },
            };
            var enemyLevels = new[]
            {
                new TestEnemyLevel { EnemyId = 1, Level = 1, Hp = 100 },
                new TestEnemyLevel { EnemyId = 1, Level = 2, Hp = 150 },
                new TestEnemyLevel { EnemyId = 2, Level = 1, Hp = 400, Flags = TestFlags.Boss },
            };
            return new MemoryDatabase(
                TestEnemyLevelTable: new TestEnemyLevelTable(enemyLevels),
                TestSkillTable: new TestSkillTable(skills));
        }
    }
}
