using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KaralamaDefteri;

public static class Editing
{
    public static string Detect(string text)
    {
        var start = text.AsSpan().TrimStart();
        if (start.IsEmpty) return "text";
        if (start[0] is '{' or '[')
        {
            try { using var json = JsonDocument.Parse(text); return "json"; }
            catch (JsonException) { }
        }
        if (start[0] == '<')
        {
            try { ReadXml(text); return "xml"; }
            catch (XmlException) { }
        }
        var sample = start[..Math.Min(start.Length, 8192)].ToString();
        if (Regex.IsMatch(sample, @"^\[[^\]\r\n]+\][ \t]*\r?\n(?:[^\n]*\n)*?[ \t]*[\w.-]+\s*=")) return "ini";
        if (Regex.IsMatch(sample, @"^(?:\[?\d{4}[-/.]\d{2}[-/.]\d{2}[ T])?\[?\d{2}:\d{2}:\d{2}")) return "log";
        if (Regex.IsMatch(sample, @"^(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|WITH|DROP)\b", RegexOptions.IgnoreCase)) return "sql";
        if (Regex.IsMatch(sample, @"^(?:\$[\w{]|(?:Get|Set|New|Remove|Write|Invoke|Import|Start|Stop|Add|ConvertTo|Test)-\w)", RegexOptions.IgnoreCase)) return "powershell";
        return "text";
    }

    private static XDocument ReadXml(string text)
    {
        using var source = new StringReader(text);
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader);
    }

    public static string Format(string text, string syntax)
    {
        if (syntax == "json")
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        if (syntax == "xml")
        {
            var document = ReadXml(text);
            return document.Declaration is null ? document.ToString() : $"{document.Declaration}{Environment.NewLine}{document}";
        }
        throw new InvalidOperationException("Biçimlendirme JSON ve XML için kullanılabilir.");
    }

    public static Regex Pattern(string query, bool regex, bool whole, bool matchCase)
    {
        var pattern = regex ? query : Regex.Escape(query);
        if (whole) pattern = $@"(?<!\w)(?:{pattern})(?!\w)";
        return new Regex(pattern, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
    }
}
