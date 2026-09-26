using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Converts <see cref="MasterDataPatch"/> to / from JSON. No external JSON library is required.</summary>
    public static class MasterDataPatchSerializer
    {
        public static string ToJson(MasterDataPatch patch, bool pretty = true)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));

            var tables = new List<object>(patch.Tables.Count);
            foreach (var table in patch.Tables)
            {
                var records = new List<object>(table.Records.Count);
                foreach (var record in table.Records)
                {
                    var changes = new List<object>(record.Changes.Count);
                    foreach (var change in record.Changes)
                    {
                        changes.Add(new MasterDataJsonObject
                        {
                            { "field", change.Field },
                            { "original", change.Original },
                            { "value", change.Value },
                        });
                    }
                    records.Add(new MasterDataJsonObject
                    {
                        { "primaryKey", record.PrimaryKey ?? new MasterDataJsonObject() },
                        { "changes", changes },
                    });
                }
                tables.Add(new MasterDataJsonObject
                {
                    { "tableName", table.TableName },
                    { "memoryTableName", table.MemoryTableName },
                    { "recordType", table.RecordType },
                    { "records", records },
                });
            }

            var root = new MasterDataJsonObject
            {
                { "formatVersion", patch.FormatVersion },
                { "masterVersion", patch.MasterVersion },
                { "exportedAt", patch.ExportedAt },
                { "tables", tables },
            };
            return MasterDataJson.Serialize(root, pretty);
        }

        public static MasterDataPatch FromJson(string json)
        {
            var root = MasterDataJson.Parse(json) as MasterDataJsonObject
                ?? throw new FormatException("Patch root must be a JSON object.");

            var patch = new MasterDataPatch
            {
                FormatVersion = GetInt(root, "formatVersion"),
                MasterVersion = GetString(root, "masterVersion"),
                ExportedAt = GetString(root, "exportedAt"),
            };

            foreach (var tableJson in GetArray(root, "tables"))
            {
                var tableObject = AsObject(tableJson, "table");
                var table = new MasterDataPatchTable
                {
                    TableName = GetString(tableObject, "tableName"),
                    MemoryTableName = GetString(tableObject, "memoryTableName"),
                    RecordType = GetString(tableObject, "recordType"),
                };
                foreach (var recordJson in GetArray(tableObject, "records"))
                {
                    var recordObject = AsObject(recordJson, "record");
                    var record = new MasterDataPatchRecord
                    {
                        PrimaryKey = recordObject["primaryKey"] as MasterDataJsonObject
                            ?? throw new FormatException("Record 'primaryKey' must be a JSON object."),
                    };
                    foreach (var changeJson in GetArray(recordObject, "changes"))
                    {
                        var changeObject = AsObject(changeJson, "change");
                        record.Changes.Add(new MasterDataPatchChange
                        {
                            Field = GetString(changeObject, "field") ?? throw new FormatException("Change 'field' is required."),
                            Original = changeObject["original"],
                            Value = changeObject["value"],
                            HasOriginal = changeObject.ContainsKey("original"),
                        });
                    }
                    table.Records.Add(record);
                }
                patch.Tables.Add(table);
            }
            return patch;
        }

        static MasterDataJsonObject AsObject(object json, string name)
        {
            return json as MasterDataJsonObject ?? throw new FormatException($"Patch {name} must be a JSON object.");
        }

        static List<object> GetArray(MasterDataJsonObject obj, string key)
        {
            var value = obj[key];
            if (value == null) return new List<object>();
            return value as List<object> ?? throw new FormatException($"'{key}' must be a JSON array.");
        }

        static string GetString(MasterDataJsonObject obj, string key)
        {
            var value = obj[key];
            switch (value)
            {
                case null: return null;
                case string s: return s;
                case MasterDataJsonNumber n: return n.Raw;
                default: throw new FormatException($"'{key}' must be a string.");
            }
        }

        static int GetInt(MasterDataJsonObject obj, string key)
        {
            if (obj[key] is MasterDataJsonNumber n) return int.Parse(n.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
            throw new FormatException($"'{key}' must be an integer.");
        }
    }
}
