using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Translates debugger controls from per-language JSON catalogs. Table and field names are intentionally left as code names.
    /// The catalog assets live outside Resources and are copied into Development Builds with the rest of the debugger UI.
    /// </summary>
    internal sealed class MasterMemoryDebugUiLocalization
    {
        const string PrefsKey = "Nesh.MasterMemoryDebugger.UiLanguage";
        public static readonly string[] Languages = { "en", "zh-TW", "ja" };
        static string language;
        static readonly Dictionary<string, Catalog> catalogs = new Dictionary<string, Catalog>();

        [Serializable]
        sealed class CatalogAsset
        {
            public string language;
            public string displayName;
            public TextEntry[] strings;
            public PatternEntry[] patterns;
        }

        [Serializable]
        sealed class TextEntry
        {
            public string key;
            public string value;
        }

        [Serializable]
        sealed class PatternEntry
        {
            public string pattern;
            public string replacement;
        }

        sealed class Catalog
        {
            public string DisplayName;
            public Dictionary<string, string> Texts;
            public List<(Regex pattern, string replacement)> Patterns;
        }

        sealed class Original
        {
            public string Text;
            public string TranslatedText;
            public string Tooltip;
            public string TranslatedTooltip;
            public string Placeholder;
            public string TranslatedPlaceholder;
        }

        readonly ConditionalWeakTable<VisualElement, Original> originals = new ConditionalWeakTable<VisualElement, Original>();

        public static string Language
        {
            get
            {
                if (language == null) language = Normalize(PlayerPrefs.GetString(PrefsKey, "en"));
                return language;
            }
            set
            {
                language = Normalize(value);
                PlayerPrefs.SetString(PrefsKey, language);
                PlayerPrefs.Save();
            }
        }

        public static string DisplayName(string code) => GetCatalog(code)?.DisplayName ?? code;
        public static string Text(string english) => GetCatalog(Language) is Catalog catalog ? Translate(english, catalog) : english;

        static string Normalize(string code) => code == "zh-TW" || code == "ja" ? code : "en";

        static Catalog GetCatalog(string code)
        {
            code = Normalize(code);
            if (catalogs.TryGetValue(code, out var cached)) return cached;
            var asset = MasterMemoryDebuggerAssets.UiCatalog(code);
            if (asset == null)
            {
                Debug.LogWarning($"[MasterMemoryDebugger] UI translation JSON for {code} was not found.");
                return null;
            }
            CatalogAsset parsed;
            try { parsed = JsonUtility.FromJson<CatalogAsset>(asset.text); }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MasterMemoryDebugger] UI translation JSON for {code} could not be read: {exception.Message}");
                return null;
            }
            if (parsed == null || parsed.strings == null || parsed.patterns == null || parsed.language != code)
            {
                Debug.LogWarning($"[MasterMemoryDebugger] UI translation JSON for {code} is incomplete.");
                return null;
            }
            var catalog = new Catalog
            {
                DisplayName = parsed.displayName,
                Texts = new Dictionary<string, string>(StringComparer.Ordinal),
                Patterns = new List<(Regex, string)>()
            };
            foreach (var entry in parsed.strings)
                if (entry != null && !string.IsNullOrEmpty(entry.key)) catalog.Texts[entry.key] = entry.value ?? entry.key;
            foreach (var entry in parsed.patterns)
            {
                if (entry == null || string.IsNullOrEmpty(entry.pattern)) continue;
                try { catalog.Patterns.Add((new Regex(entry.pattern, RegexOptions.CultureInvariant), entry.replacement ?? string.Empty)); }
                catch (ArgumentException exception)
                {
                    Debug.LogWarning($"[MasterMemoryDebugger] Invalid UI translation pattern in {code}: {exception.Message}");
                }
            }
            catalogs[code] = catalog;
            return catalog;
        }

        public void Apply(VisualElement root)
        {
            if (root == null) return;
            var catalog = GetCatalog(Language);
            if (catalog != null) Visit(root, catalog);
        }

        void Visit(VisualElement element, Catalog catalog)
        {
            // Grid cells are user data. Inspector field names already come from the registered Record type.
            if (element.name == "mm-record-grid" || element.name == "mm-table-list" || element.ClassListContains("mm-debugger__field") || element.ClassListContains("mm-debugger__referenced-by-name") || element.ClassListContains("mm-debugger__change-field-name") || element.ClassListContains("mm-debugger__change-new") || element.ClassListContains("mm-debugger__change-name") || element.ClassListContains("mm-debugger__find-table")) return;
            var original = originals.GetValue(element, _ => new Original());
            if (element is TextElement textElement)
                Update(textElement.text, value => textElement.text = value, catalog, ref original.Text, ref original.TranslatedText);
            Update(element.tooltip, value => element.tooltip = value, catalog, ref original.Tooltip, ref original.TranslatedTooltip);
            if (element is TextField field)
                Update(field.textEdition.placeholder, value => field.textEdition.placeholder = value, catalog, ref original.Placeholder, ref original.TranslatedPlaceholder);
            foreach (var child in element.Children()) Visit(child, catalog);
        }

        static void Update(string current, Action<string> set, Catalog catalog, ref string source, ref string translated)
        {
            if (string.IsNullOrEmpty(current)) return;
            // A controller may have replaced the English text since the last pass; retain that new source.
            if (current != translated) source = current;
            var next = Translate(source, catalog);
            if (current != next) set(next);
            translated = next;
        }

        static string Translate(string value, Catalog catalog)
        {
            if (catalog.Texts.TryGetValue(value, out var text)) return text;
            foreach (var (pattern, replacement) in catalog.Patterns)
                if (pattern.IsMatch(value)) return pattern.Replace(value, replacement);
            return value;
        }
    }
}
