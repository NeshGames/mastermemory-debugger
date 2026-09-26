using System;
using System.Reflection;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>How a member value is displayed / edited.</summary>
    public enum MasterDataValueKind
    {
        /// <summary>Arrays, lists, dictionaries, nested objects and other unsupported types. Always read-only.</summary>
        Complex = 0,
        String,
        Boolean,
        Int32,
        UInt32,
        Int16,
        UInt16,
        Int64,
        UInt64,
        Byte,
        SByte,
        Single,
        Double,
        Enum,
        FlagsEnum,
        Vector2,
        Vector3,
        Vector2Int,
        Vector3Int,
        Color,
    }

    /// <summary>A public property or field of a MasterMemory record.</summary>
    public sealed class MasterMemoryFieldDescriptor
    {
        readonly Func<object, object> getter;
        readonly Action<object, object> setter;

        internal MasterMemoryFieldDescriptor(
            MemberInfo member,
            Type fieldType,
            Func<object, object> getter,
            Action<object, object> setter,
            bool isPrimaryKey,
            int primaryKeyOrder,
            bool isSecondaryKey,
            int order)
        {
            Member = member;
            Name = member.Name;
            FieldType = fieldType;
            this.getter = getter;
            this.setter = setter;
            IsPrimaryKey = isPrimaryKey;
            PrimaryKeyOrder = primaryKeyOrder;
            IsSecondaryKey = isSecondaryKey;
            Order = order;

            var underlying = Nullable.GetUnderlyingType(fieldType);
            IsNullable = underlying != null;
            ValueType = underlying ?? fieldType;
            Kind = MasterDataValueUtility.GetKind(ValueType);
            if (MasterDataValueUtility.TryGetEditableListElement(fieldType, out var elementType))
            {
                ElementType = elementType;
                ElementKind = MasterDataValueUtility.GetKind(elementType);
            }
            CanEdit = !IsPrimaryKey && !IsSecondaryKey && setter != null && (Kind != MasterDataValueKind.Complex || IsList);
        }

        public string Name { get; }

        public MemberInfo Member { get; }

        /// <summary>Declared type of the member.</summary>
        public Type FieldType { get; }

        /// <summary>Declared type, or the underlying type when the member is <see cref="Nullable{T}"/>.</summary>
        public Type ValueType { get; }

        public bool IsNullable { get; }

        public MasterDataValueKind Kind { get; }

        public bool IsPrimaryKey { get; }

        /// <summary>KeyOrder of <c>[PrimaryKey]</c>, used for composite keys.</summary>
        public int PrimaryKeyOrder { get; }

        public bool IsSecondaryKey { get; }

        public bool IsKey => IsPrimaryKey || IsSecondaryKey;

        /// <summary>True when the member is a non-key member of a supported simple type, or a list of them, with a usable setter.</summary>
        public bool CanEdit { get; }

        /// <summary>
        /// Element type of an array / List / list interface of simple values (editable, copied on every edit); otherwise null.
        /// </summary>
        public Type ElementType { get; }

        public MasterDataValueKind ElementKind { get; }

        public bool IsList => ElementType != null;

        /// <summary>True when the value can be exported to / imported from a patch.</summary>
        public bool IsSimpleValue => Kind != MasterDataValueKind.Complex;

        public int Order { get; }

        public Func<object, object> Getter => getter;

        public object GetValue(object record)
        {
            return getter(record);
        }

        /// <summary>
        /// Writes a value to an editable copy of a record.
        /// Never call this on a record instance owned by the MasterMemory database.
        /// </summary>
        public void SetValue(object editableCopy, object value)
        {
            if (!CanEdit) throw new InvalidOperationException($"Field '{Name}' is read-only.");
            setter(editableCopy, value);
        }

        public override string ToString() => $"{Name} ({FieldType.Name})";
    }
}
