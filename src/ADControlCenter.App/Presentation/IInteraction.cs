using ADControlCenter.Core;

namespace ADControlCenter.App.Presentation;

public interface IInteraction
{
    bool Confirm(string title, string description, string accept);
    void Inform(string title, string description);
    bool EditUser(DirectoryUser? user, Action<UserInput> save);
    bool EditGroup(DirectoryGroup? group, IReadOnlyList<DirectoryUser> users, Func<string, string, IReadOnlyList<Guid>, bool> save);
    string? ChooseCsv();
    string? ChooseFolder(string current);
    void ApplyTheme(string theme);
}
