using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace KaralamaDefteri;

internal static class SyntaxColors
{
    private static readonly Dictionary<(string, bool), IHighlightingDefinition> Cache = [];
    public static IHighlightingDefinition? Get(string syntax, bool dark)
    {
        if (syntax == "text") return null;
        if (Cache.TryGetValue((syntax, dark), out var existing)) return existing;
        var keywords = syntax switch
        {
            "sql" => @"(?i)\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|INTO|VALUES|SET|CREATE|TABLE|ORDER|BY|GROUP|WITH|AS|AND|OR|NULL)\b",
            "powershell" => @"(?i)\$\w+|\b(?:[a-z]+-[a-z][\w-]*|if|else|foreach|function|param|return)\b",
            "ini" => @"^\s*\[[^\]]+\]|^[\w.-]+(?=\s*=)",
            "log" => @"(?i)\b(?:TRACE|DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL|CRITICAL)\b",
            "xml" => @"</?[\w:.-]+|/?>|\b[\w:.-]+(?=\s*=)",
            _ => @"\b(true|false|null)\b"
        };
        var comment = syntax switch
        {
            "sql" => @"--.*$", "ini" => @"^\s*[;#].*$",
            "powershell" => @"#.*$", "xml" => @"<!--.*?-->", _ => @"(?!)"
        };
        var colors = dark ? new[] { "#82B5FF", "#A8DCA0", "#9AAAC1", "#F9B67E" } :
            new[] { "#214A9C", "#27672D", "#53647B", "#973A04" };
        var xml = $"""
            <SyntaxDefinition name="{syntax}" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Keyword" foreground="{colors[0]}" fontWeight="bold"/>
              <Color name="String" foreground="{colors[1]}"/>
              <Color name="Comment" foreground="{colors[2]}"/>
              <Color name="Number" foreground="{colors[3]}"/>
              <RuleSet>
                <Rule color="Comment">{System.Security.SecurityElement.Escape(comment)}</Rule>
                <Rule color="String">&quot;(?:[^&quot;\\]|\\.)*&quot;|'(?:[^'\\]|\\.)*'</Rule>
                <Rule color="Keyword">{System.Security.SecurityElement.Escape(keywords)}</Rule>
                <Rule color="Number">\b\d+(?:[.:-]\d+)*\b</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        return Cache[(syntax, dark)] = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
