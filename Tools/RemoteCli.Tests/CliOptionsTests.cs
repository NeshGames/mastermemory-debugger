using NUnit.Framework;

[TestFixture]
public sealed class CliOptionsTests
{
    [Test]
    public void ParseRecords_ShouldKeepPagingAndTable()
    {
        var options = CliOptions.Parse(new[]
        {
            "records",
            "--host", "127.0.0.1",
            "--code", "123456",
            "--table", "Skill",
            "--offset", "20",
            "--limit", "50",
        });

        Assert.AreEqual("records", options.Command);
        Assert.AreEqual("Skill", options.Table);
        Assert.AreEqual(20, options.Offset);
        Assert.AreEqual(50, options.Limit);
    }

    [Test]
    public void ParseRecords_ShouldRequireTable()
    {
        var error = Assert.Throws<CliError>(() => CliOptions.Parse(new[]
        {
            "records",
            "--host", "127.0.0.1",
            "--code", "123456",
        }));

        Assert.AreEqual("USAGE", error.Code);
        StringAssert.Contains("--table", error.Message);
    }

    [Test]
    public void ParsePatchApply_ShouldRequireTransactionIdentity()
    {
        var error = Assert.Throws<CliError>(() => CliOptions.Parse(new[]
        {
            "patch-apply",
            "--host", "127.0.0.1",
            "--code", "123456",
            "--file", "patch.json",
        }));

        Assert.AreEqual("USAGE", error.Code);
        StringAssert.Contains("--plan-sha", error.Message);
    }
}
