using System.Net;
using System.Text;
using System.Text.Json;

namespace ADControlCenter.Core;

public static class ReportWriter
{
    internal static string Write(DirectoryData data, ReportKind kind, ReportFormat format)
    {
        string[] headers = kind == ReportKind.Users ? ["DisplayName", "Username", "Department", "Status"]
            : ["Name", "OperatingSystem", "Department", "LastSeenUtc", "Status"];
        IEnumerable<string[]> records = kind == ReportKind.Users
            ? data.Users.Select(u => new[] { u.DisplayName, u.Username, u.Department, u.Status })
            : data.Computers.Select(c => new[] { c.Name, c.OperatingSystem, c.Department, c.LastSeen.UtcDateTime.ToString("O"), c.Status });
        return WriteRows(data.Settings.ExportDirectory, "demo", kind, format, headers, records, "northstar.example · Fictional directory");
    }

    public static string WriteLive(string folder, IReadOnlyList<Live.LiveObject> snapshot, ReportKind kind, ReportFormat format)
    {
        if (format is not (ReportFormat.Csv or ReportFormat.Html)) throw new DirectoryException("Choose CSV or HTML for a live report.");
        for (var dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")) ||
                dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new DirectoryException("Live exports require a private location outside Git and linked directories.");
        }
        var entries = snapshot.Where(o => o.Kind == (kind == ReportKind.Users ? Live.ObjectKind.Users : Live.ObjectKind.Computers));
        string[] headers = ["Name", "Account", "Parent", "Status", kind == ReportKind.Users ? "Email" : "OperatingSystem"];
        var rows = entries.Select(o => new[] { o.Name, o.Account, o.Parent, o.Status, kind == ReportKind.Users ? o.Email : o.Platform });
        return WriteRows(folder, "live", kind, format, headers, rows, "Private · Live directory snapshot");
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
