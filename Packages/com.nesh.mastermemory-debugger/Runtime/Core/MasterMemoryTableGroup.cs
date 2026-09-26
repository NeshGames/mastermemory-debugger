using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A named group of tables shown as a folder in the debugger's table list.</summary>
    public sealed class MasterMemoryTableGroup
    {
        internal MasterMemoryTableGroup(string name, bool isUngrouped)
        {
            Name = name;
            IsUngrouped = isUngrouped;
        }

        public string Name { get; }

        /// <summary>True for the group that collects tables without an assigned group.</summary>
        public bool IsUngrouped { get; }

        public List<MasterMemoryTableDescriptor> Tables { get; } = new List<MasterMemoryTableDescriptor>();
    }
}
