namespace Gatekeeper.ModManager;

internal sealed class NewsPanel : UserControl
{
    private readonly ExpansionNews news = ExpansionNews.Bundled;
    private readonly ComboBox expansions = Picker("Expansion"), articles = Picker("Article"), sections = Picker("Jump to section");
    private readonly Label meta = MainForm.LabelOf("", 9, MainForm.Muted);
    private readonly RichTextBox reader = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = MainForm.Card, ForeColor = Color.White, DetectUrls = false,
        Font = new Font("Segoe UI", 11.5f), ScrollBars = RichTextBoxScrollBars.Vertical,
        AccessibleName = "Expansion article", ShortcutsEnabled = true
    };
    private NewsArticle? selected;
    public NewsPanel()
    {
        Dock = DockStyle.Fill; BackColor = MainForm.Card; Padding = new Padding(20, 14, 20, 12);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in new[] { 24, 36, 36, 30, 36 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        top.ColumnStyles.Add(new(SizeType.Percent, 100)); top.ColumnStyles.Add(new(SizeType.Absolute, 108));
        var title = MainForm.LabelOf("EXPANSION NEWS", 10, MainForm.Accent, true); title.Dock = DockStyle.Fill;
        var copy = new LinkLabel { Text = "Copy article", Dock = DockStyle.Fill, LinkColor = MainForm.Accent, ActiveLinkColor = Color.White,
            VisitedLinkColor = MainForm.Accent, TextAlign = ContentAlignment.TopRight, AccessibleName = "Copy full article", Margin = Padding.Empty };
        top.Controls.Add(title, 0, 0); top.Controls.Add(copy, 1, 0);
        layout.Controls.Add(top, 0, 0); layout.Controls.Add(expansions, 0, 1); layout.Controls.Add(articles, 0, 2);
        meta.Dock = DockStyle.Fill; meta.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(meta, 0, 3);
        layout.Controls.Add(sections, 0, 4); layout.Controls.Add(reader, 0, 5); Controls.Add(layout);
        expansions.DisplayMember = nameof(NewsExpansion.Title);
        foreach (var expansion in news.Index.Expansions) expansions.Items.Add(expansion);
        expansions.SelectedIndexChanged += (_, _) =>
        {
            if (expansions.SelectedItem is not NewsExpansion expansion) return;
            articles.Items.Clear(); foreach (var article in expansion.Articles) articles.Items.Add(article);
            articles.SelectedIndex = 0;
        };
        articles.SelectedIndexChanged += (_, _) =>
        {
            if (articles.SelectedItem is not NewsArticle article) return;
            selected = article;
            meta.Text = $"{article.Kind}  ·  {article.Date}  ·  v{article.Version}";
            reader.Rtf = ExpansionNews.Rtf(news.Markdown(article));
            sections.Items.Clear(); sections.Items.Add("Jump to a section…");
            foreach (var block in ExpansionNews.Blocks(news.Markdown(article)).Where(b => b.HeadingLevel > 1)) sections.Items.Add(block.Text);
            sections.SelectedIndex = 0; reader.Select(0, 0); reader.ScrollToCaret();
        };
        sections.SelectedIndexChanged += (_, _) =>
        {
            if (sections.SelectedIndex <= 0) return;
            var index = reader.Find(sections.Text, 0, reader.TextLength, RichTextBoxFinds.MatchCase);
            if (index < 0) return;
            reader.Select(index, 0); reader.ScrollToCaret(); reader.Focus();
        };
        copy.LinkClicked += (_, _) =>
        {
            if (selected == null) return;
            try { Clipboard.SetText(ExpansionNews.PlainText(news.Markdown(selected))); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "The clipboard is busy. Please try again.", "Copy article"); }
        };
        expansions.SelectedItem = news.Index.Expansions.Single(e => e.Articles.Any(a => a.Id == news.Featured.Id));
        articles.SelectedItem = news.Featured;
    }
    private static ComboBox Picker(string name) => new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
        BackColor = MainForm.Background, ForeColor = Color.White, Font = new Font("Segoe UI", 10),
        Margin = new Padding(0, 0, 0, 5), AccessibleName = name, IntegralHeight = false, DropDownHeight = 360
    };
}
