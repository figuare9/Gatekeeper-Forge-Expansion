using Gatekeeper.ModManager;
using Gatekeeper.Mods;

internal static class ExpansionKitChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string> fails)
    {
        static void Content(Fixture f, string value = "complete-content") => File.WriteAllText(Path.Combine(f.Manager, "packages/mods/Gatekeeper.CustomMods.dll"), value);
        static string Config(Fixture f, string id) => Path.Combine(f.Game, "BepInEx/config/" + id + ".cfg");
        using (var f = new Fixture())
        {
            Content(f);
            check(f.Service.ExpansionState().Stage == ExpansionStage.NotInstalled, "A clean game was not ready for a first expansion install.");
            f.Service.SaveProfile(new BalanceProfile { Enabled = false, Forge = false, CannonWheel = false, Metalheads25 = false, QuietBayShop = false, PrismJackpot = false, Movement = true, Damage = true, Cooldown = true, DamagePercent = 20 });
            f.Service.InstallExpansion();
            check(f.Service.Installed && f.Service.LoaderEnabled && f.Service.ExpansionState().Stage == ExpansionStage.Ready, "The complete expansion did not finish enabled for normal Steam launches.");
            var profile = f.Service.Profile();
            check(profile.Forge && profile.CannonWheel && !profile.Metalheads25 && profile.QuietBayShop && profile.PrismJackpot, "A legacy disabled switch excluded content from the fixed package.");
            check(!profile.Movement && !profile.Damage && !profile.Cooldown, "Legacy balance adjustments leaked into the fixed expansion profile.");
            check(File.ReadAllText(Path.Combine(f.Saves, "Saves/progress")) == "save-data" && Directory.GetFiles(Path.Combine(f.Manager, "backups"), "*.zip").Length == 1, "Expansion installation changed saved progress or missed its backup.");
            check(File.Exists(Config(f, GameService.CustomPluginId)) && File.Exists(Config(f, GameService.PluginId)), "The complete package did not write both plugin configurations.");
            // BepInEx rewrites comments/order on first launch. It must remain ready.
            var config = File.ReadAllText(Config(f, GameService.CustomPluginId));
            File.WriteAllText(Config(f, GameService.CustomPluginId), "# Plugin generated comments\n" + config.Replace("true", "True"));
            check(f.Service.ExpansionState().Stage == ExpansionStage.Ready, "Normal BepInEx configuration formatting caused a false repair prompt.");
            f.Service.SetLoader(false);
            check(f.Service.ExpansionState().Stage == ExpansionStage.RepairRequired, "A disabled expansion was reported ready.");
            f.Service.InstallExpansion();
            check(f.Service.LoaderEnabled && f.Service.ExpansionState().Stage == ExpansionStage.Ready, "The installer could not re-enable an existing complete installation.");
            File.Delete(Path.Combine(f.Game, GameService.CustomPlugin));
            check(f.Service.ExpansionState().Stage == ExpansionStage.RepairRequired, "A missing gameplay plugin was reported ready.");
            f.Service.InstallExpansion();
            check(f.Service.ExpansionState().Stage == ExpansionStage.Ready, "The complete installer did not repair a missing gameplay plugin.");
            f.Running = true;
            fails(() => f.Service.InstallExpansion(), "The expansion changed files while Gatekeeper was running.");
            f.Running = false;
            check(f.Service.Uninstall().Count == 0 && !f.Service.LoaderEnabled, "Expansion removal did not use the managed receipt.");
            check(File.ReadAllText(Path.Combine(f.Saves, "Saves/progress")) == "save-data", "Expansion removal changed native saved progress.");
        }
        using (var f = new Fixture())
        {
            Content(f, "content-v1"); f.Service.Install();
            f.Service.SaveProfile(new BalanceProfile { Forge = false, QuietBayShop = false, CannonWheel = false, Damage = true });
            Content(f, "content-v2"); File.WriteAllText(Path.Combine(f.Manager, "packages/mods/Gatekeeper.SubtleBalance.dll"), "balance-v2");
            check(f.Service.ExpansionState().Stage == ExpansionStage.UpdateRequired, "An older mod-manager install was not offered an expansion update.");
            f.Service.InstallExpansion();
            check(File.ReadAllText(Path.Combine(f.Game, GameService.CustomPlugin)) == "content-v2" && File.ReadAllText(Config(f, GameService.CustomPluginId)).Contains("[Forge]\nEnabled = true"), "Legacy installation migration did not update and enable Forge.");
            check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/plugins/Gatekeeper.SubtleBalance/Gatekeeper.SubtleBalance.dll")) == "balance-v2", "The installation kit did not update every bundled plugin.");
            check(Directory.GetFiles(Path.Combine(f.Manager, "state/plugin-backups"), "*.dll").Length == 2, "Migration did not preserve both previous plugin binaries.");
            Storage.Json(Path.Combine(f.Game, "BepInEx/config/" + GameService.CustomPluginId + ".forge-run.json"), new ForgeRunMarker(true, ForgeRules.CharacterId));
            f.Service.InstallExpansion();
            check(f.Service.LoaderEnabled && f.Service.ExpansionState().Stage == ExpansionStage.Ready, "Updating the expansion tried to disable an active saved Forge run.");
            fails(() => f.Service.Uninstall(), "The installer removed a package required by an unfinished Forge run.");
            File.WriteAllText(Path.Combine(f.Game, GameService.CustomPlugin), "external-edit");
            fails(() => f.Service.InstallExpansion(), "The expansion overwrote an externally changed plugin.");
            check(File.ReadAllText(Path.Combine(f.Game, GameService.CustomPlugin)) == "external-edit", "The changed plugin was not preserved.");
        }
        foreach (var legacy in new[] { false, true })
        {
            using var f = new Fixture(); Content(f, "original");
            if (legacy) { f.Service.Install(); Content(f, "updated"); }
            fails(() => f.Service.InstallExpansion(new Interrupted()), "The interrupted-install fixture did not stop installation.");
            check(f.Service.Receipt() is { Complete: false }, "Interrupted setup was reported complete.");
            f.Service.InstallExpansion();
            check(f.Service.ExpansionState().Stage == ExpansionStage.Ready && f.Service.LoaderEnabled, "An interrupted fresh install or legacy update could not resume.");
        }
        using (var f = new Fixture())
        {
            Content(f); f.Service.Install();
            var relative = "BepInEx/unity-libs/2022.3.62.zip";
            var existing = Path.Combine(f.Game, relative); var bundled = Path.Combine(f.Manager, "packages/loader/" + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!); Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
            File.WriteAllText(existing, "pinned-unity-support"); File.WriteAllText(bundled, "pinned-unity-support");
            f.Service.InstallExpansion();
            check(f.Service.ExpansionState().Stage == ExpansionStage.Ready, "An older loader's identical Unity support ZIP prevented migration.");
            check(!f.Service.Receipt()!.Files.Any(p => p.RelativePath.Replace('\\', '/') == relative), "The kit claimed ownership of pre-existing support files.");
            f.Service.Uninstall();
            check(File.ReadAllText(existing) == "pinned-unity-support", "Removal deleted the older loader's pre-existing support ZIP.");
        }
        using (var f = new Fixture())
        {
            fails(() => f.Service.InstallExpansion(), "The kit silently omitted a missing content plugin.");
            check(!File.Exists(f.Service.ReceiptPath) && !File.Exists(Path.Combine(f.Game, "winhttp.dll")), "An incomplete package mutated the game before failing.");
            Content(f); f.Service.ExpectedAssembly = "unsupported";
            fails(() => f.Service.InstallExpansion(), "The expansion installed into an unsupported game.");
            check(!File.Exists(f.Service.ReceiptPath), "Unsupported expansion setup wrote a receipt.");
        }
        using (var f = new Fixture())
        {
            Content(f); File.WriteAllText(Path.Combine(f.Game, "winhttp.dll"), "other-loader");
            fails(() => f.Service.InstallExpansion(), "The expansion overwrote another loader.");
            check(File.ReadAllText(Path.Combine(f.Game, "winhttp.dll")) == "other-loader" && !File.Exists(f.Service.ReceiptPath), "A conflicting loader was not preserved.");
        }
    }
    private sealed class Interrupted : IProgress<InstallProgress>
    {
        public void Report(InstallProgress progress) { if (progress.Percent >= 35) throw new IOException("Simulated power interruption"); }
    }
}
