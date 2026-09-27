using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Messages between a game build (server) and the remote editor tool (client). Every message is one frame:
    /// a 4 byte little endian length, then the payload (message type byte + fields written with BinaryWriter).
    /// Records travel as MessagePack (<see cref="MasterMemoryDebugRemote.SerializerOptions"/>), so the tool must be built
    /// from the same project as the game (same record types).
    /// </summary>
    internal static class MasterMemoryRemoteProtocol
    {
        /// <summary>2: validation messages. 3: deleted records.</summary>
        public const int Version = 3;
        public const int MaxFrameBytes = 512 * 1024 * 1024;

        public enum MessageType : byte
        {
            /// <summary>Client → server: protocol version and pairing code.</summary>
            Hello = 1,
            /// <summary>Server → client: every table, its records and the current overrides.</summary>
            Welcome = 2,
            /// <summary>Server → client: the connection is refused (wrong code, busy, other version).</summary>
            Reject = 3,
            /// <summary>Both ways: overrides set or removed, records deleted.</summary>
            Changes = 4,
            /// <summary>Server → client: whether the game validates, and the failures caused by the overrides.</summary>
            ValidationState = 5,
            /// <summary>Client → server: validate now (Validation tab).</summary>
            ValidateRequest = 6,
            /// <summary>Server → client: every failure of the game's Validate().</summary>
            ValidateResult = 7,
        }

        public sealed class ValidationState
        {
            public bool IsAvailable;
            public int NewFailureCount;
        }

        public sealed class Failure
        {
            /// <summary>Registered table name, or the record type name when the table is not registered.</summary>
            public string TableName;
            /// <summary>Key as shown in the debugger (<c>1001</c>, <c>(2, 1)</c>); empty when unknown.</summary>
            public string Key;
            public string Message;
            public bool IsNew;
        }

        public sealed class Hello
        {
            public int Version;
            public string Code;
        }

        public sealed class Table
        {
            public string TableName;
            public string MemoryTableName;
            public string RecordType;
            public string KeyType;
            public string Group;
            public List<byte[]> Records = new List<byte[]>();
            /// <summary>Display names in record order, or null when the table has no custom display name.</summary>
            public List<string> DisplayNames;
        }

        public sealed class Welcome
        {
            public int Version;
            public string MasterVersion;
            public string LabelsTsv;
            public List<Table> Tables = new List<Table>();
            public List<Change> Overrides = new List<Change>();
        }

        public enum ChangeKind : byte
        {
            /// <summary>Remove the override of the record's key (restores a deleted record).</summary>
            Remove = 0,
            /// <summary>Set the override to the record (adds it when it has no original).</summary>
            Set = 1,
            /// <summary>Delete the original record with the record's key.</summary>
            Delete = 2,
        }

        public sealed class Change
        {
            public ChangeKind Kind;

            /// <summary>True for <see cref="ChangeKind.Set"/>; setting false means <see cref="ChangeKind.Remove"/>.</summary>
            public bool IsSet
            {
                get => Kind == ChangeKind.Set;
                set => Kind = value ? ChangeKind.Set : ChangeKind.Remove;
            }

            public string TableName;

            /// <summary>The override for Set; a record with the key (the removed override, or the original) otherwise.</summary>
            public byte[] Record;
        }

        // ------------------------------------------------------------------ encode

        public static byte[] Encode(Hello message) => Write(MessageType.Hello, w =>
        {
            w.Write(message.Version);
            w.Write(message.Code ?? string.Empty);
        });

        public static byte[] Encode(Welcome message) => Write(MessageType.Welcome, w =>
        {
            w.Write(message.Version);
            w.Write(message.MasterVersion ?? string.Empty);
            w.Write(message.LabelsTsv ?? string.Empty);
            w.Write(message.Tables.Count);
            foreach (var table in message.Tables)
            {
                w.Write(table.TableName ?? string.Empty);
                w.Write(table.MemoryTableName ?? string.Empty);
                w.Write(table.RecordType ?? string.Empty);
                w.Write(table.KeyType ?? string.Empty);
                w.Write(table.Group ?? string.Empty);
                w.Write(table.Records.Count);
                foreach (var record in table.Records) WriteBytes(w, record);
                w.Write(table.DisplayNames != null);
                if (table.DisplayNames != null)
                {
                    w.Write(table.DisplayNames.Count);
                    foreach (var name in table.DisplayNames) w.Write(name ?? string.Empty);
                }
            }
            WriteChanges(w, message.Overrides);
        });

        public static byte[] EncodeReject(string reason) => Write(MessageType.Reject, w => w.Write(reason ?? string.Empty));

        public static byte[] Encode(List<Change> changes) => Write(MessageType.Changes, w => WriteChanges(w, changes));

        public static byte[] Encode(ValidationState state) => Write(MessageType.ValidationState, w =>
        {
            w.Write(state.IsAvailable);
            w.Write(state.NewFailureCount);
        });

        public static byte[] EncodeValidateRequest() => Write(MessageType.ValidateRequest, _ => { });

        public static byte[] Encode(List<Failure> failures) => Write(MessageType.ValidateResult, w =>
        {
            w.Write(failures.Count);
            foreach (var failure in failures)
            {
                w.Write(failure.TableName ?? string.Empty);
                w.Write(failure.Key ?? string.Empty);
                w.Write(failure.Message ?? string.Empty);
                w.Write(failure.IsNew);
            }
        });

        // ------------------------------------------------------------------ decode

        public static MessageType GetType(byte[] payload)
        {
            if (payload == null || payload.Length == 0) throw new InvalidDataException("Empty message.");
            return (MessageType)payload[0];
        }

        public static Hello DecodeHello(byte[] payload) => Read(payload, MessageType.Hello, r => new Hello { Version = r.ReadInt32(), Code = r.ReadString() });

        public static string DecodeReject(byte[] payload) => Read(payload, MessageType.Reject, r => r.ReadString());

        public static List<Change> DecodeChanges(byte[] payload) => Read(payload, MessageType.Changes, ReadChanges);

        public static ValidationState DecodeValidationState(byte[] payload) =>
            Read(payload, MessageType.ValidationState, r => new ValidationState { IsAvailable = r.ReadBoolean(), NewFailureCount = r.ReadInt32() });

        public static List<Failure> DecodeValidateResult(byte[] payload) => Read(payload, MessageType.ValidateResult, r =>
        {
            var count = ReadCount(r);
            var failures = new List<Failure>(count);
            for (var i = 0; i < count; i++) failures.Add(new Failure { TableName = r.ReadString(), Key = r.ReadString(), Message = r.ReadString(), IsNew = r.ReadBoolean() });
            return failures;
        });

        public static Welcome DecodeWelcome(byte[] payload) => Read(payload, MessageType.Welcome, r =>
        {
            var message = new Welcome { Version = r.ReadInt32(), MasterVersion = r.ReadString(), LabelsTsv = r.ReadString() };
            var tableCount = ReadCount(r);
            for (var i = 0; i < tableCount; i++)
            {
                var table = new Table
                {
                    TableName = r.ReadString(),
                    MemoryTableName = r.ReadString(),
                    RecordType = r.ReadString(),
                    KeyType = r.ReadString(),
                    Group = r.ReadString(),
                };
                var recordCount = ReadCount(r);
                for (var j = 0; j < recordCount; j++) table.Records.Add(ReadBytes(r));
                if (r.ReadBoolean())
                {
                    var nameCount = ReadCount(r);
                    table.DisplayNames = new List<string>(nameCount);
                    for (var j = 0; j < nameCount; j++) table.DisplayNames.Add(r.ReadString());
                }
                message.Tables.Add(table);
            }
            message.Overrides = ReadChanges(r);
            return message;
        });

        // ------------------------------------------------------------------ frames

        /// <summary>Writes one frame (length + payload).</summary>
        public static void WriteFrame(Stream stream, byte[] payload)
        {
            var header = BitConverter.GetBytes(payload.Length);
            if (!BitConverter.IsLittleEndian) Array.Reverse(header);
            stream.Write(header, 0, 4);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        /// <summary>Reads one frame; null when the stream ended.</summary>
        public static byte[] ReadFrame(Stream stream)
        {
            var header = new byte[4];
            if (!ReadExactly(stream, header, 4)) return null;
            if (!BitConverter.IsLittleEndian) Array.Reverse(header);
            var length = BitConverter.ToInt32(header, 0);
            if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException($"Invalid frame length {length}.");
            var payload = new byte[length];
            if (!ReadExactly(stream, payload, length)) throw new EndOfStreamException("The connection closed in the middle of a message.");
            return payload;
        }

        static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    if (offset == 0) return false;
                    throw new EndOfStreamException("The connection closed in the middle of a message.");
                }
                offset += read;
            }
            return true;
        }

        // ------------------------------------------------------------------ helpers

        static byte[] Write(MessageType type, Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((byte)type);
                write(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        static T Read<T>(byte[] payload, MessageType type, Func<BinaryReader, T> read)
        {
            if (GetType(payload) != type) throw new InvalidDataException($"{type} expected, got {GetType(payload)}.");
            using (var reader = new BinaryReader(new MemoryStream(payload, 1, payload.Length - 1), Encoding.UTF8))
            {
                return read(reader);
            }
        }

        static void WriteChanges(BinaryWriter w, List<Change> changes)
        {
            w.Write(changes.Count);
            foreach (var change in changes)
            {
                w.Write((byte)change.Kind);
                w.Write(change.TableName ?? string.Empty);
                WriteBytes(w, change.Record);
            }
        }

        static List<Change> ReadChanges(BinaryReader r)
        {
            var count = ReadCount(r);
            var changes = new List<Change>(count);
            for (var i = 0; i < count; i++)
            {
                var kind = (ChangeKind)r.ReadByte();
                if (kind > ChangeKind.Delete) throw new InvalidDataException($"Unknown change kind {(byte)kind}.");
                changes.Add(new Change { Kind = kind, TableName = r.ReadString(), Record = ReadBytes(r) });
            }
            return changes;
        }

        static void WriteBytes(BinaryWriter w, byte[] bytes)
        {
            bytes ??= Array.Empty<byte>();
            w.Write(bytes.Length);
            w.Write(bytes);
        }

        static byte[] ReadBytes(BinaryReader r)
        {
            var length = ReadCount(r);
            var bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("Truncated message.");
            return bytes;
        }

        static int ReadCount(BinaryReader r)
        {
            var count = r.ReadInt32();
            if (count < 0 || count > MaxFrameBytes) throw new InvalidDataException($"Invalid count {count}.");
            return count;
        }
    }
}
