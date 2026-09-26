using System.Linq;
using System.Text;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Samples.BasicExample
{
    /// <summary>
    /// Add this component to a GameObject in any scene and enter Play Mode, then press F8.
    /// Every time an override changes, the values seen through <see cref="ExampleMasterDataService"/> are logged.
    /// </summary>
    public sealed class ExampleDebuggerLauncher : MonoBehaviour
    {
        [Tooltip("Open the debugger on start (useful on devices without a keyboard).")]
        [SerializeField] bool openOnStart = false;

        [Tooltip("Rebuild the gameplay database with ImmutableBuilder whenever overrides change (optional pattern).")]
        [SerializeField] bool rebuildDatabaseOnOverride = false;

        ExampleMasterDataService service;
        ExampleDatabaseRebuilder rebuilder;

        public ExampleMasterDataService Service => service;

        void Awake()
        {
            var database = ExampleDatabaseBootstrap.Load();
            service = new ExampleMasterDataService(database);
            ExampleDebugRegistration.Register();
            if (rebuildDatabaseOnOverride) rebuilder = new ExampleDatabaseRebuilder(service);
            MasterMemoryDebugRuntime.OverridesChanged += LogCurrentValues;
        }

        void Start()
        {
            LogCurrentValues();
            if (openOnStart) RuntimeMasterMemoryDebugger.Open();
        }

        void OnDestroy()
        {
            MasterMemoryDebugRuntime.OverridesChanged -= LogCurrentValues;
            rebuilder?.Dispose();
        }

        /// <summary>Hook this to a button of an existing debug menu (mobile).</summary>
        public void ToggleDebugger()
        {
            RuntimeMasterMemoryDebugger.Toggle();
        }

        /// <summary>
        /// Logs every overridden record as gameplay sees it through the service: only the changed fields,
        /// "original → value read through the service" (colored in the Editor Console).
        /// </summary>
        void LogCurrentValues()
        {
            var richText = MasterDataDiffUtility.UseRichText;
            var sb = new StringBuilder();
            var overrides = MasterMemoryDebugRuntime.GetAllOverrides();
            sb.Append(overrides.Count == 0
                ? "[Example] No overrides: every value read through ExampleMasterDataService comes from MasterMemory."
                : $"[Example] {overrides.Count} overridden records, read through ExampleMasterDataService:");

            foreach (var entry in overrides)
            {
                if (!MasterMemoryDebugRegistry.TryGetTable(entry.Key.RecordType, out var table)) continue;
                if (!table.TryFindOriginal(entry.Key.PrimaryKey, out var original)) continue;

                var current = ReadThroughService(entry);
                var title = $"{table.TableName} {MasterDataValueUtility.FormatKey(entry.Key.PrimaryKey)} {table.GetDisplayName(current)}";
                sb.Append('\n').Append(MasterDataDiffUtility.Format(title, MasterDataDiffUtility.GetChanges(original, current), richText));
            }

            var category1 = service.GetSkillsByCategory(1);
            sb.Append($"\nFindByCategory(1) max Damage={category1.Max(x => x.Damage)} ");
            sb.Append(rebuildDatabaseOnOverride
                ? "(rebuilt database: secondary index sees overrides)"
                : "(secondary index: does not see overrides)");
            Debug.Log(sb.ToString());
        }

        /// <summary>Reads the record the way gameplay does; tables without a getter in the service use the override itself.</summary>
        object ReadThroughService(MasterDataOverrideEntry entry)
        {
            switch (entry.Value)
            {
                case ExampleSkillMaster _:
                    return service.GetSkill((int)entry.Key.PrimaryKey);
                case ExampleItemMaster _:
                    return service.GetItem((int)entry.Key.PrimaryKey);
                case ExampleEnemyLevelMaster _:
                    var (enemyId, level) = ((int, int))entry.Key.PrimaryKey;
                    return service.GetEnemyLevel(enemyId, level);
                default:
                    return entry.Value;
            }
        }
    }
}
