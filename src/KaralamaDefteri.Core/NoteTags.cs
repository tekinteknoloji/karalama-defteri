using System.Text.RegularExpressions;

namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    private static readonly Regex TagPattern = new(@"(?<![\p{L}\p{N}_])#([\p{L}\p{N}_-]+)", RegexOptions.Compiled);
    private static readonly Regex FilterPattern = new(@"(?<!\S)(?:#([\p{L}\p{N}_-]+)|renk:(\S+)|arşiv:(\S*)|resim:(\S*))(?=\s|$)", RegexOptions.Compiled);

    public string[] Tags(long? note = null)
    {
        using var command = Command("""
            SELECT t.name FROM tags t WHERE EXISTS (
              SELECT 1 FROM note_tags nt JOIN notes n ON n.id=nt.note_id
              WHERE nt.tag_id=t.id AND n.deleted_at IS NULL AND ($note IS NULL OR n.id=$note))
            ORDER BY t.name
            """, ("$note", note));
        using var rows = command.ExecuteReader();
        var names = new List<string>();
        while (rows.Read()) names.Add(rows.GetString(0));
        return names.ToArray();
    }

    public string[] TagNames(string content) => TagPattern.Matches(content)
        .Select(match => Fold(match.Groups[1].Value)).Distinct().Order().ToArray();

    internal static string RemoveTag(string content, string tag) => TagPattern.Replace(content,
        match => Fold(match.Groups[1].Value) == tag ? "" : match.Value);

    private void SyncTags(long id, string content)
    {
        Execute("DELETE FROM note_tags WHERE note_id=$id", ("$id", id));
        foreach (var name in TagNames(content))
        {
            Execute("INSERT INTO tags(name) VALUES($name) ON CONFLICT(name) DO NOTHING", ("$name", name));
            Execute("INSERT INTO note_tags(note_id,tag_id) SELECT $id,id FROM tags WHERE name=$name",
                ("$id", id), ("$name", name));
        }
    }
}
