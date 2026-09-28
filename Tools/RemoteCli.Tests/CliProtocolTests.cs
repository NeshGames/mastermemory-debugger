using System.IO;
using Nesh.MasterMemoryDebugger;
using NUnit.Framework;

[TestFixture]
public sealed class CliProtocolTests
{
    [Test]
    public void ReadPatchResponse_ShouldSkipUnrelatedMessagesAndRequestIds()
    {
        using var stream = new MemoryStream();
        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.ValidationState()));
        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.PatchResponse
            {
                RequestId = "other",
                Status = MasterMemoryRemoteProtocol.PatchStatus.Success,
            }));
        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.PatchResponse
            {
                RequestId = "wanted",
                Status = MasterMemoryRemoteProtocol.PatchStatus.Success,
                PlanSha = "plan",
            }));
        stream.Position = 0;

        var response = CliProtocol.ReadPatchResponse(stream, "wanted");

        Assert.AreEqual("wanted", response.RequestId);
        Assert.AreEqual("plan", response.PlanSha);
    }

    [Test]
    public void ReadTable_ShouldCollectOrderedChunks()
    {
        var manifest = new MasterMemoryRemoteProtocol.Table
        {
            TableName = "Skill",
            RecordCount = 2,
            HasCustomDisplayName = true,
        };
        using var stream = new MemoryStream();
        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.TableChunk
            {
                RequestId = "table",
                TableName = "Skill",
                ChunkIndex = 0,
                Records = { new byte[] { 1 } },
                DisplayNames = new System.Collections.Generic.List<string> { "A" },
            }));
        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.TableChunk
            {
                RequestId = "table",
                TableName = "Skill",
                ChunkIndex = 1,
                IsLast = true,
                Records = { new byte[] { 2 } },
                DisplayNames = new System.Collections.Generic.List<string> { "B" },
            }));
        stream.Position = 0;

        var table = CliProtocol.ReadTable(stream, manifest, "table");

        Assert.AreEqual(2, table.Records.Count);
        CollectionAssert.AreEqual(new[] { "A", "B" }, table.DisplayNames);
    }

    [Test]
    public void ReadTable_ShouldRejectOutOfOrderChunks()
    {
        var manifest = new MasterMemoryRemoteProtocol.Table
        {
            TableName = "Skill",
            RecordCount = 1,
        };
        using var stream = new MemoryStream();
        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.TableChunk
            {
                RequestId = "table",
                TableName = "Skill",
                ChunkIndex = 1,
                IsLast = true,
                Records = { new byte[] { 1 } },
            }));
        stream.Position = 0;

        var error = Assert.Throws<CliError>(() => CliProtocol.ReadTable(stream, manifest, "table"));

        Assert.AreEqual("PROTOCOL_ERROR", error.Code);
        StringAssert.Contains("out of order", error.Message);
    }
}
