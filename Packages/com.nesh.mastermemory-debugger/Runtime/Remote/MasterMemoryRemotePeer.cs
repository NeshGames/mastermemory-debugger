using System;
using System.Collections.Generic;
using MessagePack;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Shared part of the game (server) and the tool (client): local override changes are sent to the other side once per
    /// <see cref="Pump"/>, and changes received from it are applied to the local store without being sent back.
    /// </summary>
    internal abstract class MasterMemoryRemotePeer : IDisposable
    {
        readonly object pendingGate = new object();
        readonly List<MasterMemoryRemoteProtocol.Change> pending = new List<MasterMemoryRemoteProtocol.Change>();
        bool applyingRemote;
        bool syncing;

        protected MasterMemoryRemoteConnection Connection { get; set; }

        public abstract MasterMemoryRemoteState State { get; }

        /// <summary>One line for the UI: address, code, error.</summary>
        public abstract string Status { get; }

        /// <summary>Raised on the main thread when <see cref="State"/> or <see cref="Status"/> changed.</summary>
        public event Action Changed;

        /// <summary>Called every frame on the main thread.</summary>
        public abstract void Pump();

        public virtual void Dispose()
        {
            StopSyncing();
            Connection?.Dispose();
            Connection = null;
        }

        protected void RaiseChanged() => Changed?.Invoke();

        protected void StartSyncing()
        {
            lock (pendingGate) pending.Clear();
            if (syncing) return;
            syncing = true;
            MasterMemoryDebugRuntime.EntryChanged += OnEntryChanged;
        }

        protected void StopSyncing()
        {
            if (!syncing) return;
            syncing = false;
            MasterMemoryDebugRuntime.EntryChanged -= OnEntryChanged;
            lock (pendingGate) pending.Clear();
        }

        /// <summary>Sends the local changes collected since the last call.</summary>
        protected void Flush()
        {
            List<MasterMemoryRemoteProtocol.Change> changes;
            lock (pendingGate)
            {
                if (pending.Count == 0) return;
                changes = new List<MasterMemoryRemoteProtocol.Change>(pending);
                pending.Clear();
            }
            Connection?.Send(MasterMemoryRemoteProtocol.Encode(changes));
        }

        void OnEntryChanged(MasterDataOverrideKey key, object before, object after)
        {
            if (applyingRemote) return;
            if (!MasterMemoryDebugRegistry.TryGetTable(key.RecordType, out var table)) return;
            var kind = after == null ? MasterMemoryRemoteProtocol.ChangeKind.Remove
                : MasterDataOverrideStore.IsDeletedValue(after) ? MasterMemoryRemoteProtocol.ChangeKind.Delete
                : MasterMemoryRemoteProtocol.ChangeKind.Set;
            byte[] record;
            try
            {
                // a removal or a deletion sends a record with the key (the removed override, or the original record):
                // the other side reads the key from it
                record = Serialize(table.RecordType, RecordWithKey(table, key, kind == MasterMemoryRemoteProtocol.ChangeKind.Remove ? before : after));
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Warning($"Remote: {table.TableName} {MasterDataValueUtility.FormatKey(key.PrimaryKey)} could not be sent: {(e.InnerException ?? e).Message}");
                return;
            }
            lock (pendingGate) pending.Add(new MasterMemoryRemoteProtocol.Change { Kind = kind, TableName = table.TableName, Record = record });
        }

        /// <summary><paramref name="value"/>, or the original record when it is the deletion marker.</summary>
        internal static object RecordWithKey(MasterMemoryTableDescriptor table, MasterDataOverrideKey key, object value)
        {
            if (value != null && !MasterDataOverrideStore.IsDeletedValue(value)) return value;
            if (table.TryFindOriginal(key.PrimaryKey, out var original)) return original;
            throw new InvalidOperationException("the original record of the deletion was not found");
        }

        /// <summary>Applies changes of the other side in one override batch (not sent back, not an undo step).</summary>
        protected void ApplyRemote(List<MasterMemoryRemoteProtocol.Change> changes, bool replaceAll = false)
        {
            var store = MasterMemoryDebugRuntime.Store;
            applyingRemote = true;
            try
            {
                using (MasterMemoryDebugRuntime.BeginBatch())
                {
                    if (replaceAll) store.Clear();
                    foreach (var change in changes)
                    {
                        if (!MasterMemoryDebugRegistry.TryGetTable(change.TableName, out var table))
                        {
                            MasterMemoryDebugLog.Warning($"Remote: table {change.TableName} is not registered; its change is ignored.");
                            continue;
                        }
                        try
                        {
                            var record = Deserialize(table.RecordType, change.Record);
                            var key = table.GetPrimaryKey(record);
                            switch (change.Kind)
                            {
                                case MasterMemoryRemoteProtocol.ChangeKind.Set:
                                    store.Set(table.RecordType, key, record);
                                    break;
                                case MasterMemoryRemoteProtocol.ChangeKind.Delete:
                                    store.Delete(table.RecordType, key);
                                    break;
                                default:
                                    store.Remove(table.RecordType, key);
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            MasterMemoryDebugLog.Warning($"Remote: a change of {change.TableName} could not be applied: {(e.InnerException ?? e).Message}");
                        }
                    }
                }
            }
            finally
            {
                applyingRemote = false;
            }
        }

        internal static byte[] Serialize(Type type, object value)
        {
            return MessagePackSerializer.Serialize(type, value, MasterMemoryDebugRemote.SerializerOptions ?? MessagePackSerializer.DefaultOptions);
        }

        internal static object Deserialize(Type type, byte[] bytes)
        {
            return MessagePackSerializer.Deserialize(type, bytes, MasterMemoryDebugRemote.SerializerOptions ?? MessagePackSerializer.DefaultOptions);
        }
    }
}
