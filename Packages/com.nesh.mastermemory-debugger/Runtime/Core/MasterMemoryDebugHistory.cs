using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Undo / redo of override changes made by the debugger (Apply, Reset, Reset All, patches, batch edits).
    /// Each operation wrapped in <see cref="Record"/> becomes one step holding the previous and new override of every record
    /// it changed. Changes made outside a Record scope (for example by game code) clear the history, because undoing
    /// across them would overwrite them.
    /// <code>
    /// using (MasterMemoryDebugHistory.Record("Tune skills"))
    /// {
    ///     MasterMemoryDebugRuntime.SetOverride(1001, skill with { Damage = 150 });
    /// }
    /// MasterMemoryDebugHistory.Undo();
    /// </code>
    /// </summary>
    public static class MasterMemoryDebugHistory
    {
        /// <summary>Oldest steps are dropped beyond this count.</summary>
        public const int MaxSteps = 50;

        sealed class Change
        {
            public MasterDataOverrideKey Key;
            public object Before;
            public object After;
        }

        sealed class Step
        {
            public string Label;
            public readonly List<Change> Changes = new List<Change>();
            public readonly Dictionary<MasterDataOverrideKey, Change> ByKey = new Dictionary<MasterDataOverrideKey, Change>();
        }

        static readonly object s_gate = new object();
        static readonly List<Step> s_undo = new List<Step>();
        static readonly List<Step> s_redo = new List<Step>();
        static Step s_recording;
        static int s_depth;
        static bool s_replaying;

        /// <summary>Raised when a step is added, undone, redone or the history is cleared.</summary>
        public static event Action Changed;

        public static bool CanUndo
        {
            get
            {
                lock (s_gate) return s_undo.Count > 0;
            }
        }

        public static bool CanRedo
        {
            get
            {
                lock (s_gate) return s_redo.Count > 0;
            }
        }

        /// <summary>Label of the step that <see cref="Undo"/> reverts, or null.</summary>
        public static string UndoLabel
        {
            get
            {
                lock (s_gate) return s_undo.Count > 0 ? s_undo[s_undo.Count - 1].Label : null;
            }
        }

        public static string RedoLabel
        {
            get
            {
                lock (s_gate) return s_redo.Count > 0 ? s_redo[s_redo.Count - 1].Label : null;
            }
        }

        /// <summary>
        /// Records the override changes made until the returned scope is disposed as one undo step (nothing when nothing
        /// changed). Nested scopes join the outermost one.
        /// </summary>
        public static IDisposable Record(string label)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return new Scope(false);
            lock (s_gate)
            {
                if (s_depth++ == 0) s_recording = new Step { Label = label ?? "Change" };
            }
            return new Scope(true);
        }

        /// <summary>Reverts the last step. Returns its label, or null when there is nothing to undo.</summary>
        public static string Undo()
        {
            Step step;
            lock (s_gate)
            {
                if (s_undo.Count == 0 || s_depth > 0) return null;
                step = s_undo[s_undo.Count - 1];
                s_undo.RemoveAt(s_undo.Count - 1);
                s_redo.Add(step);
            }
            Replay(step, undo: true);
            Changed?.Invoke();
            return step.Label;
        }

        /// <summary>Applies the last undone step again. Returns its label, or null when there is nothing to redo.</summary>
        public static string Redo()
        {
            Step step;
            lock (s_gate)
            {
                if (s_redo.Count == 0 || s_depth > 0) return null;
                step = s_redo[s_redo.Count - 1];
                s_redo.RemoveAt(s_redo.Count - 1);
                s_undo.Add(step);
            }
            Replay(step, undo: false);
            Changed?.Invoke();
            return step.Label;
        }

        public static void Clear()
        {
            lock (s_gate)
            {
                if (s_undo.Count == 0 && s_redo.Count == 0) return;
                s_undo.Clear();
                s_redo.Clear();
            }
            Changed?.Invoke();
        }

        /// <summary>Called by the override store for every changed record.</summary>
        internal static void OnEntryChanged(MasterDataOverrideKey key, object before, object after)
        {
            var cleared = false;
            lock (s_gate)
            {
                if (s_replaying) return;
                if (s_recording != null)
                {
                    // several changes of a record in one step: keep the first previous value and the last new one
                    if (s_recording.ByKey.TryGetValue(key, out var change))
                    {
                        change.After = after;
                    }
                    else
                    {
                        change = new Change { Key = key, Before = before, After = after };
                        s_recording.ByKey.Add(key, change);
                        s_recording.Changes.Add(change);
                    }
                    return;
                }
                if (s_undo.Count > 0 || s_redo.Count > 0)
                {
                    s_undo.Clear();
                    s_redo.Clear();
                    cleared = true;
                }
            }
            if (cleared) Changed?.Invoke();
        }

        static void EndRecord()
        {
            Step finished = null;
            lock (s_gate)
            {
                if (s_depth == 0) return;
                if (--s_depth > 0) return;
                var step = s_recording;
                s_recording = null;
                step.Changes.RemoveAll(x => ReferenceEquals(x.Before, x.After));
                if (step.Changes.Count == 0) return;
                s_undo.Add(step);
                if (s_undo.Count > MaxSteps) s_undo.RemoveAt(0);
                s_redo.Clear();
                finished = step;
            }
            if (finished != null) Changed?.Invoke();
        }

        static void Replay(Step step, bool undo)
        {
            var store = MasterMemoryDebugRuntime.Store;
            lock (s_gate) s_replaying = true;
            try
            {
                using (MasterMemoryDebugRuntime.BeginBatch())
                {
                    var count = step.Changes.Count;
                    for (var i = 0; i < count; i++)
                    {
                        var change = step.Changes[undo ? count - 1 - i : i];
                        var value = undo ? change.Before : change.After;
                        if (value == null) store.Remove(change.Key.RecordType, change.Key.PrimaryKey);
                        else store.Set(change.Key.RecordType, change.Key.PrimaryKey, value);
                    }
                    // the batch raises OverridesChanged here, while replaying
                }
            }
            finally
            {
                lock (s_gate) s_replaying = false;
            }
        }

        sealed class Scope : IDisposable
        {
            bool active;

            public Scope(bool active)
            {
                this.active = active;
            }

            public void Dispose()
            {
                if (!active) return;
                active = false;
                EndRecord();
            }
        }
    }
}
