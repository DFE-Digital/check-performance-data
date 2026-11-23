namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>
/// The rules for a folder name. A name is trimmed and must be 1 to <see cref="MaxLength"/>
/// characters. It may not hold a slash, because content staging writes a folder's place as a path
/// of names, or a control character. Two folders in the same place may not share a name, ignoring
/// case; the database checks that (see <see cref="IMediaFolderRepository"/>).
/// </summary>
public static class MediaFolderNames
{
    public const int MaxLength = 100;

    public const string Missing = "Enter a folder name";
    public static readonly string TooLong = $"Folder name must be {MaxLength} characters or fewer";
    public const string BadCharacter = "Folder name must not contain a slash or a control character";

    /// <summary>The name to store, or the error to show. Exactly one of the two is set.</summary>
    public static (string? Name, string? Error) Check(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return (null, Missing);
        if (trimmed.Length > MaxLength) return (null, TooLong);
        if (trimmed.Any(c => c is '/' or '\\' || char.IsControl(c))) return (null, BadCharacter);
        return (trimmed, null);
    }

    public static string Taken(string name) => $"There is already a folder called ‘{name}’ in this folder";
}
