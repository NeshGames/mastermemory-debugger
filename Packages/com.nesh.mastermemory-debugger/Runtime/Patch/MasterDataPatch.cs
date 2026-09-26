using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Field level patch: only changed, editable fields of overridden records together with their original values.
    /// Designed both to restore overrides after a restart and to be handed to whoever maintains the master data source.
    /// <code>
    /// {
    ///   "formatVersion": 1,
    ///   "masterVersion": "2026.09.26.001",
    ///   "exportedAt": "2026-09-26T14:00:00Z",
    ///   "tables": [{
    ///     "tableName": "SkillMaster", "memoryTableName": "skill", "recordType": "MyGame.SkillMaster",
    ///     "records": [{
    ///       "primaryKey": { "Id": 1001 },
    ///       "changes": [{ "field": "Damage", "original": 120, "value": 185 }]
    ///     }]
    ///   }]
    /// }
    /// </code>
    /// </summary>
    public sealed class MasterDataPatch
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion = CurrentFormatVersion;
        public string MasterVersion;

        /// <summary>UTC time in ISO 8601 format.</summary>
        public string ExportedAt;

        public List<MasterDataPatchTable> Tables = new List<MasterDataPatchTable>();

        public int RecordCount
        {
            get
            {
                var count = 0;
                foreach (var table in Tables) count += table.Records.Count;
                return count;
            }
        }
    }

    public sealed class MasterDataPatchTable
    {
        /// <summary>Registered table name (for example "SkillMaster").</summary>
        public string TableName;

        /// <summary>Name given to [MemoryTable] (for example "skill"). May be null.</summary>
        public string MemoryTableName;

        /// <summary>Full name of the record type.</summary>
        public string RecordType;

        public List<MasterDataPatchRecord> Records = new List<MasterDataPatchRecord>();
    }

    public sealed class MasterDataPatchRecord
    {
        /// <summary>Primary key member name → value. Composite keys have several entries.</summary>
        public MasterDataJsonObject PrimaryKey = new MasterDataJsonObject();

        public List<MasterDataPatchChange> Changes = new List<MasterDataPatchChange>();
    }

    public sealed class MasterDataPatchChange
    {
        public string Field;

        /// <summary>Original value as a JSON value (see <see cref="MasterDataJson"/>).</summary>
        public object Original;

        /// <summary>Overridden value as a JSON value (see <see cref="MasterDataJson"/>).</summary>
        public object Value;

        /// <summary>False when the patch did not contain an "original" entry (hand written patches).</summary>
        public bool HasOriginal = true;
    }
}
