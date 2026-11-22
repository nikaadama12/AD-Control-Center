using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using ADControlCenter.Core;
using ADControlCenter.Core.Live;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.LiveTests;

[TestClass]
public sealed class ReportContentTests
{
    private string _folder = null!;
    [TestInitialize] public void Start() => _folder = Directory.CreateTempSubdirectory("adcc-reports-").FullName;
    [TestCleanup] public void Finish() => Directory.Delete(_folder, true);

    private DirectoryData Demo(string marker)
    {
        var u = new DirectoryUser(Guid.NewGuid(), marker + "Person", marker + "Login", marker + "Unit", true, false);
        return new(1, 0, [u, u with { Id = Guid.NewGuid(), Locked = true }, u with { Id = Guid.NewGuid(), Enabled = false }],
            [new(Guid.NewGuid(), marker + "Device", marker + "OS", marker + "OtherUnit", DateTimeOffset.UtcNow)],
            [new(Guid.NewGuid(), marker + "Team", marker + "Description", [u.Id])],
            [new(DateTimeOffset.Now, marker, marker, marker, marker, marker)], new("Dark", _folder));
    }

    private static LiveObject[] Live(string marker)
    {
        var u = new LiveObject(ObjectKind.Users, marker + "Person", $"CN={marker},OU={marker},DC={marker},DC=test",
            marker + "Login", "Enabled", marker + "OS", $"{marker}@{marker}.test", marker + "Description", $"{marker}.test",
            DateTimeOffset.UtcNow, [marker + "Member"], [marker + "ParentGroup"]);
        return [u, u with { Status = "Locked" }, u with { Status = "Disabled" }, u with { Status = "Unknown" },
            u with { Kind = ObjectKind.Groups, IsSecurityGroup = true, Name = marker + "Team" },
            u with { Kind = ObjectKind.Groups, Name = marker + "Distribution" },
            u with { Kind = ObjectKind.Computers, Name = marker + "Device" },
            u with { Kind = ObjectKind.OrganizationalUnits, Name = marker + "Unit" }];
    }

    [TestMethod]
    [DataRow(false, ReportFormat.Csv)]
    [DataRow(false, ReportFormat.Html)]
    [DataRow(false, ReportFormat.Json)]
    [DataRow(true, ReportFormat.Csv)]
    [DataRow(true, ReportFormat.Html)]
    public void CountsOnlyContainsExactlySixMetricsAndIsIndependentOfIdentifiers(bool live, ReportFormat format)
    {
        string Export(string marker) => File.ReadAllText(live
            ? ReportWriter.WriteLive(_folder, Live(marker), ReportKind.CountsOnly, format)
            : ReportWriter.Write(Demo(marker), ReportKind.CountsOnly, format));
        string body = Export("PrivateA");
        Assert.AreEqual(body, Export("192.0.2.43-S-1-5-21-123-PrivateB"), "Changing every identifying value must leave the entire report identical.");
        Assert.IsFalse(body.Contains("PrivateA"));
        Assert.IsFalse(body.Contains("northstar.example"));
        string[][] expected = [["Total users", live ? "4" : "3"], ["Enabled users", "2"], ["Disabled users", "1"],
            ["Total groups", live ? "2" : "1"], ["Total computers", "1"], ["Total OUs", live ? "1" : "2"]];
        switch (format)
        {
            case ReportFormat.Csv:
                var csv = expected.Prepend(["Metric", "Count"]).Select(row => $"\"{row[0]}\",\"{row[1]}\"");
                Assert.AreEqual(string.Join("\r\n", csv) + "\r\n", body);
                break;
            case ReportFormat.Html:
                var table = XElement.Parse(body[body.IndexOf("<table>", StringComparison.Ordinal)..body.IndexOf("</html>", StringComparison.Ordinal)]);
                CollectionAssert.AreEqual(new[] { "Metric", "Count" }, table.Descendants("th").Select(c => c.Value).ToArray());
                var rows = table.Descendants("tbody").Elements("tr").ToArray();
                Assert.AreEqual(6, rows.Length);
                for (int i = 0; i < rows.Length; i++) CollectionAssert.AreEqual(expected[i], rows[i].Elements("td").Select(c => c.Value).ToArray());
                break;
            case ReportFormat.Json:
                using (var json = JsonDocument.Parse(body))
                {
                    Assert.AreEqual(6, json.RootElement.GetArrayLength());
                    for (int i = 0; i < 6; i++)
                    {
                        var props = json.RootElement[i].EnumerateObject().ToArray();
                        CollectionAssert.AreEqual(new[] { "Metric", "Count" }, props.Select(p => p.Name).ToArray());
                        CollectionAssert.AreEqual(expected[i], props.Select(p => p.Value.GetString()).ToArray());
                    }
                }
                break;
        }
    }

    [TestMethod]
    [DataRow(false, ReportKind.Users, "FixturePerson", 3)]
    [DataRow(false, ReportKind.SecurityGroups, "FixtureTeam", 1)]
    [DataRow(false, ReportKind.Computers, "FixtureDevice", 1)]
    [DataRow(false, ReportKind.OrganizationalUnits, "FixtureOtherUnit", 2)]
    [DataRow(true, ReportKind.Users, "FixturePerson", 4)]
    [DataRow(true, ReportKind.SecurityGroups, "FixtureTeam", 1)]
    [DataRow(true, ReportKind.Computers, "FixtureDevice", 1)]
    [DataRow(true, ReportKind.OrganizationalUnits, "FixtureUnit", 1)]
    public void DetailedSelectionWritesTheChosenDataset(bool live, ReportKind kind, string expectedName, int rowCount)
    {
        string file = live ? ReportWriter.WriteLive(_folder, Live("Fixture"), kind, ReportFormat.Csv)
            : ReportWriter.Write(Demo("Fixture"), kind, ReportFormat.Csv);
        var rows = File.ReadAllLines(file).Skip(1).ToArray();
        Assert.AreEqual(rowCount, rows.Length);
        Assert.IsTrue(rows.Any(r => r.StartsWith($"\"{expectedName}\"", StringComparison.Ordinal)));
        Assert.IsFalse(rows.Any(r => r.Contains("FixtureDistribution")));
    }

    [TestMethod]
    public void EmptyInventoryStillWritesSixZeros()
    {
        string file = ReportWriter.WriteLive(_folder, [], ReportKind.CountsOnly, ReportFormat.Csv);
        var rows = File.ReadAllLines(file).Skip(1).ToArray();
        Assert.AreEqual(6, rows.Length);
        Assert.IsTrue(rows.All(r => r.EndsWith(",\"0\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CountsOnlyHonorsLiveGitWorktreeRestrictionBeforeCreatingDestination()
    {
        File.WriteAllText(Path.Combine(_folder, ".git"), "gitdir: fixture");
        string destination = Path.Combine(_folder, "reports");
        Assert.ThrowsExactly<DirectoryException>(() => ReportWriter.WriteLive(destination, [], ReportKind.CountsOnly, ReportFormat.Html));
        Assert.IsFalse(Directory.Exists(destination));
    }
}
