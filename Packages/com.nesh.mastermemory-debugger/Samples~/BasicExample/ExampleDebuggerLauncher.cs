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

        /// <summary>Logs every overridden record as gameplay sees it through the service, next to the original value.</summary>
        void LogCurrentValues()
        {
            var original = ExampleDatabaseBootstrap.OriginalDatabase;
            var sb = new StringBuilder("[Example] Values read through ExampleMasterDataService");
            var overrides = MasterMemoryDebugRuntime.GetAllOverrides();
            if (overrides.Count == 0) sb.Append(": no overrides, every value comes from MasterMemory.");

            foreach (var entry in overrides)
            {
                switch (entry.Value)
                {
                    case ExampleSkillMaster _:
                    {
                        var id = (int)entry.Key.PrimaryKey;
                        var skill = service.GetSkill(id);
                        var source = original.ExampleSkillMasterTable.FindById(id);
                        sb.Append($"\n  Skill {id} {skill.Name}: Damage={skill.Damage} (original {source.Damage}), Cooldown={skill.Cooldown} (original {source.Cooldown}), Element={skill.Element}");
                        break;
                    }
                    case ExampleItemMaster _:
                    {
                        var id = (int)entry.Key.PrimaryKey;
                        var item = service.GetItem(id);
                        var source = original.ExampleItemMasterTable.FindById(id);
                        sb.Append($"\n  Item {id} {item.Name}: Price={item.Price} (original {source.Price}), Rarity={item.Rarity}, DropRate={item.DropRate}");
                        break;
                    }
                    case ExampleEnemyLevelMaster _:
                    {
                        var (enemyId, level) = ((int, int))entry.Key.PrimaryKey;
                        var enemy = service.GetEnemyLevel(enemyId, level);
                        var source = original.ExampleEnemyLevelMasterTable.FindByEnemyIdAndLevel((enemyId, level));
                        sb.Append($"\n  Enemy ({enemyId}, {level}): Hp={enemy.Hp} (original {source.Hp}), Attack={enemy.Attack}, MoveSpeed={enemy.MoveSpeed}");
                        break;
                    }
                }
            }

            var category1 = service.GetSkillsByCategory(1);
            sb.Append($"\n  FindByCategory(1) max Damage={category1.Max(x => x.Damage)} ");
            sb.Append(rebuildDatabaseOnOverride
                ? "(rebuilt database: secondary index sees overrides)"
                : "(secondary index: does not see overrides)");
            Debug.Log(sb.ToString());
        }
    }
}
