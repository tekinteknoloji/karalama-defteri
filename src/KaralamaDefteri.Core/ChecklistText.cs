namespace KaralamaDefteri;

public static class ChecklistText
{
    public static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    public static bool Complete(string line) => line.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase);
    public static bool Item(string line) => Complete(line) || line.StartsWith("[ ] ", StringComparison.Ordinal);
    public static string Label(string line) => Item(line) ? line[4..] : line;
    public static string Convert(string text, bool checklist) => string.Join("\n", Lines(text).Select(line =>
        checklist ? Item(line) || string.IsNullOrWhiteSpace(line) || ImageMarkers.Pattern().IsMatch(line) ? line : "[ ] " + line : Label(line)));
    public static string Progress(string text)
    {
        var lines = Lines(text);
        return $"{lines.Count(Complete)}/{lines.Count(Item)}";
    }
}
