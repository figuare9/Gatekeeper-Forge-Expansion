using System.Text.Json;
using Gatekeeper.ModManager;

internal static class ExpansionNewsChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string> fails)
    {
        var news = ExpansionNews.Bundled;
        var forge = news.Index.Expansions.SingleOrDefault(e => e.Id == "forge")?.Articles.SingleOrDefault(a => a.Id == "forge-complete");
        check(forge != null && news.Index.Expansions.SelectMany(e => e.Articles).Any(a => a.Id == news.Index.FeaturedArticleId), "The full Forge article or configured featured entry is missing.");
        var full = news.Markdown(forge!);
        foreach (var subject in new[] { "Cannon Wheel", "Item Exchange", "Prism Jackpot", "Rivet Cannon", "Volatile Charge", "Aegis Field", "Field Engineer", "Aspect of Chaos", "Aspect of Scaling", "Aspect of Elite", "Ascendant", "Seven more songs", "controller", "performance", "Ethereal Bridge", "25 stacks" })
            check(full.Contains(subject, StringComparison.OrdinalIgnoreCase), "Complete expansion notes omit " + subject);
        check(full.Contains("regardless of stage",StringComparison.OrdinalIgnoreCase) && !full.Contains("Stage 7",StringComparison.OrdinalIgnoreCase),"Current expansion notes still require Stage 7 to discover Forge.");
        check(news.Index.Expansions.SelectMany(e => e.Articles).All(a => news.Markdown(a).StartsWith("# " + a.Title + "\n")), "A bundled article has no matching headline.");
        check(Version.Parse(ExpansionKit.Version) >= Version.Parse(ExpansionKit.ContentVersion), "The installer version is older than its bundled gameplay.");
        // A second expansion and any number of updates are editorial additions;
        // no feature switches, game install or per-expansion UI code are needed.
        var first = new NewsArticle("one", "First", "Complete expansion notes", "2026-11-07", "1.0.0", "one.md");
        var second = new NewsArticle("two", "Second", "Expansion announcement", "2027-01-01", "2.0.0", "two.md");
        var index = new NewsIndex(1, "two", new[] { new NewsExpansion("first", "First expansion", new[] { first }), new NewsExpansion("second", "Second expansion", new[] { second }) });
        string Read(string name) => name switch { "one.md" => "# First\n\nOriginal article.\n", "two.md" => "# Second\n\nNew expansion.\n", _ => throw new FileNotFoundException() };
        var next = new ExpansionNews(JsonSerializer.Serialize(index), Read);
        check(next.Featured == second && next.Index.Expansions.Length == 2 && next.Markdown(first).Contains("Original article"), "Adding a future expansion lost the original article or ignored the featured entry.");
        fails(() => new ExpansionNews(JsonSerializer.Serialize(index with { SchemaVersion = 2 }), Read), "Unsupported news schema was silently accepted.");
        fails(() => new ExpansionNews(JsonSerializer.Serialize(index with { FeaturedArticleId = "absent" }), Read), "A missing featured article was accepted.");
        fails(() => new ExpansionNews(JsonSerializer.Serialize(index with { Expansions = new[] { index.Expansions[0], index.Expansions[0] } }), Read), "Duplicate expansion IDs were accepted.");
        var duplicate = index with { Expansions = new[] { index.Expansions[0], new NewsExpansion("second", "Other", new[] { first }) } };
        fails(() => new ExpansionNews(JsonSerializer.Serialize(duplicate), Read), "Duplicate article IDs were accepted.");
        foreach (var file in new[] { "../secret.md", "https://example.org/file.md", "folder/file.md", "file.rtf" })
        {
            var bad = index with { Expansions = new[] { new NewsExpansion("first", "First", new[] { first with { File = file } }) } };
            fails(() => new ExpansionNews(JsonSerializer.Serialize(bad), Read), "News accepted an external/path-traversing article.");
        }
        fails(() => new ExpansionNews(JsonSerializer.Serialize(index), _ => "# Wrong headline\n"), "Mismatched article content was accepted.");
        fails(() => new ExpansionNews(JsonSerializer.Serialize(index), _ => throw new FileNotFoundException()), "Missing article content was silently omitted.");
        var markdown = "# Forge\r\n\r\nFirst **bold**\r\ncontinued.\r\n\r\n## Shields\r\n\r\n- Blue — 50%\r\n- Cyan {\\rtf1} 😀\r\n";
        var blocks = ExpansionNews.Blocks(markdown);
        check(blocks.Count == 5 && blocks[1].Text == "First **bold** continued." && blocks[2].HeadingLevel == 2 && blocks[4].Bullet, "News paragraph/list/section boundaries are broken.");
        var rtf = ExpansionNews.Rtf(markdown);
        check(rtf.Contains(@"\{\\rtf1\}") && rtf.Contains(@"\u8212?") && rtf.Contains(@"\u-10179?\u-8704?"), "News renderer lost Unicode or allowed article text to inject RTF formatting.");
        check(!ExpansionNews.PlainText(markdown).Contains("**") && ExpansionNews.PlainText(markdown).Contains("• Blue — 50%"), "Copyable article text contains markup or lost bullets.");
    }
}
