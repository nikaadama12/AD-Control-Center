namespace ADControlCenter.Core;

internal static class SampleDirectory
{
    internal static readonly string[] Units = ["Engineering", "Operations", "Finance", "People", "Design", "Support"];
    private static readonly string[] First = ["Avery", "Mira", "Rowan", "Elian", "Noa", "Iris", "Soren", "Remy", "Arden", "Liora", "Finn", "Nico", "Tala", "Eden", "Wren", "Kai"];
    private static readonly string[] Last = ["Vale", "Linden", "Marlow", "Aster", "Finch", "Hollis", "Reed", "Alden"];

    internal static DirectoryData Create(Preferences preferences)
    {
        var now = DateTimeOffset.UtcNow;
        var users = Enumerable.Range(0, 128).Select(i =>
        {
            string first = First[i % First.Length], last = Last[i / First.Length];
            return new DirectoryUser(Guid.NewGuid(), $"{first} {last}", $"{first}.{last}".ToLowerInvariant(),
                UnitAt(i, [38, 29, 21, 17, 13, 10]), i % 16 != 15, i % 32 == 6);
        }).ToArray();
        var computers = Enumerable.Range(0, 84).Select(i => new Computer(Guid.NewGuid(),
            $"NS-{(i % 7 == 0 ? "SRV" : "WS")}-{i + 1:000}", i % 7 == 0 ? "Windows Server 2022" : "Windows 11",
            UnitAt(i, [24, 20, 14, 11, 8, 7]), now.AddDays(-(i % 9 == 0 ? 95 + i : i % 30)).AddHours(-1))).ToArray();
        var groups = Enumerable.Range(0, 12).Select(i => new DirectoryGroup(Guid.NewGuid(),
            $"{Units[i % 6]} {(i < 6 ? "Team" : "Readers")}", $"Sample access group for {Units[i % 6]}.",
            users.Where(u => u.Department == Units[i % 6]).Where((_, n) => i < 6 || n % 3 == 0).Select(u => u.Id).ToArray())).ToArray();
        AuditEntry[] audit = [
            new(now.AddMinutes(-5), "Demo Operator", "Group updated", groups[0].Name, "Success", "Sample activity · Group membership updated."),
            new(now.AddMinutes(-17), "Demo Operator", "User created", users[0].Username, "Success", "Sample activity · Fictional user added."),
            new(now.AddMinutes(-38), "Demo Operator", "DEMO unlock", users[1].Username, "Success", "Sample activity · Account unlock simulated."),
            new(now.AddHours(-1), "Demo Operator", "Report exported", "Computer inventory", "Success", "Sample activity · Inventory report demonstration."),
            new(now.AddHours(-2), "Demo System", "Directory initialized", "northstar.example", "Success", "Fictional directory seeded.")
        ];
        return new(1, 0, users, computers, groups, audit, preferences);
    }

    private static string UnitAt(int index, int[] sizes)
    {
        for (int i = 0; i < sizes.Length; i++)
        {
            if (index < sizes[i]) return Units[i];
            index -= sizes[i];
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }
}
