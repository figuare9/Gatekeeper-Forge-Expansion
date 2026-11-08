using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gatekeeper.ModManager;

public sealed record NewsArticle(string Id, string Title, string Kind, string Date, string Version, string File)
{
    public override string ToString() => Title;
}
public sealed record NewsExpansion(string Id, string Title, NewsArticle[] Articles);
public sealed record NewsIndex(int SchemaVersion, string FeaturedArticleId, NewsExpansion[] Expansions);
public sealed record NewsBlock(int HeadingLevel, bool Bullet, string Text);

// A versioned editorial catalog, independent of install receipts, unlocks and
// gameplay compatibility. Adding an expansion or article requires content only.
public sealed class ExpansionNews
{
    private const string Prefix = "Gatekeeper.News.";
    private readonly Dictionary<string, string> articles = new(StringComparer.Ordinal);
    public NewsIndex Index { get; }
    public static ExpansionNews Bundled { get; } = LoadBundled();
    public NewsArticle Featured => Index.Expansions.SelectMany(e => e.Articles).Single(a => a.Id == Index.FeaturedArticleId);

    internal ExpansionNews(string catalog, Func<string, string> read)
    {
        Index = JsonSerializer.Deserialize<NewsIndex>(catalog, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Expansion news catalog is missing.");
        if (Index.SchemaVersion != 1 || Index.Expansions is not { Length: > 0 })
            throw new InvalidDataException("Unsupported expansion news catalog.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var expansions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expansion in Index.Expansions)
        {
            if (expansion == null || !ValidId(expansion.Id) || !expansions.Add(expansion.Id) ||
                string.IsNullOrWhiteSpace(expansion.Title) || expansion.Articles is not { Length: > 0 })
                throw new InvalidDataException("Invalid expansion in news catalog.");
            foreach (var article in expansion.Articles)
            {
                if (article == null || !ValidId(article.Id) || !ids.Add(article.Id) ||
                    string.IsNullOrWhiteSpace(article.Title) || string.IsNullOrWhiteSpace(article.Kind) ||
                    !DateOnly.TryParseExact(article.Date, "yyyy-MM-dd", out _) || !Version.TryParse(article.Version, out _) ||
                    !Regex.IsMatch(article.File ?? "", @"^[a-z0-9][a-z0-9-]*\.md$"))
                    throw new InvalidDataException("Invalid article in news catalog.");
                var markdown = read(article.File!);
                if (string.IsNullOrWhiteSpace(markdown) || !markdown.StartsWith("# " + article.Title + "\n", StringComparison.Ordinal))
                    throw new InvalidDataException("News article title is missing or does not match: " + article.Id);
                articles.Add(article.Id, markdown);
            }
        }
        if (!articles.ContainsKey(Index.FeaturedArticleId ?? "")) throw new InvalidDataException("Featured expansion article was not found.");
    }
    private static bool ValidId(string? id) => Regex.IsMatch(id ?? "", "^[a-z0-9][a-z0-9-]*$");
    private static ExpansionNews LoadBundled()
    {
        var assembly = typeof(ExpansionNews).Assembly;
        string Read(string name)
        {
            using var stream = assembly.GetManifestResourceStream(Prefix + name) ?? throw new InvalidDataException("Bundled news is missing: " + name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Replace("\r\n", "\n");
        }
        return new(Read("catalog.json"), Read);
    }
    public string Markdown(NewsArticle article) => articles[article.Id];

    // Deliberately small Markdown subset: headings, paragraphs, flat lists,
    // bold emphasis. No active HTML, scripts, remote links or dependencies.
    public static IReadOnlyList<NewsBlock> Blocks(string markdown)
    {
        var result = new List<NewsBlock>();
        var paragraph = new StringBuilder();
        void Flush() { if (paragraph.Length > 0) { result.Add(new(0, false, paragraph.ToString())); paragraph.Clear(); } }
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var heading = Regex.Match(line, @"^(#{1,3}) (.+)$");
            if (line.Length == 0) { Flush(); continue; }
            if (heading.Success) { Flush(); result.Add(new(heading.Groups[1].Length, false, heading.Groups[2].Value)); }
            else if (line.StartsWith("- ", StringComparison.Ordinal)) { Flush(); result.Add(new(0, true, line[2..])); }
            else { if (paragraph.Length > 0) paragraph.Append(' '); paragraph.Append(line); }
        }
        Flush(); return result.AsReadOnly();
    }
    public static string PlainText(string markdown) => string.Join("\n\n", Blocks(markdown).Select(b =>
        (b.Bullet ? "• " : "") + b.Text.Replace("**", "")));

    public static string Rtf(string markdown)
    {
        var output = new StringBuilder(@"{\rtf1\ansi\deff0\uc1{\fonttbl{\f0 Segoe UI;}}{\colortbl;\red236\green241\blue247;\red80\green218\blue235;\red255\green175\blue94;}");
        foreach (var block in Blocks(markdown))
        {
            output.Append(@"\pard\f0\cf1\fs23\sl330\slmult1\sa180 ");
            if (block.HeadingLevel == 1) output.Append(@"\cf1\fs38\b\sa260 ");
            else if (block.HeadingLevel > 1) output.Append(@"\cf2\fs28\b\sb240\sa160 ");
            else if (block.Bullet) output.Append(@"\li270\fi-230 ").Append(Escape("•  "));
            var segments = block.Text.Split("**", StringSplitOptions.None);
            for (var i = 0; i < segments.Length; i++)
            {
                if (block.HeadingLevel == 0) output.Append(i % 2 == 1 ? @"\b\cf3 " : @"\b0\cf1 ");
                output.Append(Escape(segments[i]));
            }
            output.Append(@"\b0\par ");
        }
        return output.Append('}').ToString();
    }
    private static string Escape(string value)
    {
        var result = new StringBuilder();
        foreach (var c in value)
            if (c is '\\' or '{' or '}') result.Append('\\').Append(c);
            else if (c > 127) result.Append(@"\u").Append((short)c).Append('?');
            else if (!char.IsControl(c)) result.Append(c);
        return result.ToString();
    }
}
