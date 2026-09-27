using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Creates runtime UI Toolkit editors for simple member types, lists of them and nested objects.
    /// Unsupported types fall back to a read-only label instead of failing.
    /// </summary>
    internal static class MasterFieldDrawerFactory
    {
        public const string EditorClass = "mm-debugger__field-editor";
        public const string ReadOnlyClass = "mm-debugger__field-readonly";
        public const string InvalidClass = "mm-debugger__field-editor--invalid";

        /// <summary>Longer lists are shown read-only.</summary>
        public const int MaxEditableListItems = 200;

        /// <summary>Nested objects deeper than this are shown read-only.</summary>
        public const int MaxObjectDepth = 4;

        /// <summary>Read-only view of any value: text, or a foldout tree for arrays / lists / nested objects.</summary>
        public static VisualElement CreateReadOnly(object value)
        {
            if (MasterValueTreeFactory.IsExpandable(value)) return MasterValueTreeFactory.Create(value);
            var label = new Label(MasterDataValueUtility.Format(value));
            label.AddToClassList(ReadOnlyClass);
            label.selection.isSelectable = true;
            return label;
        }

        /// <summary>
        /// Editor of an editable member. <paramref name="onChanged"/> receives values already converted to <see cref="MasterMemoryFieldDescriptor.FieldType"/>.
        /// </summary>
        public static VisualElement CreateEditor(MasterMemoryFieldDescriptor field, object value, Action<object> onChanged)
        {
            return CreateEditor(field, value, onChanged, 1);
        }

        static VisualElement CreateEditor(MasterMemoryFieldDescriptor field, object value, Action<object> onChanged, int depth)
        {
            if (!field.CanEdit) return CreateReadOnly(value);
            if (field.IsList) return CreateListEditor(field, (IList)value, onChanged);
            if (field.IsObject) return CreateObjectEditor(value, onChanged, depth);
            if (field.IsNullable) return CreateNullableEditor(field, value, onChanged);

            var editor = CreateValueEditor(field.Kind, field.ValueType, value, onChanged);
            return editor ?? CreateReadOnly(value);
        }

        /// <summary>
        /// Foldout with one editor per element, remove buttons and Add. The list shown by the record is never modified:
        /// every change passes a new array / List to <paramref name="onChanged"/>.
        /// </summary>
        static VisualElement CreateListEditor(MasterMemoryFieldDescriptor field, IList value, Action<object> onChanged)
        {
            if (value != null && value.Count > MaxEditableListItems)
            {
                var readOnly = CreateReadOnly(value);
                readOnly.tooltip = $"Lists with more than {MaxEditableListItems} items are read-only.";
                return readOnly;
            }

            var items = new List<object>();
            if (value != null)
            {
                foreach (var item in value) items.Add(item);
            }

            var foldout = new Foldout { value = items.Count <= 10 };
            foldout.AddToClassList("mm-debugger__list-editor");
            var body = new VisualElement();
            body.AddToClassList("mm-debugger__list-items");
            var addButton = new Button { text = "+ Add", tooltip = "Append an element" };
            addButton.AddToClassList("mm-debugger__button");
            addButton.AddToClassList("mm-debugger__list-add");

            void Publish()
            {
                foldout.text = $"[{items.Count}]";
                onChanged(MasterDataValueUtility.CreateList(field.FieldType, field.ElementType, items));
            }

            void Rebuild()
            {
                body.Clear();
                foldout.text = $"[{items.Count}]";
                for (var i = 0; i < items.Count; i++)
                {
                    var index = i;
                    var row = new VisualElement();
                    row.AddToClassList("mm-debugger__list-item");
                    var indexLabel = new Label(index.ToString());
                    indexLabel.AddToClassList("mm-debugger__list-index");
                    row.Add(indexLabel);

                    var editor = CreateValueEditor(field.ElementKind, field.ElementType, items[index], newValue =>
                    {
                        items[index] = newValue;
                        Publish();
                    }) ?? CreateReadOnly(items[index]);
                    editor.AddToClassList("mm-debugger__list-value");
                    row.Add(editor);

                    var remove = new Button(() =>
                    {
                        items.RemoveAt(index);
                        Rebuild();
                        Publish();
                    })
                    { text = "×", tooltip = "Remove this element" };
                    remove.AddToClassList("mm-debugger__button");
                    remove.AddToClassList("mm-debugger__list-remove");
                    row.Add(remove);
                    body.Add(row);
                }
                body.Add(addButton);
            }

            addButton.clicked += () =>
            {
                // repeat the last element: lists usually hold similar values
                items.Add(items.Count > 0 ? items[items.Count - 1] : MasterDataValueUtility.CreateDefaultElement(field.ElementType));
                Rebuild();
                Publish();
            };

            Rebuild();
            foldout.Add(body);
            return foldout;
        }

        /// <summary>
        /// Foldout with one row per member of a nested object. The object shown by the record is never modified: every
        /// change passes a changed copy to <paramref name="onChanged"/>. Null objects can not be edited.
        /// </summary>
        static VisualElement CreateObjectEditor(object value, Action<object> onChanged, int depth)
        {
            if (value == null || depth > MaxObjectDepth)
            {
                var readOnly = CreateReadOnly(value);
                readOnly.tooltip = value == null ? "Null objects can not be edited." : $"Objects nested deeper than {MaxObjectDepth} levels are read-only.";
                return readOnly;
            }

            var current = value;
            var foldout = new Foldout { text = MasterDataValueUtility.Format(current), value = depth == 1 };
            foldout.AddToClassList("mm-debugger__object-editor");
            foreach (var member in MasterDataReflectionCache.Get(value.GetType()).Fields)
            {
                var row = new VisualElement();
                row.AddToClassList("mm-debugger__object-member");
                var name = new Label(member.Name) { tooltip = member.ToString() };
                name.AddToClassList("mm-debugger__object-member-name");
                row.Add(name);

                var memberValue = member.GetValue(current);
                var editor = member.CanEdit
                    ? CreateEditor(member, memberValue, newValue =>
                    {
                        var copy = MasterDataCloneUtility.Clone(current);
                        member.SetValue(copy, newValue);
                        current = copy;
                        foldout.text = MasterDataValueUtility.Format(current);
                        onChanged(current);
                    }, depth + 1)
                    : CreateReadOnly(memberValue);
                editor.AddToClassList("mm-debugger__object-member-value");
                row.Add(editor);
                foldout.Add(row);
            }
            return foldout;
        }

        static VisualElement CreateNullableEditor(MasterMemoryFieldDescriptor field, object value, Action<object> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("mm-debugger__nullable");

            var hasValue = new Toggle { value = value != null, tooltip = "Has value (unchecked = null)" };
            hasValue.AddToClassList("mm-debugger__nullable-toggle");

            object lastValue = value ?? Activator.CreateInstance(field.ValueType);
            var inner = CreateValueEditor(field.Kind, field.ValueType, lastValue, newValue =>
            {
                lastValue = newValue;
                onChanged(newValue);
            });
            if (inner == null) return CreateReadOnly(value);

            inner.SetEnabled(value != null);
            hasValue.RegisterValueChangedCallback(evt =>
            {
                inner.SetEnabled(evt.newValue);
                onChanged(evt.newValue ? lastValue : null);
            });

            container.Add(hasValue);
            container.Add(inner);
            return container;
        }

        static VisualElement CreateValueEditor(MasterDataValueKind kind, Type type, object value, Action<object> onChanged)
        {
            switch (kind)
            {
                case MasterDataValueKind.String:
                {
                    // multiline + wrapping shows long values completely; Enter still applies (handled by the controller)
                    var f = Prepare(new TextField { value = (string)value ?? string.Empty, multiline = true });
                    f.AddToClassList("mm-debugger__text-editor");
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Boolean:
                {
                    var f = Prepare(new Toggle { value = (bool)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Int32:
                {
                    var f = Prepare(new IntegerField { value = (int)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Int16:
                    return CreateClampedIntegerEditor(Convert.ToInt32(value), short.MinValue, short.MaxValue, v => onChanged((short)v));
                case MasterDataValueKind.UInt16:
                    return CreateClampedIntegerEditor(Convert.ToInt32(value), ushort.MinValue, ushort.MaxValue, v => onChanged((ushort)v));
                case MasterDataValueKind.Byte:
                    return CreateClampedIntegerEditor(Convert.ToInt32(value), byte.MinValue, byte.MaxValue, v => onChanged((byte)v));
                case MasterDataValueKind.SByte:
                    return CreateClampedIntegerEditor(Convert.ToInt32(value), sbyte.MinValue, sbyte.MaxValue, v => onChanged((sbyte)v));
                case MasterDataValueKind.UInt32:
                {
                    var f = Prepare(new UnsignedIntegerField { value = (uint)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Int64:
                {
                    var f = Prepare(new LongField { value = (long)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.UInt64:
                {
                    var f = Prepare(new UnsignedLongField { value = (ulong)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Single:
                {
                    var f = Prepare(new FloatField { value = (float)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Double:
                {
                    var f = Prepare(new DoubleField { value = (double)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Enum when !Enum.IsDefined(type, value):
                    // EnumField can not display undefined values; edit them as text
                    return CreateTextEnumEditor(type, (Enum)value, onChanged);
                case MasterDataValueKind.Enum:
                {
                    var f = Prepare(new EnumField((Enum)value));
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.FlagsEnum:
                    return CreateTextEnumEditor(type, (Enum)value, onChanged);
                case MasterDataValueKind.Vector2:
                {
                    var f = Prepare(new Vector2Field { value = (Vector2)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Vector3:
                {
                    var f = Prepare(new Vector3Field { value = (Vector3)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Vector2Int:
                {
                    var f = Prepare(new Vector2IntField { value = (Vector2Int)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Vector3Int:
                {
                    var f = Prepare(new Vector3IntField { value = (Vector3Int)value });
                    f.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                    return f;
                }
                case MasterDataValueKind.Color:
                    return CreateColorEditor((Color)value, onChanged);
                default:
                    return null;
            }
        }

        static T Prepare<T>(T element) where T : VisualElement
        {
            element.AddToClassList(EditorClass);
            return element;
        }

        static VisualElement CreateClampedIntegerEditor(int value, int min, int max, Action<int> onChanged)
        {
            var field = Prepare(new IntegerField { value = value });
            field.RegisterValueChangedCallback(evt =>
            {
                var clamped = Mathf.Clamp(evt.newValue, min, max);
                if (clamped != evt.newValue) field.SetValueWithoutNotify(clamped);
                onChanged(clamped);
            });
            return field;
        }

        /// <summary>Flags enums ("A, B") and undefined values are edited as text because the runtime has no EnumFlagsField.</summary>
        static VisualElement CreateTextEnumEditor(Type type, Enum value, Action<object> onChanged)
        {
            var field = Prepare(new TextField { value = value.ToString() });
            field.tooltip = "Values: " + string.Join(", ", Enum.GetNames(type));
            field.RegisterValueChangedCallback(evt =>
            {
                // edited live: an unparsable text (for example while typing) is only marked, never applied
                Enum parsed;
                try
                {
                    parsed = (Enum)MasterDataValueUtility.ParseEnum(type, evt.newValue);
                }
                catch (Exception)
                {
                    field.AddToClassList(InvalidClass);
                    return;
                }
                field.RemoveFromClassList(InvalidClass);
                onChanged(parsed);
            });
            return field;
        }

        /// <summary>The runtime has no ColorField: RGBA float fields and a swatch.</summary>
        static VisualElement CreateColorEditor(Color value, Action<object> onChanged)
        {
            var container = Prepare(new VisualElement());
            container.AddToClassList("mm-debugger__color");

            var swatch = new VisualElement();
            swatch.AddToClassList("mm-debugger__color-swatch");
            swatch.style.backgroundColor = value;
            container.Add(swatch);

            var current = value;
            string[] channels = { "R", "G", "B", "A" };
            for (var i = 0; i < 4; i++)
            {
                var channel = i;
                var field = new FloatField(channels[i]) { value = current[channel] };
                field.AddToClassList("mm-debugger__color-channel");
                field.RegisterValueChangedCallback(evt =>
                {
                    current[channel] = evt.newValue;
                    swatch.style.backgroundColor = current;
                    onChanged(current);
                });
                container.Add(field);
            }
            return container;
        }
    }
}
