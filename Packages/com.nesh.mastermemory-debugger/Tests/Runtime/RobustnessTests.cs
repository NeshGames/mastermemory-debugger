using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class RobustnessTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
        }

        [Test]
        public void PatchJson_RandomizedRoundTrip_ShouldBeStable()
        {
            var random = new Random(0x5EED1234);

            for (var iteration = 0; iteration < 100; iteration++)
            {
                var patch = new MasterDataPatch
                {
                    FormatVersion = MasterDataPatch.CurrentFormatVersion,
                    MasterVersion = RandomText(random, 12),
                    SchemaHash = RandomText(random, 16),
                    ExportedAt = "2026-09-28T00:00:00Z",
                };
                var table = new MasterDataPatchTable
                {
                    TableName = "Table_" + iteration,
                    MemoryTableName = "table_" + iteration,
                    RecordType = "Tests.Record" + iteration,
                };
                patch.Tables.Add(table);

                var recordCount = 1 + random.Next(4);
                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    var record = new MasterDataPatchRecord
                    {
                        PrimaryKey = new MasterDataJsonObject { { "Id", random.Next(1, 100000) } },
                    };
                    var changeCount = 1 + random.Next(5);
                    for (var changeIndex = 0; changeIndex < changeCount; changeIndex++)
                    {
                        record.Changes.Add(new MasterDataPatchChange
                        {
                            Field = "Field_" + changeIndex,
                            Original = RandomJsonValue(random),
                            Value = RandomJsonValue(random),
                            HasOriginal = random.Next(4) != 0,
                        });
                    }
                    table.Records.Add(record);
                }

                var first = MasterDataPatchSerializer.ToJson(patch, pretty: false);
                var parsed = MasterDataPatchSerializer.FromJson(first);
                var second = MasterDataPatchSerializer.ToJson(parsed, pretty: false);

                Assert.AreEqual(first, second,
                    "Patch JSON must have a stable parse/serialize round trip at iteration " + iteration);
            }
        }

        [Test]
        public void QueryParser_RandomizedInput_ShouldNeverThrowOrWidenMalformedFilters()
        {
            var random = new Random(0x51A2B3C);
            var type = MasterDataReflectionCache.Get<TestSkill>();
            var records = Table<TestSkill>().CreateRecordSnapshot();
            const string alphabet = "abcXYZ0123_ ()&|=!<>~-+.,";

            for (var iteration = 0; iteration < 1000; iteration++)
            {
                var length = random.Next(0, 96);
                var chars = new char[length];
                for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
                var input = new string(chars);

                MasterRecordQuery query = null;
                Assert.DoesNotThrow(() => query = MasterRecordQuery.Parse(input, type),
                    "Parser threw for randomized input: " + input);

                foreach (var record in records)
                {
                    Assert.DoesNotThrow(() => query.Matches(record),
                        "Matcher threw for randomized input: " + input);
                }

                if (query.HasBooleanSyntax && query.Errors.Count > 0)
                {
                    foreach (var record in records)
                    {
                        Assert.IsFalse(query.Matches(record),
                            "Malformed boolean filters must fail closed: " + input);
                    }
                }
            }
        }

        [Test]
        public void RemoteFrames_RandomizedPayloads_ShouldRoundTripExactly()
        {
            var random = new Random(0x7A6B5C4D);

            for (var iteration = 0; iteration < 250; iteration++)
            {
                var payload = new byte[1 + random.Next(8192)];
                random.NextBytes(payload);

                using var stream = new MemoryStream();
                MasterMemoryRemoteProtocol.WriteFrame(stream, payload);
                stream.Position = 0;

                var decoded = MasterMemoryRemoteProtocol.ReadFrame(stream);

                CollectionAssert.AreEqual(payload, decoded,
                    "Frame payload changed at iteration " + iteration);
                Assert.AreEqual(stream.Length, stream.Position,
                    "Frame reader must consume exactly one frame");
            }
        }

        static object RandomJsonValue(Random random)
        {
            switch (random.Next(6))
            {
                case 0: return null;
                case 1: return random.Next(-1000000, 1000000);
                case 2: return random.Next(2) == 0;
                case 3: return RandomText(random, 24);
                case 4:
                    return new List<object>
                    {
                        random.Next(-100, 100),
                        RandomText(random, 8),
                        random.Next(2) == 0,
                    };
                default:
                    return new MasterDataJsonObject
                    {
                        { "number", random.Next(-100, 100) },
                        { "text", RandomText(random, 8) },
                    };
            }
        }

        static string RandomText(Random random, int maxLength)
        {
            const string alphabet = "abcXYZ0123 _-中文日本語";
            var length = random.Next(maxLength + 1);
            var chars = new char[length];
            for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
            return new string(chars);
        }
    }
}
