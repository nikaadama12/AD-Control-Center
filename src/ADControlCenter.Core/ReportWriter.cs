using System.Net;
using System.Text;
using System.Text.Json;

namespace ADControlCenter.Core;

internal static class ReportWriter
{
    internal static string Write(DirectoryData data, ReportKind kind, ReportFormat format)
    {
        string[] headers = kind == ReportKind.Users ? ["DisplayName", "Username", "Department", "Status"]
            : ["Name", "OperatingSystem", "Department", "LastSeenUtc", "Status"];
        IEnumerable<string[]> records = kind == ReportKind.Users
            ? data.Users.Select(u => new[] { u.DisplayName, u.Username, u.Department, u.Status })
            : data.Computers.Select(c => new[] { c.Name, c.OperatingSystem, c.Department, c.LastSeen.UtcDateTime.ToString("O"), c.Status });
        string content = format switch
        {
            ReportFormat.Csv => string.Join("\r\n", records.Prepend(headers).Select(row => string.Join(",", row.Select(QuoteCsv)))) + "\r\n",
            ReportFormat.Json => JsonSerializer.Serialize(records.Select(row => headers.Zip(row).ToDictionary(p => p.First, p => p.Second)),
                new JsonSerializerOptions { WriteIndented = true }),
            ReportFormat.Html => $"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>DEMO {kind} report</title>" +
                "<style>body{font:15px system-ui;margin:3rem;color:#18232a}table{border-collapse:collapse}td,th{padding:8px 16px;border:1px solid #ccc;text-align:left}</style>" +
                $"<h1>DEMO {kind} report</h1><p>northstar.example · Fictional directory</p><table><thead><tr>" +
                string.Concat(headers.Select(h => $"<th>{h}</th>")) + "</tr></thead><tbody>" +
                string.Concat(records.Select(row => "<tr>" + string.Concat(row.Select(v => $"<td>{WebUtility.HtmlEncode(v)}</td>")) + "</tr>")) +
                "</tbody></table></html>",
            _ => throw new DirectoryException("Unsupported report format.")
        };
        string folder = data.Settings.ExportDirectory;
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"demo-{kind.ToString().ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{format.ToString().ToLowerInvariant()}");
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
