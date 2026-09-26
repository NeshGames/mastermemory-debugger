using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Read-only foldout tree for arrays, lists, dictionaries and nested objects.
    /// Children are created when a node is expanded; depth and item count are limited.
    /// </summary>
    internal static class MasterValueTreeFactory
    {
        public const int MaxDepth = 3;
        public const int MaxItems = 100;

        public static VisualElement Create(object value)
        {
            if (!IsExpandable(value)) return CreateLeaf(null, value);
            return CreateFoldout(null, value, 1);
        }

        /// <summary>True for values shown as a tree (not null, not a simple value).</summary>
        public static bool IsExpandable(object value)
        {
            if (value == null) return false;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) return false;
            return MasterDataValueUtility.GetKind(type) == MasterDataValueKind.Complex;
        }

        /// <summary>Child entries (name, value) of a complex value, at most <paramref name="max"/> plus the total count.</summary>
        public static List<KeyValuePair<string, object>> GetChildren(object value, int max, out int total)
        {
            var children = new List<KeyValuePair<string, object>>();
            total = 0;
            switch (value)
            {
                case IDictionary dictionary:
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (total++ < max) children.Add(new KeyValuePair<string, object>(MasterDataValueUtility.Format(entry.Key), entry.Value));
                    }
                    break;
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                    {
                        if (total < max) children.Add(new KeyValuePair<string, object>("[" + total + "]", item));
                        total++;
                    }
                    break;
                default:
                    foreach (var field in MasterDataReflectionCache.Get(value.GetType()).Fields)
                    {
                        object child;
                        try
                        {
                            child = field.GetValue(value);
                        }
                        catch (Exception e)
                        {
                            child = "<error: " + ((e as System.Reflection.TargetInvocationException)?.InnerException ?? e).Message + ">";
                        }
                        if (total++ < max) children.Add(new KeyValuePair<string, object>(field.Name, child));
                    }
                    break;
            }
            return children;
        }

        static VisualElement CreateFoldout(string name, object value, int depth)
        {
            var summary = MasterDataValueUtility.Format(value);
            var foldout = new Foldout
            {
                text = name == null ? summary : $"{name}: {summary}",
                value = false,
            };
            foldout.AddToClassList("mm-debugger__tree");

            var built = false;
            foldout.RegisterValueChangedCallback(evt =>
            {
                // nested foldouts bubble their change events
                if (evt.target != foldout || !evt.newValue || built) return;
                built = true;
                BuildChildren(foldout, value, depth);
            });
            return foldout;
        }

        static void BuildChildren(Foldout parent, object value, int depth)
        {
            var children = GetChildren(value, MaxItems, out var total);
            foreach (var child in children)
            {
                parent.Add(IsExpandable(child.Value) && depth < MaxDepth
                    ? CreateFoldout(child.Key, child.Value, depth + 1)
                    : CreateLeaf(child.Key, child.Value));
            }
            if (total > children.Count)
            {
                var more = new Label($"... {total - children.Count} more");
                more.AddToClassList("mm-debugger__tree-more");
                parent.Add(more);
            }
            if (total == 0)
            {
                var empty = new Label("(empty)");
                empty.AddToClassList("mm-debugger__tree-more");
                parent.Add(empty);
            }
        }

        static Label CreateLeaf(string name, object value)
        {
            var text = MasterDataValueUtility.Format(value);
            var label = new Label(name == null ? text : $"{name}: {text}");
            label.AddToClassList(MasterFieldDrawerFactory.ReadOnlyClass);
            label.AddToClassList("mm-debugger__tree-leaf");
            label.selection.isSelectable = true;
            return label;
        }
    }
}
