using Microsoft.VisualBasic.FileIO;

namespace ADControlCenter.Core;

internal static class CsvImport
{
    internal static IReadOnlyList<ImportRow> Parse(string csv, IEnumerable<DirectoryUser> users)
    {
        if (csv.Length > 2_000_000) throw new DirectoryException("Choose a CSV smaller than 2 MB.");
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF')))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        var rows = new List<ImportRow>();
        var names = users.Select(u => u.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            string[] required = ["DisplayName", "Username", "Department"];
            var header = parser.ReadFields() ?? [];
            if (header.Length != 3 || !required.All(h => header.Contains(h, StringComparer.OrdinalIgnoreCase)))
                throw new DirectoryException("Use exactly these CSV headers: DisplayName, Username, Department.");
            var order = required.Select(h => Array.FindIndex(header, f => f.Equals(h, StringComparison.OrdinalIgnoreCase))).ToArray();
            while (!parser.EndOfData)
            {
                if (rows.Count >= 5000) throw new DirectoryException("Import up to 5,000 rows at a time.");
                int line = checked((int)parser.LineNumber);
                var fields = parser.ReadFields() ?? [];
                if (fields.Length != 3)
                {
                    rows.Add(new(line, new("", "", ""), "Expected three fields."));
                    continue;
                }
                var input = new UserInput(fields[order[0]].Trim(), fields[order[1]].Trim().ToLowerInvariant(), fields[order[2]].Trim());
                string error = DemoDirectoryService.ValidateUser(input, names);
                names.Add(input.Username);
                rows.Add(new(line, input, error));
            }
        }
        catch (MalformedLineException e) { throw new DirectoryException($"Malformed CSV at line {e.LineNumber}. Check quoted fields."); }
        if (rows.Count == 0) throw new DirectoryException("The CSV contains no user rows.");
        return rows;
    }
}
