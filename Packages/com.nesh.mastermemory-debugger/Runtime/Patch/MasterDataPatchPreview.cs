using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterDataPatchPreviewStatus
    {
        Ready,
        VersionMismatch,
        SchemaMismatch,
        UnsupportedFormat,
        Invalid,
        Disabled,
    }

    /// <summary>Non-mutating impact summary produced by the same transaction planner used by Patch apply.</summary>
    public sealed class MasterDataPatchPreviewResult
    {
        public MasterDataPatchPreviewStatus Status;
        public string PatchMasterVersion;
        public string CurrentMasterVersion;
        public string PatchSchemaHash;
        public string CurrentSchemaHash;
        public int TargetRecords;
        public int ChangedRecords;
        public int AddedRecords;
        public int DeletedRecords;
        public int ResetRecords;
        public int Fields;
        public int RemovedExistingOverrides;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool Succeeded => Status == MasterDataPatchPreviewStatus.Ready;
    }
}
