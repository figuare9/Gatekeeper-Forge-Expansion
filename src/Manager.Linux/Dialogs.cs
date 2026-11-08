using System.Diagnostics;

namespace Gatekeeper.ModManager.Linux;

internal sealed class Dialogs
{
    private readonly string? tool;
    public Dialogs(bool terminal)
    {
        if (terminal || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return;
        foreach (var name in new[] { "zenity", "kdialog" })
            if ((Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(p => File.Exists(Path.Combine(p, name)))) { tool = name; break; }
    }
    private (int Code, string Text) Run(params string[] args)
    {
        var start = new ProcessStartInfo(tool!) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not open the installer dialog.");
        var text = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, text.TrimEnd('\r', '\n'));
    }
    public bool Confirm(string text, string action)
    {
        if (tool == "zenity") return Run("--question", "--title=" + ExpansionKit.Name, "--no-markup", "--width=620", "--text=" + text, "--ok-label=" + action, "--cancel-label=Cancel").Code == 0;
        if (tool == "kdialog") return Run("--title", ExpansionKit.Name, "--yes-label", action, "--no-label", "Cancel", "--yesno", text).Code == 0;
        Console.WriteLine(text); Console.Write(action + "? [y/N] ");
        return Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
    }
    public string? PickFolder()
    {
        if (tool == "zenity") { var result = Run("--file-selection", "--directory", "--title=Select the Gatekeeper game folder"); return result.Code == 0 ? result.Text : null; }
        if (tool == "kdialog") { var result = Run("--title", "Select the Gatekeeper game folder", "--getexistingdirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); return result.Code == 0 ? result.Text : null; }
        Console.Write("Gatekeeper game folder (blank to cancel): "); return Console.ReadLine()?.Trim().Trim('"');
    }
    public void Message(string text, bool error = false)
    {
        (error ? Console.Error : Console.Out).WriteLine(text);
        if (tool == "zenity") Run(error ? "--error" : "--info", "--title=" + ExpansionKit.Name, "--no-markup", "--width=650", "--text=" + text);
        else if (tool == "kdialog") Run("--title", ExpansionKit.Name, error ? "--error" : "--msgbox", text);
    }
    public void Instructions(string text)
    {
        Console.WriteLine(text);
        if (tool == "zenity")
        {
            var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardInput = true };
            foreach (var arg in new[] { "--text-info", "--title=Forge installed — Steam setup", "--width=760", "--height=520", "--ok-label=Done" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            try { process.StandardInput.Write(text); process.StandardInput.Close(); } catch (IOException) { }
            process.WaitForExit();
        }
        else if (tool == "kdialog") Message(text);
    }
    public IDisposable Progress(out IProgress<InstallProgress> reports)
    {
        Process? process = null;
        if (tool == "zenity")
        {
            var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardInput = true };
            foreach (var arg in new[] { "--progress", "--title=Installing Forge Expansion", "--text=Checking the included files…", "--width=620", "--percentage=0", "--no-cancel", "--auto-close" }) start.ArgumentList.Add(arg);
            process = Process.Start(start);
        }
        var progress = new ProgressDialog(process); reports = progress; return progress;
    }
    private sealed class ProgressDialog(Process? process) : IDisposable, IProgress<InstallProgress>
    {
        private string last = "";
        public void Report(InstallProgress value)
        {
            if (last != value.Message) { Console.WriteLine(value.Message); last = value.Message; }
            if (process == null) return;
            try { process.StandardInput.WriteLine(value.Percent); process.StandardInput.WriteLine("# " + value.Message); process.StandardInput.Flush(); }
            catch (IOException) { } catch (InvalidOperationException) { }
        }
        public void Dispose()
        {
            if (process == null) return;
            try { process.StandardInput.Close(); if (!process.WaitForExit(2000)) process.Kill(); }
            catch (InvalidOperationException) { } catch (IOException) { }
            finally { process.Dispose(); }
        }
    }
}
