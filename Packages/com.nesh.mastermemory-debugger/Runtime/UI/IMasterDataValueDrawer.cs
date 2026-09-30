using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Optional capability of an <see cref="IMasterDataValueConverter"/>: its own inspector editor instead of the default text
    /// field (which parses every change and only marks text it can not read). Used for members, Nullable members and list
    /// elements of the converted type.
    /// </summary>
    public interface IMasterDataValueDrawer
    {
        /// <summary>
        /// An editor of <paramref name="value"/>. Pass every new value (of <see cref="IMasterDataValueConverter.ValueType"/>) to
        /// <paramref name="onChanged"/>; never pass values that could not be read. Null uses the default text field.
        /// </summary>
        VisualElement CreateEditor(object value, Action<object> onChanged);
    }
}
