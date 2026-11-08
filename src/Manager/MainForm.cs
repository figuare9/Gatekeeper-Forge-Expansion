using System.Diagnostics;
using System.Reflection;

namespace Gatekeeper.ModManager;

public sealed class MainForm : Form
{
    internal static readonly Color Background = Color.FromArgb(16, 21, 29), Card = Color.FromArgb(25, 33, 44),
        Muted = Color.FromArgb(162, 177, 194), Accent = Color.FromArgb(80, 218, 235), Orange = Color.FromArgb(255, 153, 68),
        Line = Color.FromArgb(51, 65, 83);
    private GameService? game;
    private readonly Func<string, GameService> selectGame;
    private readonly string? preview;
    private Mutex? gameMutex;
    private GameInfo? info;
    private ExpansionStatus? status;
    private bool busy;
    private readonly TextBox location = new() { ReadOnly = true, BackColor = Card, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, TabStop = false };
    private readonly Label folderHint = LabelOf("Searching for Gatekeeper…", 9, Muted);
    private readonly Label heading = LabelOf("Checking installation…", 16, Color.White, true);
    private readonly Label detail = LabelOf("", 10, Muted), activity = LabelOf("", 10, Accent);
    private readonly Button primary = ButtonOf("Install expansion", true), choose = ButtonOf("Change folder"), support = ButtonOf("Support"), close = ButtonOf("Close");
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Visible = false };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };

    public MainForm(GameService? initialGame, Func<string, GameService> selectGame, string? preview)
    {
        this.selectGame = selectGame; this.preview = preview;
        Text = ExpansionKit.Name + " · Installer";
        BackColor = Background; ForeColor = Color.White; Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(1140, 820); MinimumSize = new Size(1030, 740);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32, 24, 32, 20), ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 144)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
        Controls.Add(layout); layout.Controls.Add(BuildHeader(), 0, 0); layout.Controls.Add(BuildBody(), 0, 1); layout.Controls.Add(BuildFooter(), 0, 2);
        primary.Click += async (_, _) => await InstallOrPlay();
        choose.Click += async (_, _) => await ChooseFolder(); close.Click += (_, _) => Close();
        support.Click += async (_, _) =>
        {
            if (game == null) return;
            using var help = new SupportForm(game); help.ShowDialog(this); await CheckInstallation();
        };
        timer.Tick += (_, _) => RefreshActions();
        Shown += async (_, _) =>
        {
            try { if (initialGame != null) BindGame(initialGame); }
            catch (Exception ex) { activity.Text = ex.Message; }
            await CheckInstallation(); timer.Start();
            if (preview != null)
            {
                await Task.Delay(300);
                using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(preview); Close();
            }
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        FormClosed += (_, _) => { timer.Dispose(); gameMutex?.ReleaseMutex(); gameMutex?.Dispose(); };
        RefreshActions();
    }

    private Panel BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        Place(panel, LabelOf("GATEKEEPER  /  COMMUNITY EXPANSION", 10, Accent, true), 0, 0, 660, 25);
        Place(panel, LabelOf("FORGE EXPANSION", 29, Color.White, true), 0, 33, 650, 58);
        Place(panel, LabelOf("Build your stronghold. Bring the whole expansion.", 13, Muted), 1, 91, 690, 29);
        Place(panel, LabelOf("INSTALLER " + ExpansionKit.Version + "   /   GAMEPLAY " + ExpansionKit.ContentVersion, 9, Orange, true), 2, 122, 690, 22);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Gatekeeper.Installer.portrait") ?? throw new InvalidDataException("Installer artwork is missing.");
        using var image = Image.FromStream(stream);
        var portrait = new PictureBox { Image = new Bitmap(image), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        Place(panel, portrait, 737, 0, 130, 137); panel.Resize += (_, _) => portrait.Left = panel.ClientSize.Width - portrait.Width;
        FormClosed += (_, _) => portrait.Image.Dispose();
        return panel;
    }

    private TableLayoutPanel BuildBody()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var setup = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, AutoScroll = true, Margin = new Padding(0, 0, 22, 0) };
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 24, 48, 43, 44, 40, 82, 27, 78 }) setup.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var label = LabelOf("GATEKEEPER LOCATION", 9, Muted, true); label.Dock = DockStyle.Fill;
        setup.Controls.Add(label, 0, 0);
        location.Multiline = true; location.Dock = DockStyle.Fill; location.Margin = new Padding(0, 0, 0, 8); setup.Controls.Add(location, 0, 1);
        choose.Dock = DockStyle.Fill; choose.Margin = new Padding(0, 0, 0, 8); setup.Controls.Add(choose, 0, 2);
        folderHint.Dock = DockStyle.Fill; setup.Controls.Add(folderHint, 0, 3);
        heading.Dock = DockStyle.Fill; heading.Font = new Font("Segoe UI", 14, FontStyle.Bold); setup.Controls.Add(heading, 0, 4);
        detail.Dock = DockStyle.Fill; setup.Controls.Add(detail, 0, 5);
        var included = LabelOf("ONE COMPLETE INSTALLATION", 9, Accent, true); included.Dock = DockStyle.Fill; setup.Controls.Add(included, 0, 6);
        var summary = LabelOf("Forge · Cannon Wheel\nItem Exchange · Prism Jackpot\n3 Aspects · Ascendant · 7 songs", 10, Muted); summary.Dock = DockStyle.Fill;
        setup.Controls.Add(summary, 0, 7);
        body.Controls.Add(setup, 0, 0); body.Controls.Add(new NewsPanel { Margin = Padding.Empty }, 1, 0);
        return body;
    }

    private Panel BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Size = new Size(885, 146) };
        Place(panel, progress, 0, 0, 885, 7); Place(panel, activity, 0, 15, 885, 40);
        progress.Anchor = activity.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        Place(panel, primary, 0, 62, 254, 48); Place(panel, support, 270, 62, 120, 48);
        Place(panel, close, 765, 62, 120, 48); close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Place(panel, LabelOf("Unofficial expansion · Gatekeeper required · Saves backed up automatically", 9, Muted), 0, 120, 885, 24);
        return panel;
    }

    private void BindGame(GameService selected)
    {
        if (game?.Root.Equals(selected.Root, StringComparison.OrdinalIgnoreCase) == true) return;
        var gate = new Mutex(true, "Local\\GatekeeperModManager-" + PortablePackage.Key(selected.Root), out var acquired);
        if (!acquired) { gate.Dispose(); throw new InvalidOperationException("The installer or an older mod manager is already open for this game. Close it and try again."); }
        gameMutex?.ReleaseMutex(); gameMutex?.Dispose(); gameMutex = gate;
        game = selected; info = null; status = null; location.Text = selected.Root;
    }

    private async Task ChooseFolder()
    {
        using var picker = new FolderBrowserDialog { Description = "Select the Gatekeeper folder containing Gatekeeper.exe.", UseDescriptionForTitle = true,
            ShowNewFolderButton = false, InitialDirectory = game?.Root ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (!SteamDiscovery.IsGame(picker.SelectedPath)) { MessageBox.Show(this, "Choose the game installation folder containing Gatekeeper.exe.", "Gatekeeper not found"); return; }
        try { BindGame(selectGame(picker.SelectedPath)); activity.Text = ""; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, ExpansionKit.Name); return; }
        await CheckInstallation();
    }

    private async Task InstallOrPlay()
    {
        if (game == null) { await ChooseFolder(); return; }
        if (status?.Stage == ExpansionStage.Ready)
            await Operation("Starting Gatekeeper…", () => { game.LaunchExpansion(); return "Gatekeeper is starting. You can also launch it from Steam."; });
        else
        {
            var reports = new Progress<InstallProgress>(p => { if (!IsDisposed) { progress.Value = p.Percent; activity.Text = p.Message; } });
            await Operation("Preparing the complete expansion…", () => { game.InstallExpansion(reports); return "Installation complete. Your saves are backed up. Ready to play."; });
        }
    }

    private async Task CheckInstallation()
    {
        if (game == null) { RefreshActions(); return; }
        busy = true; RefreshActions();
        try { var selected = game; (info, status) = await Task.Run(() => (selected.Inspect(), selected.ExpansionState())); }
        catch (Exception ex) { info = null; status = null; activity.Text = ex.Message; }
        finally { busy = false; RefreshActions(); }
    }

    private async Task Operation(string message, Func<string> work)
    {
        busy = true; progress.Value = 0; progress.Visible = true; activity.Text = message; RefreshActions();
        try { activity.Text = await Task.Run(work); }
        catch (Exception ex)
        {
            activity.Text = ex is UnauthorizedAccessException ? "Windows denied access. Close this kit, right-click its EXE and choose Run as administrator." : ex.Message;
            if (preview == null) MessageBox.Show(this, activity.Text, ExpansionKit.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { progress.Visible = false; busy = false; await CheckInstallation(); }
    }

    private void RefreshActions()
    {
        var running = game?.Running == true;
        choose.Enabled = !busy && !running; support.Enabled = !busy && game != null; close.Enabled = !busy;
        if (game == null)
        {
            location.Text = "Gatekeeper was not found automatically"; folderHint.Text = "Install Gatekeeper through Steam, or select its game folder.";
            heading.Text = "Find your game"; detail.Text = "Choose the folder containing Gatekeeper.exe to continue.";
            primary.Text = "Find Gatekeeper"; primary.Enabled = !busy; choose.Text = "Choose folder"; return;
        }
        choose.Text = "Change folder";
        folderHint.Text = info?.Supported == true ? "Compatible Steam installation detected." : info == null ? "Checking the game files…" : "This game version is not supported by this kit.";
        primary.Enabled = !busy && !running && info?.Supported == true;
        primary.Text = status?.Stage switch { ExpansionStage.Ready => "Play Gatekeeper", ExpansionStage.UpdateRequired => "Update expansion", ExpansionStage.RepairRequired => "Repair installation", _ => "Install expansion" };
        heading.Text = busy ? "Please wait…" : running ? "Gatekeeper is running" : info == null ? "Unable to check this installation" : !info.Supported ? "A compatible game version is needed" : status?.Stage switch
        { ExpansionStage.Ready => "Ready to play", ExpansionStage.UpdateRequired => "Expansion update ready", ExpansionStage.RepairRequired => "Finish your setup", _ => "Ready to install" };
        detail.Text = busy ? "Preparing your installation. This window will stay open until the operation finishes." : running ? "Close the game before installing or updating the expansion." : info?.Supported == false ? "This kit checks the game version before making changes. Ask for an updated kit if Steam has updated the game." : status?.Detail ?? "Check the folder above and try selecting it again.";
    }

    internal static Label LabelOf(string text, float size, Color color, bool bold = false) => new()
    { Text = text, ForeColor = color, BackColor = Color.Transparent, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), AutoSize = false, UseMnemonic = false };
    internal static Button ButtonOf(string text, bool primary = false)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Card, ForeColor = primary ? Background : Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, UseVisualStyleBackColor = false, UseMnemonic = false };
        button.FlatAppearance.BorderColor = Line; button.FlatAppearance.BorderSize = primary ? 0 : 1; return button;
    }
    internal static void Place(Control parent, Control child, int x, int y, int width, int height) { child.SetBounds(x, y, width, height); parent.Controls.Add(child); }
}

internal sealed class SupportForm : Form
{
    private bool busy;
    public SupportForm(GameService game)
    {
        Text = "Expansion support"; ClientSize = new Size(610, 420); MinimumSize = Size; MaximumSize = Size;
        StartPosition = FormStartPosition.CenterParent; BackColor = MainForm.Background; ForeColor = Color.White; Font = new Font("Segoe UI", 10);
        MainForm.Place(this, MainForm.LabelOf("Backups & support", 22, Color.White, true), 26, 22, 550, 45);
        MainForm.Place(this, MainForm.LabelOf("Your saves and unlocks stay on this PC. Installation makes a save backup.\nFinish runs using Forge, Cannon Wheel or expansion Aspects before removal.", 10, MainForm.Muted), 26, 84, 550, 60);
        var buttons = new List<Button>();
        Button Add(string title, int x, int y, Action action)
        {
            var button = MainForm.ButtonOf(title); MainForm.Place(this, button, x, y, 269, 42);
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); } }; buttons.Add(button); return button;
        }
        void Open(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        Add("Open backup folder", 26, 160, () => Open(Path.Combine(game.ManagerRoot, "backups")));
        Add("Open save folder", 311, 160, () => Open(game.SaveRoot));
        var backup = Add("Back up saves now", 26, 218, () => { });
        Add("Open game log", 311, 218, () => { if (!File.Exists(game.LogPath)) throw new InvalidOperationException("The log is created on your first game launch after installation."); Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { game.LogPath }, UseShellExecute = false }); });
        var remove = Add("Remove expansion…", 26, 276, () => { });
        Add("Licenses & instructions", 311, 276, () => Open(Path.Combine(game.PackageRoot, "notices")));
        Add("Back to installer", 311, 352, Close);
        var feedback = MainForm.LabelOf("", 9, MainForm.Accent); MainForm.Place(this, feedback, 26, 350, 269, 55);
        async Task Work(Func<string> action)
        {
            busy = true; buttons.ForEach(b => b.Enabled = false); feedback.Text = "Please wait…";
            try { feedback.Text = await Task.Run(action); }
            catch (Exception ex) { feedback.Text = ""; MessageBox.Show(this, ex.Message, Text); }
            finally { busy = false; buttons.ForEach(b => b.Enabled = true); }
        }
        backup.Click += async (_, _) => await Work(() => game.BackupSaves() == null ? "No saves found yet." : "Save backup created.");
        remove.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Remove the expansion from this game installation? Your saves, unlocks and backups will be kept.", "Remove expansion", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await Work(() => { var kept = game.Uninstall(); return kept.Count == 0 ? "Expansion removed." : "Removed. Changed files were kept."; });
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
    }
}
