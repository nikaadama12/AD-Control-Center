using ADControlCenter.Core.Live;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ADControlCenter.Core;

public static class ReportWriter
{
    internal static string Write(DirectoryData data, ReportKind kind, ReportFormat format)
    {
        var units = data.Users.Select(u => u.Department).Concat(data.Computers.Select(c => c.Department))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        if (kind == ReportKind.CountsOnly)
            return WriteCounts(data.Settings.ExportDirectory, "demo", format, data.Users.Count,
                data.Users.Count(u => u.Enabled), data.Users.Count(u => !u.Enabled), data.Groups.Count, data.Computers.Count, units.Count());

        (string[] Headers, IEnumerable<string[]> Rows) table = kind switch
        {
            ReportKind.Users => (["DisplayName", "Username", "Department", "Status"],
                data.Users.Select(u => new[] { u.DisplayName, u.Username, u.Department, u.Status })),
            ReportKind.Computers => (["Name", "OperatingSystem", "Department", "LastSeenUtc", "Status"],
                data.Computers.Select(c => new[] { c.Name, c.OperatingSystem, c.Department, c.LastSeen.UtcDateTime.ToString("O"), c.Status })),
            ReportKind.SecurityGroups => (["Name", "Description", "MemberCount"],
                data.Groups.Select(g => new[] { g.Name, g.Description, Number(g.MemberCount) })),
            ReportKind.OrganizationalUnits => (["OrganizationalUnit"], units.Select(unit => new[] { unit })),
            _ => throw new DirectoryException("Unknown report dataset.")
        };
        return WriteRows(data.Settings.ExportDirectory, "demo", kind, format, table.Headers, table.Rows, "northstar.example · Fictional directory");
    }

    public static string WriteLive(string folder, IReadOnlyList<LiveObject> snapshot, ReportKind kind, ReportFormat format)
    {
        if (format is not (ReportFormat.Csv or ReportFormat.Html)) throw new DirectoryException("Choose CSV or HTML for a live report.");
        for (var dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")) ||
                dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new DirectoryException("Live exports require a private location outside Git and linked directories.");
        }
        if (kind == ReportKind.CountsOnly)
        {
            var users = snapshot.Where(o => o.Kind == ObjectKind.Users).ToArray();
            return WriteCounts(folder, "live", format, users.Length, users.Count(u => u.Status is "Enabled" or "Locked"),
                users.Count(u => u.Status == "Disabled"), snapshot.Count(o => o.Kind == ObjectKind.Groups),
                snapshot.Count(o => o.Kind == ObjectKind.Computers), snapshot.Count(o => o.Kind == ObjectKind.OrganizationalUnits));
        }

        var entries = LiveDetailEntries(snapshot, kind);
        (string[] Headers, IEnumerable<string[]> Rows) table = kind switch
        {
            ReportKind.Users or ReportKind.Computers => (["Name", "Account", "Parent", "Status", kind == ReportKind.Users ? "Email" : "OperatingSystem"],
                entries.Select(o => new[] { o.Name, o.Account, o.Parent, o.Status, kind == ReportKind.Users ? o.Email : o.Platform })),
            ReportKind.SecurityGroups => (["Name", "Parent", "Description", "MemberCount"],
                entries.Select(g => new[] { g.Name, g.Parent, g.Description, Number(g.Members.Count) })),
            ReportKind.OrganizationalUnits => (["Name", "DistinguishedName", "Parent"],
                entries.Select(o => new[] { o.Name, o.DistinguishedName, o.Parent })),
            _ => throw new DirectoryException("Unknown report dataset.")
        };
        return WriteRows(folder, "live", kind, format, table.Headers, table.Rows, "Private · Live directory snapshot");
    }

    public static int LiveDetailCount(IReadOnlyList<LiveObject> snapshot, ReportKind kind) => LiveDetailEntries(snapshot, kind).Count();

    private static IEnumerable<LiveObject> LiveDetailEntries(IReadOnlyList<LiveObject> snapshot, ReportKind kind) => kind switch
    {
        ReportKind.Users => snapshot.Where(o => o.Kind == ObjectKind.Users),
        ReportKind.Computers => snapshot.Where(o => o.Kind == ObjectKind.Computers),
        ReportKind.SecurityGroups => snapshot.Where(o => o.Kind == ObjectKind.Groups && o.IsSecurityGroup),
        ReportKind.OrganizationalUnits => snapshot.Where(o => o.Kind == ObjectKind.OrganizationalUnits),
        _ => throw new DirectoryException("Choose a detailed report dataset.")
    };

    private static string Number(int count) => count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string WriteCounts(string folder, string mode, ReportFormat format, int users, int enabled,
        int disabled, int groups, int computers, int units)
    {
        string[][] rows = [
            ["Total users", Number(users)], ["Enabled users", Number(enabled)], ["Disabled users", Number(disabled)],
            ["Total groups", Number(groups)], ["Total computers", Number(computers)], ["Total OUs", Number(units)]
        ];
        return WriteRows(folder, mode, ReportKind.CountsOnly, format, ["Metric", "Count"], rows, "Aggregate directory counts");
    }

    private static string WriteRows(string folder, string mode, ReportKind kind, ReportFormat format,
        string[] headers, IEnumerable<string[]> records, string description)
    {
        string content = format switch
        {
            ReportFormat.Csv => string.Join("\r\n", records.Prepend(headers).Select(row => string.Join(",", row.Select(QuoteCsv)))) + "\r\n",
            ReportFormat.Json => JsonSerializer.Serialize(records.Select(row => headers.Zip(row).ToDictionary(p => p.First, p => p.Second)),
                new JsonSerializerOptions { WriteIndented = true }),
            ReportFormat.Html => $"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>{mode.ToUpperInvariant()} {kind} report</title>" +
                "<style>body{font:15px system-ui;margin:3rem;color:#18232a}table{border-collapse:collapse}td,th{padding:8px 16px;border:1px solid #ccc;text-align:left}</style>" +
                $"<h1>{mode.ToUpperInvariant()} {kind} report</h1><p>{description}</p><table><thead><tr>" +
                string.Concat(headers.Select(h => $"<th>{h}</th>")) + "</tr></thead><tbody>" +
                string.Concat(records.Select(row => "<tr>" + string.Concat(row.Select(v => $"<td>{WebUtility.HtmlEncode(v)}</td>")) + "</tr>")) +
                "</tbody></table></html>",
            _ => throw new DirectoryException("Unsupported report format.")
        };
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"{mode}-{kind.ToString().ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{format.ToString().ToLowerInvariant()}");
        string temporary = file + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(format == ReportFormat.Csv));
            File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return file;
    }

    private static string QuoteCsv(string value)
    {
        if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
