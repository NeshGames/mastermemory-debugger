using MasterMemory;

namespace Nesh.MasterMemoryDebugger.Samples.BasicExample
{
    /// <summary>
    /// The project's master data access layer. Every primary key lookup asks the debugger first.
    /// In non-development builds TryGetOverride always returns false, so no #if is needed here.
    /// </summary>
    public sealed class ExampleMasterDataService
    {
        public ExampleMasterDataService(MemoryDatabase database)
        {
            Database = database;
        }

        /// <summary>
        /// The database used by gameplay. Usually the original database; <see cref="MasterMemoryDebugRebuild.AutoRebuild{TDatabase}"/>
        /// replaces it with a database rebuilt from the overrides (see ExampleDebuggerLauncher).
        /// </summary>
        public MemoryDatabase Database { get; set; }

        public ExampleSkillMaster GetSkill(int id)
        {
            if (MasterMemoryDebugRuntime.TryGetOverride<ExampleSkillMaster, int>(id, out var value))
            {
                return value;
            }
            return Database.ExampleSkillMasterTable.FindById(id);
        }

        public ExampleItemMaster GetItem(int id)
        {
            // same as above with the helper
            return MasterMemoryDebugRuntime.Resolve<ExampleItemMaster, int>(id, key => Database.ExampleItemMasterTable.FindById(key));
        }

        public ExampleEnemyLevelMaster GetEnemyLevel(int enemyId, int level)
        {
            // composite keys are ValueTuples, exactly like FindByEnemyIdAndLevel
            return MasterMemoryDebugRuntime.Resolve<ExampleEnemyLevelMaster, (int, int)>(
                (enemyId, level),
                key => Database.ExampleEnemyLevelMasterTable.FindByEnemyIdAndLevel(key));
        }

        /// <summary>
        /// Secondary key / range queries read the MasterMemory indexes directly and do NOT see overrides,
        /// unless the database was rebuilt with <see cref="MasterMemoryDebugRebuild"/>.
        /// </summary>
        public RangeView<ExampleSkillMaster> GetSkillsByCategory(int category)
        {
            return Database.ExampleSkillMasterTable.FindByCategory(category);
        }
    }
}
