using System.Linq;
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

        void LogCurrentValues()
        {
            var fireball = service.GetSkill(1001);
            var potion = service.GetItem(1);
            var enemy = service.GetEnemyLevel(1, 2);
            var category1 = service.GetSkillsByCategory(1);
            Debug.Log(
                "[Example] via MasterDataService: " +
                $"Skill 1001 {fireball.Name} Damage={fireball.Damage} Cooldown={fireball.Cooldown}, " +
                $"Item 1 {potion.Name} Price={potion.Price}, " +
                $"Enemy (1,2) Hp={enemy.Hp}. " +
                $"FindByCategory(1) max damage={category1.Max(x => x.Damage)} " +
                (rebuildDatabaseOnOverride ? "(rebuilt database: sees overrides)" : "(secondary index: does not see overrides)"));
        }
    }
}
