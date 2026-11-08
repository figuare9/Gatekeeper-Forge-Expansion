using System.IO.Compression;
using Gatekeeper.ModManager;
using Gatekeeper.Mods;

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
void Fails(Action action, string message)
{ try { action(); } catch (Exception) { passed++; return; } throw new Exception(message); }

Check(new[]{AspectRules.Chaos,AspectRules.Scaling,AspectRules.Elite}.Distinct().Count()==3 &&
    new[]{AspectRules.Chaos,AspectRules.Scaling,AspectRules.Elite}.All(i=>i>20 && AspectRules.IsCustom(i)),"Expansion Aspect IDs collide with native types or cannot fit the native byte wire format.");
Check(!AspectRules.IsCustom(0) && !AspectRules.IsCustom(1) && !AspectRules.IsCustom(20) && !AspectRules.IsCustom(255),"An expansion Aspect captured a native/unknown ID.");
Check(AspectRules.SpawnMultiplier(1,false)==1 && AspectRules.SpawnMultiplier(1.5f,false)==1.5f,"Disabled Chaos changed native Swarm.");
Check(AspectRules.SpawnMultiplier(1,true)==1.75f && AspectRules.SpawnMultiplier(1.5f,true)==2.25f,"Chaos and Swarm must add 75% + 50%, rather than multiply.");
Check(AspectRules.TeamFactor(Array.Empty<int>())==1 && AspectRules.TeamFactor(new[]{0,-1,1})==1,"Missing/uninitialised players changed level-one scaling.");
Check(Math.Abs(AspectRules.TeamFactor(new[]{11,31})-2.4f)<.0001f,"Scaling must use average team level, not highest level or summed levels.");
Check(Math.Abs(AspectRules.TeamFactor(new[]{0,20})-1.63f)<.0001f,"A valid native level-zero teammate was omitted from the average.");
Check(Math.Abs(AspectRules.TeamFactor(new[]{19})-2.26f)<.0001f && Math.Abs(AspectRules.Health(250,2.26f)-565)<.001f,"The agreed 7% per-level health bonus was changed.");
var level21Scaling=AspectRules.TeamScaling(new[]{11,31});
Check(Math.Abs(level21Scaling.Health-2.4f)<.0001f && level21Scaling.Prisms==2,"Increasing enemy health also increased the agreed 5% Prism rewards.");
var level2Scaling=AspectRules.TeamScaling(new[]{2});
Check(Math.Abs(level2Scaling.Health-1.07f)<.0001f && Math.Abs(level2Scaling.Prisms-1.05f)<.0001f,"Health and Prism bonuses did not start independently after level one.");
Check(AspectRules.TeamScaling(Array.Empty<int>())==AspectScaling.Unscaled && AspectRules.TeamScaling(new[]{0,1}).Prisms==1,"Missing or starting-level teammates created bonus Prism rewards.");
Check(AspectRules.Health(250,1)==250 && AspectRules.Health(250,float.NaN)==250 && float.IsFinite(AspectRules.Health(float.MaxValue,100)),"Scaling changed baseline health or overflowed native health.");
Check(AscendantRules.Health(1)==1 && AscendantRules.EliteChance(1)==0,"Ascendant penalized a starting build.");
Check(AscendantRules.ExpectedCrit(0,5)==1 && AscendantRules.ExpectedCrit(1,5)==5 && AscendantRules.ExpectedCrit(2,5)==5,"Expected crit power does not match native probability limits.");
Check(AscendantRules.TeamPower(new[]{1f,9f})==5 && AscendantRules.TeamPower(Array.Empty<float>())==1,"Ascendant used the strongest co-op player instead of the average.");
Check(AscendantRules.Power(float.NaN,1,1)==1 && AscendantRules.Health(float.PositiveInfinity)==1,"Invalid native stats escaped difficulty safeguards.");
foreach(var power in new[]{2f,4f,10f,100f,10000f})
{
    Check(AscendantRules.Health(power)>1 && AscendantRules.Health(power)<power,"Adaptive health canceled item power gains.");
    Check(AscendantRules.Health(power)<=25 && AscendantRules.EliteChance(power)<=.15f,"Ascendant exceeded its defense/elite limits.");
}
Check(ForgeTooltips.Descriptions.Skip(1).All(t=>t.Contains("<color=#") && t.Contains("\n\n")),"Forge tooltips lost native stat layout/colors.");
Check(!ForgeTooltips.Descriptions.Any(t=>t.Contains("Hold Space") || t.Contains("Hold F")),"Tooltips assume keyboard bindings for held abilities.");
var nativeBuildPower=AscendantRules.Power(87.99999f/40,2.5f,AscendantRules.ExpectedCrit(.67151403f,9.5f)/AscendantRules.ExpectedCrit(.03f,2));
Check(Math.Abs(nativeBuildPower-35.818718f)<.001f,"Native stacked-build crit fractions or Opal Eye damage were counted incorrectly.");
Check(Math.Abs(160*AscendantRules.Health(nativeBuildPower)-704.309f)<.1f,"Adaptive health no longer matches the gentler native stacked-build fixture.");
AscendantRetuneChecks.Run(Check);
ExpansionNewsChecks.Run(Check, Fails);
var prismCarry=new AspectPrismCarry();
Check(Enumerable.Range(0,20).Sum(_=>prismCarry.Apply(1,1.05f))==21,"Small enemy rewards rounded a 5% bonus away or inflated it into 100%.");
prismCarry.Clear();
Check(Enumerable.Range(0,20).Sum(_=>prismCarry.Apply(1,level21Scaling.Prisms))==40,"Level-21 Prism drops used the new health factor instead of the retained reward factor.");
prismCarry.Clear();
Check(Enumerable.Range(0,100).Sum(_=>prismCarry.Apply(3,1.5f))==450,"Fractional scaling rewards do not preserve their total.");
Check(prismCarry.Apply(0,2)==0 && prismCarry.Apply(5,1)==5 && prismCarry.Apply(5,float.NaN)==5,"Scaling manufactured a reward or changed an unscaled reward.");
prismCarry.Clear();Check(prismCarry.Remainder==0,"Reward fractions leaked into a new run.");
Check(AspectRules.ChooseElite(8,127,10)==8,"Elite replaced an enemy's existing native elite type.");
Check(Enumerable.Range(0,100).All(i=>new[]{2,32}.Contains(AspectRules.ChooseElite(0,2|32,i))),"Elite selected an unsupported enemy affix.");
Check(AspectRules.ChooseElite(0,0,0)==4 && Enumerable.Range(0,20).All(i=>new[]{1,2,4,8,16,32,64}.Contains(AspectRules.ChooseElite(0,-1,i))),"Boss fallback or All-mask selection produced a non-elite/combined affix.");
using(var f=new Fixture())
{
    f.Service.Install();var marker=Path.Combine(f.Game,"BepInEx/config/"+GameService.CustomPluginId+".aspects-run.json");
    Storage.Json(marker,new AspectRunMarker{RequiresMod=true,Aspects=new[]{(int)AspectRules.Scaling}});
    Fails(()=>f.Service.SetLoader(false),"Disabled the loader during an expansion-Aspect run.");
    Fails(()=>f.Service.Uninstall(),"Removed custom Aspect metadata needed by a saved run.");
    Storage.Json(marker,new AspectRunMarker{RequiresMod=false});f.Service.SetLoader(false);
    Check(!f.Service.LoaderEnabled,"Ended expansion-Aspect run still blocked vanilla compatibility.");
}

using (var f = new Fixture())
{
    var assembly = GameService.Hash(Path.Combine(f.Game, "GameAssembly.dll"));
    f.Service.Install();
    Check(f.Service.Installed && !f.Service.LoaderEnabled, "Install must finish with the loader disabled.");
    Check(GameService.Hash(Path.Combine(f.Game, "GameAssembly.dll")) == assembly, "Original game code changed.");
    var zipPath = Directory.GetFiles(Path.Combine(f.Manager, "backups"), "*.zip").Single();
    using (var zip = ZipFile.OpenRead(zipPath)) Check(zip.GetEntry("Saves/progress") != null, "Save backup missing.");
    f.Service.SetLoader(true); Check(f.Service.LoaderEnabled, "Enable failed.");
    f.Service.SetLoader(false); Check(!f.Service.LoaderEnabled, "Disable failed.");
    f.Service.SaveProfile(new BalanceProfile { Movement = true, MovementPercent = 7 });
    Check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/config/" + GameService.PluginId + ".cfg")).Contains("Percent = 7"), "Configuration not applied.");
    f.Running = true;
    Fails(() => f.Service.SetLoader(true), "Changed loader while game running.");
    Fails(() => f.Service.BackupSaves(), "Backed up active saves.");
    Fails(() => f.Service.Uninstall(), "Removed active loader.");
    Fails(() => f.Service.UpdateCustomMods(), "Updated custom mods while the game was running.");
    f.Running = false;
    var customSource = Path.Combine(f.Manager, "packages/mods/Gatekeeper.CustomMods.dll");
    File.WriteAllText(customSource, "custom-v1"); f.Service.UpdateCustomMods();
    var customTarget = Path.Combine(f.Game, GameService.CustomPlugin);
    Check(File.ReadAllText(customTarget) == "custom-v1", "Custom plugin upgrade from v0.1 failed.");
    Check(f.Service.Mods().Count == 0, "Included custom plugin appears as an unmanaged plugin.");
    File.WriteAllText(customSource, "custom-v2"); f.Service.UpdateCustomMods();
    Check(File.ReadAllText(customTarget) == "custom-v2", "Custom plugin update failed.");
    Check(Directory.GetFiles(Path.Combine(f.Manager, "state/plugin-backups"), "*.dll").Length == 1, "Plugin update did not preserve the previous version.");
    File.Copy(f.Service.ReceiptPath, f.Service.ReceiptPath + ".previous", true);
    File.WriteAllText(customSource, "custom-v3"); File.WriteAllText(customTarget + ".new", "custom-v3");
    var updateReceipt = f.Service.Receipt()!;
    updateReceipt.Files.RemoveAll(x => x.RelativePath.Replace('\\', '/') == GameService.CustomPlugin);
    updateReceipt.Files.Add(new(GameService.CustomPlugin, GameService.Hash(customTarget + ".new"))); Storage.Json(f.Service.ReceiptPath, updateReceipt);
    f.Service.UpdateCustomMods();
    Check(File.ReadAllText(customTarget) == "custom-v3" && !File.Exists(customTarget + ".new"), "An interrupted custom-mod update was not recovered.");
    File.WriteAllText(customTarget, "external-change");
    Fails(() => f.Service.UpdateCustomMods(), "Overwrote an externally changed custom plugin.");
    File.WriteAllText(customTarget, "custom-v3");
    f.Service.SaveProfile(new BalanceProfile { Enabled = false, Metalheads25 = true, QuietBayShop = false });
    var customConfig = File.ReadAllText(Path.Combine(f.Game, "BepInEx/config/" + GameService.CustomPluginId + ".cfg"));
    Check(customConfig.Contains("[Metalheads]\nEnabled = false") && customConfig.Contains("[QuietBayShop]\nEnabled = false"), "Removed Metalhead override was re-enabled by a legacy profile.");
    var import = Path.Combine(f.Manager, "sample.dll"); File.WriteAllText(import, "test plugin");
    f.Service.ImportMod(import); var mod = f.Service.Mods().Single();
    f.Service.ToggleMod(mod); Check(!f.Service.Mods().Single().Enabled, "Plugin disable failed.");
    f.Service.ToggleMod(f.Service.Mods().Single()); Check(f.Service.Mods().Single().Enabled, "Plugin enable failed.");
    Fails(() => f.Service.ImportMod(import), "Overwrote imported plugin.");
    var changed = Path.Combine(f.Game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"); File.AppendAllText(changed, "user change");
    var kept = f.Service.Uninstall();
    Check(kept.Count == 1 && File.Exists(changed), "Uninstall deleted a modified file.");
    Check(File.Exists(Path.Combine(f.Game, "BepInEx/plugins/Imported/sample.dll")), "Uninstall deleted another mod.");
    Check(!File.Exists(Path.Combine(f.Game, "winhttp.dll")) && !File.Exists(Path.Combine(f.Game, "winhttp.dll.gkmm-disabled")), "Uninstall left loader active.");
    Check(File.ReadAllText(Path.Combine(f.Saves, "Saves/progress")) == "save-data", "Save changed.");
}
using (var f = new Fixture())
{
    File.WriteAllText(Path.Combine(f.Game, "winhttp.dll"), "existing loader");
    Fails(() => f.Service.Install(), "Overwrote another loader.");
    Check(File.ReadAllText(Path.Combine(f.Game, "winhttp.dll")) == "existing loader" && !File.Exists(f.Service.ReceiptPath), "Conflict preflight changed files.");
}
using (var f = new Fixture())
{
    f.Service.ExpectedAssembly = "different";
    Fails(() => f.Service.Install(), "Accepted unsupported build.");
    Check(!File.Exists(Path.Combine(f.Game, "winhttp.dll")), "Unsupported install wrote files.");
}
using (var f = new Fixture())
{
    f.Service.Install();
    var receipt = f.Service.Receipt()!;
    receipt.Files.Add(new("../outside.txt", "whatever")); Storage.Json(f.Service.ReceiptPath, receipt);
    Fails(() => f.Service.Uninstall(), "Trusted a traversal path in the receipt.");
    Check(File.Exists(Path.Combine(f.Game, "winhttp.dll.gkmm-disabled")), "Uninstall mutated before validating all paths.");
}
using (var f = new Fixture())
{
    var target = Path.Combine(f.Game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "partial");
    var receipt = new InstallReceipt { GameRoot = f.Game, AssemblyHash = "a", MetadataHash = "b" };
    receipt.Files.Add(new("BepInEx/core/BepInEx.Unity.IL2CPP.dll", GameService.Hash(target))); Storage.Json(f.Service.ReceiptPath, receipt);
    f.Service.Uninstall(); Check(!File.Exists(target), "Interrupted install could not be recovered without a proxy.");
}
Fails(() => GameService.Inside(Path.GetTempPath(), "../escape"), "Path traversal allowed.");
Fails(() => GameService.Inside(Path.GetTempPath(), "C:\\absolute"), "Rooted path allowed.");
Fails(() => GameService.Inside(Path.GetTempPath(), "file:stream"), "Alternate data stream allowed.");
Fails(() => new BalanceProfile { DamagePercent = 21 }.Validate(), "Out-of-range setting accepted.");
Check(Math.Abs(BalanceMath.Increase(10, 5) - 10.5f) < .0001f, "Movement/damage scaling incorrect.");
Check(Math.Abs(BalanceMath.Shorten(10, 5) - 9.5f) < .0001f, "Cooldown scaling incorrect.");
Check(BalanceMath.Increase(0, 20) == 0 && BalanceMath.Shorten(-1, 20) == -1, "Invalid values changed.");
Check(float.IsNaN(BalanceMath.Increase(float.NaN, 5)), "NaN handling changed.");
Check(BalanceMath.Increase(10, 500) == 12 && BalanceMath.Shorten(10, 500) == 8, "Adjustment cap failed.");
Check(CustomModRules.CanBuy(true, true, true, true, true, false, 70, 70, false), "Rejected an affordable unlocked shop purchase.");
Check(!CustomModRules.CanBuy(false, true, true, true, true, false, 70, 100, false), "Allowed a purchase outside Quiet Bay.");
Check(!CustomModRules.CanBuy(true, false, true, true, true, false, 70, 100, false), "Allowed a purchase before the host session was ready.");
Check(!CustomModRules.CanBuy(true, true, false, true, true, false, 70, 100, false), "Allowed a locked item purchase.");
Check(!CustomModRules.CanBuy(true, true, true, false, true, false, 70, 100, false), "Allowed an unavailable DLC item purchase.");
Check(!CustomModRules.CanBuy(true, true, true, true, true, true, 70, 100, false), "Allowed purchase beyond the stack cap.");
Check(!CustomModRules.CanBuy(true, true, true, true, true, false, 70, 69, false), "Allowed an unaffordable purchase.");
Check(!CustomModRules.CanBuy(true, true, true, true, true, false, -1, 100, false), "Accepted an unavailable native price.");
Check(!CustomModRules.CanBuy(true, true, true, true, true, false, 70, 100, true), "Allowed duplicate pending purchases.");
var before = new ShopFunds(100, 100, 15, 15);
var payment = new ShopPayment(before, 70);
Check(payment.CanStart, "Rejected an affordable, synchronized wallet.");
Check(!payment.Charged(before), "An unchanged wallet authorized delivery.");
Check(!payment.Charged(new(100, 100, 15, 85)), "Regression: a server-only ForceChange authorized an item while normal shops still had all funds.");
Check(!payment.Charged(new(30, 30, 85, 15)), "Delivered an item before the native server acknowledged the charge.");
Check(!payment.Charged(new(100, 30, 85, 85)), "Ignored a stale normal-shop currency balance.");
Check(payment.Charged(new(30, 30, 85, 85)), "Rejected a charge confirmed by both normal-shop and server state.");
Check(payment.Charged(new(35, 35, 85, 85)), "Concurrent earned currency invalidated an otherwise exact spent-counter charge.");
Check(!payment.Charged(new(65, 65, 50, 50)), "A partial charge authorized delivery.");
Check(!payment.Charged(new(29, 29, 86, 86)), "An incorrect charge authorized delivery.");
Check(!new ShopPayment(new(69, 69, 15, 15), 70).CanStart, "Accepted insufficient funds.");
Check(!new ShopPayment(new(100, 100, 15, 20), 70).CanStart, "Started a purchase with unsynchronized spent counters.");
Check(!new ShopPayment(new(100, 99, 15, 15), 70).CanStart, "Started with disagreement between displayed and held funds.");
Check(!new ShopPayment(before, -1).CanStart, "Started with an unavailable price.");
Check(!new ShopPayment(new(int.MaxValue, int.MaxValue, int.MaxValue - 1, int.MaxValue - 1), 70).CanStart, "Allowed the native spent counter to overflow.");
Check(new ShopPayment(before, 0).Charged(before), "A zero-price item required a currency mutation.");
Check(payment.RefundAmount(new(30, 30, 85, 15)) == 70, "A delayed server charge prevented precise client rollback.");
Check(payment.RefundAmount(new(65, 65, 50, 50)) == 35, "Refund did not match the observed partial debit.");
Check(payment.RefundAmount(before) == 0, "A no-op charge produced a refund credit.");
Check(payment.RefundAmount(new(20, 20, 95, 95)) == -1, "Unexpected external spending generated a speculative refund.");
Check(payment.Refunded(before), "Did not confirm a restored native wallet.");
Check(!payment.Refunded(new(100, 100, 15, 85)), "Confirmed refund before server acknowledgment.");
Check(!payment.Refunded(new(100, 99, 15, 15)), "Confirmed refund with stale displayed currency.");
Check(SlotRules.Stake(1000) == 200 && SlotRules.Payout(200) == 1000, "Slot bet is not 20% with a 5x TOTAL payout.");
Check(1000 - SlotRules.Stake(1000) + SlotRules.Payout(SlotRules.Stake(1000)) == 1800, "Slot win incorrectly adds the stake to the advertised 5x return.");
Check(SlotRules.Stake(999) == 199 && SlotRules.Stake(5) == 1, "Slot fractional bets are not rounded down.");
Check(SlotRules.Stake(4) == 0 && SlotRules.Stake(0) == 0 && SlotRules.Stake(-1) == 0, "Slot machine can bet zero or overcharge tiny balances.");
Check(Enumerable.Range(0,5).Count(SlotRules.Wins) == 1, "Slot odds do not give exactly one win in five outcomes.");
Check(SlotRules.CanBet(1000,1000,0,200), "Rejected a normal fixed slot bet.");
Check(!SlotRules.CanBet(4,4,0,0), "Allowed a bet before a valid initial stake was set.");
Check(!SlotRules.CanBet(int.MaxValue,int.MaxValue,0,SlotRules.Stake(int.MaxValue)), "A winning slot bet can overflow the native wallet.");
Check(!SlotRules.CanBet(100,100,int.MaxValue,20), "A slot debit can overflow the native spent counter.");
Check(!SlotRules.CanBet(100,int.MaxValue,0,20), "A slot prize can overflow accumulated currency.");
var visitBets = new SlotVisitStakes();
var losingBalance = 1000; var visitSpent = 0;
for (var spin = 0; spin < 5; spin++)
{
    var stake = visitBets.Capture(17,losingBalance);
    Check(stake == 200 && SlotRules.CanBet(losingBalance,1000,visitSpent,stake), "The price shrank after a losing spin or a repeat approach.");
    losingBalance -= stake; visitSpent += stake;
}
Check(losingBalance == 0 && !SlotRules.CanBet(losingBalance,1000,visitSpent,visitBets.ForPlayer(17)), "1,000 Prisms funded more than five losing 200-Prism spins.");
Check(visitBets.Capture(17,1800) == 200 && SlotRules.Payout(visitBets.ForPlayer(17)) == 1000, "A win or new income changed the visit's price or prize.");
Check(visitBets.Capture(18,250) == 50 && visitBets.ForPlayer(17) == 200, "Co-op players shared a fixed bet instead of their own initial prices.");
Check(!SlotRules.CanBet(199,1000,801,200) && SlotRules.CanBet(200,1000,800,200), "An unaffordable fixed bet was shrunk, or an exact-balance bet was rejected.");
Check(visitBets.Capture(19,4) == 0 && visitBets.Capture(19,9) == 1 && visitBets.Capture(19,1000) == 1, "An unfunded first approach set a free bet or a later visit to the same machine reset its price.");
Check(SlotRules.CanBet(1,9,8,visitBets.ForPlayer(19)), "A small fixed stake was blocked by reapplying the initial five-Prism minimum.");
Check(!SlotRules.CanBet(1000,1000,0,500_000_000) && !SlotRules.CanBet(1000,1000,0,-1), "Invalid fixed stakes were accepted.");
visitBets.Clear();
Check(visitBets.ForPlayer(18) == 0 && visitBets.Capture(17,500) == 100, "A new Quiet Bay visit retained the previous visit's prices.");
var spinId = Guid.NewGuid().ToString("N");
var jackpot = new SlotRound(spinId, 17, 100, 0);
Check(!jackpot.ConfirmDebit(18,spinId) && !jackpot.ConfirmDebit(17,Guid.NewGuid().ToString("N")), "Another player or another request can authorize a bet.");
Check(!jackpot.ClaimPayout(), "A slot jackpot paid before debit acknowledgment.");
Check(jackpot.ConfirmDebit(17,spinId) && !jackpot.ConfirmDebit(17,spinId), "Duplicate bet acknowledgments can restart a spin.");
Check(jackpot.Payout == 500 && jackpot.Owner == 17, "The jackpot belongs to the wrong player or pays the wrong amount.");
Check(jackpot.ClaimPayout() && !jackpot.ClaimPayout(), "A repeated result can pay a jackpot twice.");
Check(!jackpot.ConfirmPayout(18,spinId), "A teammate can acknowledge another player's payout.");
Check(jackpot.ConfirmPayout(17,spinId) && !jackpot.ConfirmPayout(17,spinId), "Duplicate payout acknowledgments can settle twice.");
Check(!jackpot.RefundBeforeSpin(), "A completed jackpot can also refund its stake.");
var loss = new SlotRound(Guid.NewGuid().ToString("N"), 2, 100, 4);
loss.ConfirmDebit(2,loss.Id);
Check(!loss.ClaimPayout() && loss.Payout == 0 && loss.Stage == SlotStage.Finished, "A losing roll pays currency.");
var cancelled = new SlotRound(Guid.NewGuid().ToString("N"), 2, 100, 0);
Check(cancelled.RefundBeforeSpin() && !cancelled.RefundBeforeSpin() && !cancelled.ConfirmDebit(2,cancelled.Id) && !cancelled.ClaimPayout(), "A cancelled bet can refund twice or later pay a jackpot.");
Fails(() => new SlotRound("invalid",2,100,0), "Invalid slot request IDs were accepted.");
Fails(() => new SlotRound(spinId,2,100,5), "A roll outside the five-outcome range was accepted.");
Check(new BalanceProfile { Enabled=false, Metalheads25=false, QuietBayShop=false, PrismJackpot=true }.CustomConfig().Contains("[PrismJackpot]\nEnabled = true"), "Slot switch depends on the other mods.");
Check(System.Text.Json.JsonSerializer.Deserialize<BalanceProfile>("{\"QuietBayShop\":false}")!.PrismJackpot, "Old profiles do not enable the newly requested slot machine.");
var walletReceipts = new SlotWalletReceipts();
Check(!walletReceipts.Claim(spinId,"payout",17,18), "A wallet receipt can pay a different local player.");
Check(walletReceipts.Claim(spinId,"debit",17,17) && !walletReceipts.Claim(spinId,"debit",17,17), "A repeated wallet receipt can deduct the stake twice.");
Check(walletReceipts.Claim(spinId,"payout",17,17) && !walletReceipts.Claim(spinId,"payout",17,17), "A result retry can duplicate the prize in the visible owner wallet.");
Check(!walletReceipts.Claim(spinId.ToUpperInvariant(),"payout",17,17), "A case-changed receipt can duplicate a prize.");
Check(walletReceipts.Claim(spinId,"refund",17,17) && !walletReceipts.Claim(spinId,"refund",17,17), "A repeated cancellation can duplicate a refund.");
Check(!walletReceipts.Claim("invalid","payout",17,17) && !walletReceipts.Claim(Guid.NewGuid().ToString("N"),"unknown",17,17), "An invalid wallet instruction was accepted.");
var applied = new SlotWalletReceipts(); var ownAccumulated = 1000; var ownSpent = 0;
void ApplyOwnWallet(int delta, bool spent) { if (spent) ownSpent += delta; else ownAccumulated += delta; }
Check(!applied.Apply(spinId,"debit",18,17,200,ApplyOwnWallet) && ownSpent == 0, "Another player's debit changed this wallet.");
Check(applied.Apply(spinId,"debit",17,17,200,ApplyOwnWallet) && ownSpent == 200, "The owner's stake was not deducted.");
ownAccumulated += 50; // Ordinary income arrives while the reels are spinning.
Check(applied.Apply(spinId,"payout",17,17,1000,ApplyOwnWallet) && ownAccumulated - ownSpent == 1850, "A prize overwrote ordinary income during a spin.");
Check(!applied.Apply(spinId,"payout",17,17,1000,ApplyOwnWallet) && ownAccumulated - ownSpent == 1850, "A repeated payout changed the winner's wallet.");
Check(!applied.Apply(Guid.NewGuid().ToString("N"),"refund",17,17,200,ApplyOwnWallet), "An invalid refund could spend more money.");
var failingId = Guid.NewGuid().ToString("N");
Fails(() => applied.Apply(failingId,"payout",17,17,500,(delta,spent) => { ApplyOwnWallet(delta,spent); throw new Exception("Native callback failed after applying"); }), "The receipt swallowed a failed native callback.");
var afterCallbackFailure = ownAccumulated;
Check(!applied.Apply(failingId,"payout",17,17,500,ApplyOwnWallet) && ownAccumulated == afterCallbackFailure, "Retrying after a callback failure applied the prize twice.");
var soundTimeline = new SlotSoundTimeline();
Check(!soundTimeline.ConfirmWin(""), "An unknown or empty round played a jackpot sound.");
Check(soundTimeline.Begin(spinId,0).SequenceEqual(new[] { SlotSoundCue.Lever, SlotSoundCue.CycleStart }), "A confirmed new spin did not start lever and cycle audio.");
Check(soundTimeline.Begin(spinId,0).Length == 0 && soundTimeline.Advance(1.79f).Length == 0, "A retried spin replayed the lever or a number clicked early.");
Check(soundTimeline.Advance(SlotSoundTimeline.ReelStop(0)).SequenceEqual(new[] { SlotSoundCue.NumberLand }), "The first number's sound missed its visible stop.");
Check(soundTimeline.Advance(SlotSoundTimeline.ReelStop(0)).Length == 0, "A number played its stop sound twice.");
Check(soundTimeline.Advance(SlotSoundTimeline.ReelStop(1)).SequenceEqual(new[] { SlotSoundCue.NumberLand }), "The second number's sound missed its visible stop.");
Check(soundTimeline.Advance(SlotSoundTimeline.ReelStop(2)).SequenceEqual(new[] { SlotSoundCue.NumberLand, SlotSoundCue.CycleStop }) && soundTimeline.Landed == 3, "The third number did not click and stop the cycle.");
Check(soundTimeline.Advance(5).Length == 0, "A settled spin kept triggering cycle or land sounds.");
Check(!soundTimeline.ConfirmWin(Guid.NewGuid().ToString("N")) && soundTimeline.ConfirmWin(spinId) && !soundTimeline.ConfirmWin(spinId), "A wrong round or duplicate payout replayed the win sound.");
var lateSoundId = Guid.NewGuid().ToString("N");
Check(soundTimeline.Begin(lateSoundId,2).SequenceEqual(new[] { SlotSoundCue.CycleStart }) && soundTimeline.Landed == 1, "A late join replayed the lever or already stopped number.");
Check(soundTimeline.Advance(3).SequenceEqual(new[] { SlotSoundCue.NumberLand, SlotSoundCue.NumberLand, SlotSoundCue.CycleStop }), "Late join failed to finish only the remaining reels.");
soundTimeline.Reset();
Check(soundTimeline.Advance(3).Length == 0 && !soundTimeline.ConfirmWin(lateSoundId), "Audio survived a scene/session reset.");
Check(soundTimeline.Begin(Guid.NewGuid().ToString("N"),3).Length == 0, "Joining after all reels stopped replayed cycle sounds.");
Check(ExchangeRules.PedestalBase(200.5f) == 200 && ExchangeRules.PedestalBase(201.5f) == 202, "Pedestal rounding differs from native round-to-even.");
Check(ExchangeRules.PedestalBase(float.NaN) == -1 && ExchangeRules.PedestalBase(float.PositiveInfinity) == -1 && ExchangeRules.PedestalBase(-1) == -1 && ExchangeRules.PedestalBase(float.MaxValue) == -1, "Invalid scaled pedestal prices were accepted.");
Check(ExchangeRules.PlayerPrice(200,0) == 200 && ExchangeRules.Markup(200) == 253, "A native 200-Prism Eternal Heart offer did not become 253 with the markup.");
Check(ExchangeRules.PlayerPrice(201,-.1f) == 180 && ExchangeRules.Markup(180) == 228, "The buyer's discount did not follow the pedestal's floorf adjustment before the markup.");
Check(ExchangeRules.Markup(1) == 2 && ExchangeRules.Markup(101) == 128 && ExchangeRules.Markup(0) == 0, "The 26.5% premium rounded down or changed a native zero price.");
Check(ExchangeRules.Markup(int.MaxValue) == -1 && ExchangeRules.Markup(-1) == -1, "The exchange premium overflowed or priced an unavailable item.");
Check(ExchangeRules.PlayerPrice(200,float.NaN) == -1 && ExchangeRules.PlayerPrice(200,-2) == -1 && ExchangeRules.PlayerPrice(int.MaxValue,1) == -1, "Invalid player modifiers produced valid shop prices.");
Check(Enumerable.Range(0,5).All(ExchangeRules.AllowedType) && !ExchangeRules.AllowedType(5) && !ExchangeRules.AllowedType(-1) && !ExchangeRules.AllowedType(6), "Triads or unknown item types remained eligible for the store.");
var exchangeId = Guid.NewGuid().ToString("N");
var exchange = new ExchangeOrder(exchangeId,17,42,230,0,0);
Check(!exchange.ClaimGrant(17,exchangeId,0,0), "An unpaid co-op order granted an item.");
Check(!exchange.ClaimGrant(18,exchangeId,230,230) && !exchange.ClaimGrant(17,Guid.NewGuid().ToString("N"),230,230), "Another owner or request could authorize item delivery.");
Check(!exchange.ClaimGrant(17,exchangeId,0,230) && !exchange.ClaimGrant(17,exchangeId,230,0), "A server-only or client-only debit granted an item.");
Check(exchange.ClaimGrant(17,exchangeId,230,230) && !exchange.ClaimGrant(17,exchangeId,230,230), "The paid order failed or a repeated acknowledgment granted twice.");
Check(!exchange.Refund(), "An order whose grant began could also receive a speculative refund.");
Check(!exchange.ObserveDelivery(0,230) && !exchange.ObserveDelivery(1,229), "An unchanged inventory or incorrect payment confirmed delivery.");
Check(exchange.ObserveDelivery(1,230), "Native inventory and payment did not confirm a delivered item.");
Check(!exchange.Complete(18,exchangeId,1,1,230) && !exchange.Complete(17,exchangeId,0,1,230), "A teammate or stale owner inventory completed a purchase.");
Check(exchange.Complete(17,exchangeId,1,1,230) && !exchange.Complete(17,exchangeId,1,1,230), "A settled purchase was not completed exactly once.");
var cancelledExchange = new ExchangeOrder(Guid.NewGuid().ToString("N"),18,43,230,80,2);
Check(cancelledExchange.Refund() && !cancelledExchange.Refund() && !cancelledExchange.ClaimGrant(18,cancelledExchange.Id,310,310), "A cancelled purchase could grant an item or refund twice.");
Check(!cancelledExchange.CompleteRefund(17,cancelledExchange.Id,80,80) && !cancelledExchange.CompleteRefund(18,cancelledExchange.Id,80,310), "A wrong owner or unconfirmed wallet authorized a refund settlement.");
Check(cancelledExchange.CompleteRefund(18,cancelledExchange.Id,80,80), "An exactly restored owner wallet did not complete cancellation.");
var aliceFunds = new ShopFunds(1000,1000,0,0); var bobFunds = new ShopFunds(600,600,80,80);
var alicePayment = new ShopPayment(aliceFunds,230); var bobPayment = new ShopPayment(bobFunds,345);
Check(alicePayment.Charged(new(770,770,230,230)) && !bobPayment.Charged(bobFunds), "Alice's debit also confirmed Bob's separate order.");
Check(bobPayment.Charged(new(255,255,425,425)) && alicePayment.Charged(new(820,820,230,230)), "Independent co-op purchases or concurrent ordinary Prism income failed settlement.");
var partialExchange = new ExchangeOrder(Guid.NewGuid().ToString("N"),17,42,230,0,0);
Check(!partialExchange.ClaimGrant(17,partialExchange.Id,100,100) && partialExchange.Refund(), "A partial payment granted an item or could not be cancelled.");
partialExchange.Fail();
Check(!partialExchange.ClaimGrant(17,partialExchange.Id,230,230) && !partialExchange.Refund(), "An unresolved native delivery could be retried or speculatively refunded.");
Fails(() => new ExchangeOrder("bad",17,42,230,0,0), "An invalid exchange request ID was accepted.");
Fails(() => new ExchangeOrder(exchangeId,17,42,230,int.MaxValue,0), "A purchase overflowed the spent counter.");
var quotePacket = new SlotMessage { Kind = "exchange", Scene=998, Session = Guid.NewGuid().ToString("N"), Visit = Guid.NewGuid().ToString("N"), Shop = new ExchangeMessage {
    Kind = "quotes", Visit = Guid.NewGuid().ToString("N"), Quotes = Enumerable.Range(0,ModProtocol.QuoteBatch).Select(_ => new ExchangeQuote { Item = int.MaxValue, Native = int.MaxValue, Price = int.MaxValue, Sale=int.MaxValue, Currency = 0 }).ToArray() } };
var encodedQuote = System.Text.Json.JsonSerializer.Serialize(quotePacket);
Check(encodedQuote.Length <= ModProtocol.MaxMessageChars, "A full quote page exceeds the reliable transport's message bound.");
Check(System.Text.Json.JsonSerializer.Deserialize<SlotMessage>(encodedQuote)!.Shop!.Quotes!.Length == ModProtocol.QuoteBatch && quotePacket.Version == 12 && System.Text.Json.JsonSerializer.Deserialize<SlotMessage>(encodedQuote)!.Scene==998, "The combined shop/slot protocol lost room context, quotes or retained the incompatible old version.");
Check(CustomModRules.IsRestAreaScene(20) && CustomModRules.IsRestAreaScene(998) && new[]{0,10,100,999,1000}.All(s=>!CustomModRules.IsRestAreaScene(s)),"The exchange/slot room gate excluded Quiet Place or admitted a combat/menu scene.");
Check(new BalanceProfile { QuietBayShop = true, PrismJackpot = false }.CustomConfig().Contains("[QuietBayShop]\nEnabled = true"), "The store could not be enabled with the slot machine disabled.");
using (var f = new Fixture())
{
    var stateRoot = Path.Combine(f.Manager,"user-data");
    var portable = new GameService(f.Game,stateRoot,() => false,f.Saves,packageRoot:f.Manager)
    { ExpectedAssembly=f.Service.ExpectedAssembly, ExpectedMetadata=f.Service.ExpectedMetadata };
    portable.SaveProfile(new BalanceProfile { QuietBayShop=false,PrismJackpot=true,Metalheads25=false });
    portable.Install();
    Check(portable.Installed && File.Exists(portable.ReceiptPath) && !File.Exists(f.Service.ReceiptPath), "Portable installation stored its receipt in the package cache instead of per-game data.");
    Check(File.ReadAllText(Path.Combine(f.Game,"BepInEx/config/"+GameService.CustomPluginId+".cfg")).Contains("[QuietBayShop]\nEnabled = false"), "Portable installation ignored its own feature switches.");
    var restarted = new GameService(f.Game,stateRoot,() => false,f.Saves,packageRoot:f.Manager);
    Check(restarted.Installed && !restarted.Profile().QuietBayShop && restarted.Profile().PrismJackpot && !restarted.Profile().Metalheads25, "Replacing or moving the portable EXE lost its receipt or switches.");
    portable.Uninstall();
    Check(File.ReadAllText(Path.Combine(f.Saves,"Saves/progress"))=="save-data", "Portable setup/removal changed saved progress.");
}
using (var f = new Fixture())
{
    var data = Path.Combine(f.Manager,"local");
    Check(PortablePackage.DataRoot(f.Game,data)==Path.Combine(data,"games",PortablePackage.Key(f.Game)), "A fresh installation inherited another user's manager data.");
    var legacy = Path.Combine(f.Game,"GatekeeperModManager");
    Storage.Json(Path.Combine(legacy,"state/installation.json"),new InstallReceipt { GameRoot=f.Game,AssemblyHash="a",MetadataHash="b",Complete=true });
    Check(PortablePackage.DataRoot(f.Game,data)==legacy, "The single-file manager abandoned an existing installation's receipts/backups.");
    Check(PortablePackage.Key(f.Game)==PortablePackage.Key(f.Game.ToUpperInvariant()+Path.DirectorySeparatorChar) && PortablePackage.Key(f.Game)!=PortablePackage.Key(Path.Combine(f.Game,"other")), "Per-game data keys changed with path spelling or mixed installations.");
}
MemoryStream Bundle(params (string path,string text)[] files)
{
    var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream,ZipArchiveMode.Create,true))
    {
        var records = files.Select(file =>
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(file.text);
            using var content = zip.CreateEntry(file.path).Open(); content.Write(bytes);
            return new BundleFile(file.path,bytes.Length,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
        }).ToArray();
        using var metadata = zip.CreateEntry("bundle.json").Open();
        System.Text.Json.JsonSerializer.Serialize(metadata,new BundleManifest("0.7.0",records));
    }
    stream.Position=0; return stream;
}
using (var f = new Fixture())
{
    var cache=Path.Combine(f.Manager,"cache");
    using var source=Bundle(("packages/mods/Gatekeeper.CustomMods.dll","embedded plugin and all four sounds"),("notices/LICENSE","license"));
    var manifest=PortablePackage.Extract(source,cache);
    var plugin=Path.Combine(cache,"packages/mods/Gatekeeper.CustomMods.dll");
    Check(manifest.Files.Length==2 && File.ReadAllText(plugin)=="embedded plugin and all four sounds", "Embedded component extraction lost content.");
    File.WriteAllText(plugin,"damaged cache"); source.Position=0; PortablePackage.Extract(source,cache);
    Check(File.ReadAllText(plugin)=="embedded plugin and all four sounds", "A damaged cached component was trusted instead of repaired from the EXE.");
    var modified=File.GetLastWriteTimeUtc(plugin); source.Position=0; PortablePackage.Extract(source,cache);
    Check(File.GetLastWriteTimeUtc(plugin)==modified, "Valid cached components were needlessly overwritten.");
    foreach (var invalid in new[] { "../escape.txt","packages/mods/../../escape.txt","packages/mods/file:stream","packages/mods/./bad","state/profile.json","packages\\mods\\wrong.dll" })
    { using var bad=Bundle((invalid,"bad")); Fails(()=>PortablePackage.Extract(bad,cache),"Invalid embedded path escaped the package area: "+invalid); }
    using var duplicate=Bundle(("packages/mods/test.dll","a"),("packages/mods/TEST.dll","b"));
    Fails(()=>PortablePackage.Extract(duplicate,cache),"Case-colliding bundle entries were accepted.");
    using var tampered=new MemoryStream();
    using (var zip=new ZipArchive(tampered,ZipArchiveMode.Create,true))
    {
        using (var content=zip.CreateEntry("packages/mods/test.dll").Open()) content.WriteByte(42);
        using var metadata=zip.CreateEntry("bundle.json").Open();
        System.Text.Json.JsonSerializer.Serialize(metadata,new BundleManifest("test",new[] {new BundleFile("packages/mods/test.dll",1,new string('0',64))}));
    }
    tampered.Position=0;
    Fails(()=>PortablePackage.Extract(tampered,cache),"A component whose bytes failed the embedded manifest hash was used.");
    Check(!File.Exists(Path.Combine(cache,"packages/mods/test.dll")),"A failed integrity check installed the corrupt component.");
}
using (var f = new Fixture())
{
    var steam=Path.Combine(f.Manager,"Steam"); var secondary=Path.Combine(f.Manager,"Other Steam Library");
    Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
    File.WriteAllText(Path.Combine(steam,"steamapps/libraryfolders.vdf"),"\"libraryfolders\" { \"0\" { \"path\" \""+secondary.Replace("\\","\\\\")+"\" \"apps\" { \"2106670\" \"1234\" } } }");
    var moved=Path.Combine(secondary,"steamapps/common/Custom Gatekeeper Folder"); Directory.CreateDirectory(moved);
    File.Copy(Path.Combine(f.Game,"Gatekeeper.exe"),Path.Combine(moved,"Gatekeeper.exe"));
    File.Copy(Path.Combine(f.Game,"GameAssembly.dll"),Path.Combine(moved,"GameAssembly.dll"));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(moved,GameService.MetadataFile))!);
    File.Copy(Path.Combine(f.Game,GameService.MetadataFile),Path.Combine(moved,GameService.MetadataFile));
    File.WriteAllText(Path.Combine(secondary,"steamapps/appmanifest_2106670.acf"),"\"AppState\" { \"appid\" \"2106670\" \"installdir\" \"Custom Gatekeeper Folder\" }");
    Check(SteamDiscovery.LibraryPaths(File.ReadAllText(Path.Combine(steam,"steamapps/libraryfolders.vdf"))).SequenceEqual(new[] {secondary}),"Steam library paths were not decoded.");
    Check(SteamDiscovery.Find(new[] {steam,steam}).SequenceEqual(new[] {Path.GetFullPath(moved)}),"Discovery missed Gatekeeper in another Steam library/custom install folder.");
    Check(SteamDiscovery.LibraryPaths("\"1\" \""+secondary.Replace("\\","\\\\")+"\"").Single()==secondary,"Legacy Steam library paths were not decoded.");
    File.WriteAllText(Path.Combine(secondary,"steamapps/appmanifest_2106670.acf"),"\"installdir\" \"../../escape\"");
    Check(SteamDiscovery.Find(new[] {steam}).Length==0,"A Steam manifest escaped the common folder.");
    Check(!SteamDiscovery.IsGame(steam),"A folder without Gatekeeper binaries was accepted as the game.");
}
Check(ExchangeRules.SalePrice(200)==100 && ExchangeRules.Markup(200)==253,"Selling used the marked-up store price instead of 50% of native value.");
Check(ExchangeRules.SalePrice(201)==100 && ExchangeRules.SalePrice(1)==0 && ExchangeRules.SalePrice(0)==0 && ExchangeRules.SalePrice(-1)==-1,"Half-value payouts rounded up or accepted an unavailable price.");
Check(ExchangeRules.SalePrice(ExchangeRules.PlayerPrice(201,-.1f))==90,"Sale pricing skipped the player's current native discount.");
Check(ExchangeRules.CanCredit(100,100,100) && !ExchangeRules.CanCredit(int.MaxValue,100,1) && !ExchangeRules.CanCredit(100,int.MaxValue,1),"Sale proceeds can overflow the seller's wallet.");
Check(!ExchangeRules.CanCredit(100,100,0) && !ExchangeRules.CanCredit(-1,100,10),"A valueless or invalid wallet allowed item destruction.");
var saleId=Guid.NewGuid().ToString("N");
var sale=new SaleOrder(saleId,17,2,100);
Check(!sale.ClaimCredit(18,saleId,1,1) && !sale.ClaimCredit(17,Guid.NewGuid().ToString("N"),1,1),"A teammate or wrong order claimed sale proceeds.");
Check(!sale.ClaimCredit(17,saleId,2,2),"An unchanged inventory generated sale money.");
Check(!sale.ClaimCredit(17,saleId,1,2) && !sale.ClaimCredit(17,saleId,2,1),"Only one peer confirming removal paid a sale.");
Check(!sale.ClaimCredit(17,saleId,0,0),"An ambiguous multi-stack removal paid a one-stack sale.");
Check(sale.ClaimCredit(17,saleId,1,1) && !sale.ClaimCredit(17,saleId,1,1),"A confirmed sale failed or a duplicate removal receipt paid twice.");
Check(!sale.Cancel(2),"An already-paid sale could be cancelled.");
Check(!sale.Complete(18,saleId,1,1,200,200,200) && !sale.Complete(17,saleId,2,1,200,200,200),"Another player or stale inventory completed a sale.");
Check(!sale.Complete(17,saleId,1,1,100,200,200) && !sale.Complete(17,saleId,1,1,200,100,200),"Server-only or owner-only sale credits completed a trade.");
Check(sale.Complete(17,saleId,1,1,225,225,200) && !sale.Complete(17,saleId,1,1,225,225,200),"Concurrent normal income broke sale completion or a duplicate payout completed twice.");
var lastStack=new SaleOrder(Guid.NewGuid().ToString("N"),18,1,100);
Check(lastStack.ClaimCredit(18,lastStack.Id,0,0),"Selling the last owned stack was blocked.");
var cancelSale=new SaleOrder(Guid.NewGuid().ToString("N"),18,3,100);
Check(!cancelSale.Cancel(2) && cancelSale.Cancel(3) && !cancelSale.ClaimCredit(18,cancelSale.Id,2,2),"A cancelled sale could still pay or silently lose a removed item.");
var failedSale=new SaleOrder(Guid.NewGuid().ToString("N"),17,1,100); failedSale.Fail();
Check(!failedSale.ClaimCredit(17,failedSale.Id,0,0) && !failedSale.Cancel(1),"An unresolved sale retried removal/payment.");
Fails(()=>new SaleOrder(saleId,17,0,100),"An empty inventory created a sale.");
Fails(()=>new SaleOrder(saleId,17,1,0),"A zero-value sale destroyed an item.");
Check(CannonRules.ItemId>2005 && CannonRules.MaxStacks==10,"Cannon Wheel reused an official item ID or lost its stack cap.");
Check(CannonRules.DamageMultiplier(0)==0 && CannonRules.DamageMultiplier(1)==2.5f && CannonRules.DamageMultiplier(2)==2.5f && CannonRules.DamageMultiplier(10)==2.5f,"Cannon Wheel must deal 250% per cannon without an additional hidden stack multiplier.");
Check(CannonRules.DamageMultiplier(10)==CannonRules.DamageMultiplier(1000),"Cannon damage exceeded its ten-stack ceiling.");
Check(CannonRules.Cooldown(1)==2 && CannonRules.Cooldown(10)==2 && CannonRules.Cooldown(10)>CannonRules.Flight(CannonRules.Range),"Cannon fire timing can reorder approved impacts.");
Check(float.IsPositiveInfinity(CannonRules.Cooldown(0)) && CannonRules.Flight(0)==.08f && CannonRules.Flight(999)==.75f,"Empty stacks or extreme projectile travel produced invalid timing.");
Check(CannonRules.CanFire(true,true,true,1,true,2,1) && !CannonRules.CanFire(false,true,true,1,true,2,1),"A client could independently authorize cannon fire.");
Check(!CannonRules.CanFire(true,false,true,1,true,2,1) && !CannonRules.CanFire(true,true,false,1,true,2,1) && !CannonRules.CanFire(true,true,true,0,true,2,1),"A cannon fired outside gameplay, while dead or without its item.");
Check(!CannonRules.CanFire(true,true,true,1,false,2,1) && !CannonRules.CanFire(true,true,true,1,true,.5f,1) && !CannonRules.CanFire(true,true,true,1,true,float.NaN,1),"A cannon ignored targets, cooldowns or invalid time.");
Check(CannonRules.ValidDamage(150) && !CannonRules.ValidDamage(float.NaN) && !CannonRules.ValidDamage(float.PositiveInfinity) && !CannonRules.ValidDamage(-1),"Invalid cannon damage reached the native hit path.");
var cannonReceipts=new CannonReceipts();
Check(cannonReceipts.Claim(5,1,"shot") && !cannonReceipts.Claim(5,1,"shot") && cannonReceipts.Claim(5,1,"hit"),"A duplicate shot replayed, or a visual packet consumed the damage receipt.");
Check(cannonReceipts.Claim(5,3,"hit") && !cannonReceipts.Claim(5,2,"hit") && !cannonReceipts.Claim(5,3,"hit"),"Old or duplicate approved hits could apply native damage twice.");
Check(cannonReceipts.Claim(6,1,"hit") && !cannonReceipts.Claim(-1,1,"hit") && !cannonReceipts.Claim(5,0,"hit") && !cannonReceipts.Claim(5,4,"unknown"),"Co-op characters shared hit receipts or accepted invalid packets.");
cannonReceipts.Clear();Check(cannonReceipts.Claim(5,1,"hit"),"A new network session retained stale hit receipts.");
var cannonProgress=new CannonProgressData();var winRound=Guid.NewGuid().ToString("N");
Check(!cannonProgress.Unlocked("alice") && !cannonProgress.Award("alice","invalid"),"An invalid jackpot unlocked Cannon Wheel.");
Check(cannonProgress.Award("alice",winRound) && cannonProgress.Unlocked("alice") && !cannonProgress.Unlocked("bob"),"A jackpot unlocked the item for another Steam account.");
Check(!cannonProgress.Award("alice",Guid.NewGuid().ToString("N")) && cannonProgress.Winners["alice"]==winRound,"A repeat win overwrote the permanent unlock proof.");
var restoredProgress=System.Text.Json.JsonSerializer.Deserialize<CannonProgressData>(System.Text.Json.JsonSerializer.Serialize(cannonProgress))!;
Check(restoredProgress.Unlocked("alice") && !restoredProgress.Unlocked("bob"),"The jackpot unlock did not survive saving/loading.");
Check(new BalanceProfile().CannonWheel && new BalanceProfile{CannonWheel=false}.CustomConfig().Contains("[CannonWheel]\nEnabled = false"),"The manager did not expose an independent Cannon Wheel switch.");
using(var f=new Fixture())
{
    f.Service.Install();
    var marker=Path.Combine(f.Game,"BepInEx/config/"+GameService.CustomPluginId+".cannon-run.json");
    Storage.Json(marker,new CannonRunMarker(true,CannonRules.ItemId,1,"alice"));
    Fails(()=>f.Service.SetLoader(false),"Vanilla mode disabled a loader needed by a saved custom item.");
    Fails(()=>f.Service.Uninstall(),"Removing the loader abandoned a saved Cannon Wheel run.");
    f.Service.SaveProfile(new BalanceProfile{CannonWheel=false});
    Check(!f.Service.Profile().CannonWheel,"The cannon effect could not be disabled while its metadata is needed.");
    f.Service.SetLoader(true);Check(f.Service.LoaderEnabled,"A custom-item run could not continue modded.");
    var progressFile=Path.Combine(f.Game,"BepInEx/config/"+GameService.CustomPluginId+".cannon-progress.json");
    Storage.Json(progressFile,cannonProgress);
    var backed=f.Service.BackupSaves()!;
    Check(File.Exists(Path.ChangeExtension(backed,"cannon-progress.json")) && File.ReadAllText(progressFile)==File.ReadAllText(Path.ChangeExtension(backed,"cannon-progress.json")),"Save backups lost permanent Cannon Wheel unlocks.");
    Storage.Json(marker,new CannonRunMarker(false,CannonRules.ItemId,0,"alice"));
    f.Service.SetLoader(false);Check(!f.Service.LoaderEnabled,"A finished Cannon Wheel run permanently blocked vanilla mode.");
    f.Service.Uninstall();
    Check(File.Exists(progressFile),"Removing the loader erased a permanent Cannon Wheel unlock.");
}
Check(CannonRules.Count(2)==2 && CannonRules.Count(999)==10 && CannonRules.Count(-1)==0,"Stack count did not create exactly one cannon per item within its cap.");
Check(CannonRules.HasUnit(2,0) && CannonRules.HasUnit(2,1) && !CannonRules.HasUnit(2,2) && !CannonRules.HasUnit(2,-1),"Buying or selling stacks left the wrong cannon units active.");
Check(!CannonRules.InCombatLevel(true,20) && !CannonRules.InCombatLevel(true,10) && !CannonRules.InCombatLevel(true,998),"Cannons spawned in Quiet Bay, the hub or another quiet room.");
Check(CannonRules.InCombatLevel(true,101) && CannonRules.InCombatLevel(true,500) && CannonRules.InCombatLevel(true,725) && CannonRules.InCombatLevel(true,999),"A combat level, arena or final boss room blocked cannon spawning.");
Check(!CannonRules.InCombatLevel(false,101) && !CannonRules.InCombatLevel(true,3) && !CannonRules.InCombatLevel(true,95) && !CannonRules.InCombatLevel(true,1000),"Cannons spawned in a menu, transition or unsupported scene.");
Check(Math.Abs(CannonRules.Range/18-1.35f)<.00001f && CannonRules.ProjectileSpeed>26 && CannonRules.Flight(CannonRules.Range)<18f/26,"The requested 35% range increase or faster shell flight was lost.");
Check(CannonRules.ChooseMove(CannonMove.Patrol,5,true,22,true)==CannonMove.Advance && CannonRules.ChooseMove(CannonMove.Advance,5,true,15,true)==CannonMove.Advance,"Cannons did not pursue enemies independently or stopped short of their firing position.");
Check(CannonRules.ChooseMove(CannonMove.Advance,5,true,13,true)==CannonMove.Hold && CannonRules.ChooseMove(CannonMove.Hold,5,true,17,true)==CannonMove.Hold,"Firing-position hysteresis caused constant chasing/stopping.");
Check(CannonRules.ChooseMove(CannonMove.Hold,5,true,12,false)==CannonMove.Advance,"A blocked cannon never sought a clear firing position.");
Check(CannonRules.ChooseMove(CannonMove.Hold,5,true,4,true)==CannonMove.Retreat && CannonRules.ChooseMove(CannonMove.Retreat,5,true,8,true)==CannonMove.Retreat && CannonRules.ChooseMove(CannonMove.Retreat,5,true,10,true)==CannonMove.Hold,"Ranged cannons did not make room from nearby enemies.");
Check(CannonRules.ChooseMove(CannonMove.Hold,34,true,10,true)==CannonMove.Return && CannonRules.ChooseMove(CannonMove.Return,20,true,10,true)==CannonMove.Return && CannonRules.ChooseMove(CannonMove.Return,10,false,0,false)==CannonMove.Patrol,"An autonomous cannon abandoned its owner or never resumed roaming.");
Check(!CannonRules.CanTrack(40,5,true) && !CannonRules.CanTrack(10,30,false) && CannonRules.CanTrack(10,30,true),"Target retention ignored its owner leash or lost every moving target at the acquisition boundary.");
var patrol=new CannonTactics(0,123);var originPoint=System.Numerics.Vector3.Zero;
var roam=patrol.Update(originPoint,originPoint,null,false,0);
Check(roam.Length()>=7 && roam.Length()<=11 && patrol.Mode==CannonMove.Patrol,"Idle cannons remained glued to the player.");
Check(patrol.Update(originPoint,originPoint,null,false,1)==roam,"An idle patrol point changed every frame.");
Check(patrol.Update(originPoint,originPoint,null,false,10)!=roam,"An idle cannon never chose another patrol point.");
var movedOwner=new System.Numerics.Vector3(15,0,0);var shifted=patrol.Update(movedOwner,originPoint,null,false,10.1f);
Check(System.Numerics.Vector3.Distance(shifted,movedOwner)>=7 && System.Numerics.Vector3.Distance(shifted,movedOwner)<=11,"Patrols failed to recenter around a moving owner.");
var hunter=new CannonTactics(1,321);var firingGoal=hunter.Update(originPoint,originPoint,new(28,0,0),true,0);
Check(hunter.Mode==CannonMove.Advance && firingGoal.X>10 && System.Numerics.Vector3.Distance(firingGoal,new(28,0,0))<15,"A cannon followed its owner instead of approaching a ranged firing position.");
var occludedHunter=new CannonTactics(1,333);var occludedGoal=occludedHunter.Update(originPoint,originPoint,new(12,0,0),false,0);
Check(occludedHunter.Mode==CannonMove.Advance && occludedGoal.X>7,"An obstructed cannon chose a standoff point that kept it behind the wall.");
var returnGoal=hunter.Update(originPoint,new(40,0,0),new(42,0,0),true,1);
Check(hunter.Mode==CannonMove.Return && returnGoal.Length()<13,"A cannon kept chasing enemies after its owner moved away.");
var enemyPoint=new System.Numerics.Vector3(18,0,0);var enemyVelocity=new System.Numerics.Vector3(0,0,6);
Check(CannonRules.PredictAim(originPoint,enemyPoint,originPoint)==enemyPoint,"A stationary target received unnecessary aim drift.");
var ledPoint=CannonRules.PredictAim(originPoint,enemyPoint,enemyVelocity);
var newTravel=CannonRules.Flight(ledPoint.Length());var oldMiss=(enemyVelocity*(18f/26)).Length();
var newMiss=System.Numerics.Vector3.Distance(ledPoint,enemyPoint+enemyVelocity*newTravel);
Check(oldMiss>CannonRules.BlastRadius && newMiss<CannonRules.BlastRadius && ledPoint.Z>0,"Faster shells and partial lead did not improve a moving-target shot.");
Check(System.Numerics.Vector3.Distance(ledPoint,enemyPoint-enemyVelocity*newTravel)>CannonRules.BlastRadius,"Predicted shots became guaranteed hits after an enemy reversed direction.");
Check(System.Numerics.Vector3.Distance(CannonRules.PredictAim(originPoint,enemyPoint,new(1000,0,1000)),enemyPoint)<=CannonRules.MaxLead+.0001f,"Teleporting/fast enemies produced unbounded prediction.");
Check(CannonRules.PredictAim(originPoint,enemyPoint,new(float.NaN,0,0))==enemyPoint,"Invalid native velocity corrupted the target point.");
Check(CannonRules.FollowSpeed(8,12)>12 && CannonRules.FollowSpeed(1,5)<CannonRules.FollowSpeed(8,5),"Cannons could not catch a sprinting owner or adjust smoothly with distance.");
Check(!CannonRules.ShouldRejoin(20,false,false,10) && !CannonRules.ShouldRejoin(40,true,false,10) && !CannonRules.ShouldRejoin(40,false,true,10) && !CannonRules.ShouldRejoin(40,false,false,1),"Ordinary following or a pending/reachable path triggered a teleport.");
Check(CannonRules.ShouldRejoin(40,false,false,3),"An unreachable distant cannon could never rejoin its owner.");
var perUnit=new CannonReceipts();
Check(perUnit.Claim(5,20,"hit",0) && perUnit.Claim(5,19,"hit",1),"A second cannon lost its approved damage to another cannon's newer receipt.");
Check(!perUnit.Claim(5,20,"hit",0) && !perUnit.Claim(5,19,"hit",1),"Multiple cannons allowed duplicate native hits.");
Check(perUnit.Claim(6,19,"hit",1) && !perUnit.Claim(5,30,"hit",10),"Co-op ownership or the cannon-unit bound was lost.");
var poses=new CannonPoseBuffer();
Check(poses.Push(0,new(0,0,0),350,new(10,0,0),0) && poses.Push(.1f,new(1,0,0),10,new(10,0,0),0),"Valid follower snapshots were rejected.");
var halfway=poses.At(.17f);
Check(Math.Abs(halfway.Position.X-.5f)<.0001f && Math.Abs(halfway.Yaw-360)<.001f,"Remote cannons did not interpolate between packets or turned the long way across zero degrees.");
var frameSteps=Enumerable.Range(1,5).Select(i=>poses.At(.12f+i*.016f).Position.X).ToArray();
Check(frameSteps.Zip(frameSteps.Skip(1),(a,b)=>b-a).All(d=>d>.15f && d<.17f),"A remote cannon stopped between network updates.");
Check(poses.At(10).Position.X<2.21f,"A missing network packet extrapolated the cannon indefinitely.");
Check(!poses.Push(.05f,new(999,0,0),0,new(),0) && !poses.Push(.2f,new(float.NaN,0,0),0,new(),0),"Stale or invalid snapshots disturbed follower motion.");
Check(poses.Push(.3f,new(50,0,0),0,new(),1) && poses.At(.3f).Position.X==50,"A real far-distance rejoin interpolated through the entire level.");
Check(!poses.Push(.4f,new(1,0,0),0,new(),0),"A stale pre-teleport snapshot pulled a cannon back.");
var cannonPacket=new SlotMessage{Kind="cannon",Session=Guid.NewGuid().ToString("N"),Cannon=new(){Kind="hit",World=Guid.NewGuid().ToString("N"),Sequence=long.MaxValue,Character=int.MaxValue,Owner=int.MaxValue,Unit=9,Warp=int.MaxValue,Yaw=359.99f,Velocity=new[]{99999f,99999f,99999f},Scene=99,Damage=10_000_000,Position=new[]{99999f,99999f,99999f},Targets=Enumerable.Repeat(int.MaxValue,12).ToArray()}};
Check(System.Text.Json.JsonSerializer.Serialize(cannonPacket).Length<=ModProtocol.MaxMessageChars,"A full cannon impact exceeded the transport bound.");

// Forge prototype: serial charge economy, passive timing and co-op isolation.
var forgeGate=new ForgeChargeGate();
Check(forgeGate.TrySpend(0,2,8) && forgeGate.TrySpend(0,2,8) && !forgeGate.TrySpend(0,2,8),"Forge spent more than its charge capacity.");
Check(!forgeGate.TrySpend(7.99,2,8) && forgeGate.TrySpend(8,2,8) && !forgeGate.TrySpend(8,2,8),"Forge recharged all charges in parallel.");
Check(forgeGate.TrySpend(16,2,8) && !forgeGate.TrySpend(16,2,8),"Forge serial recharge was not preserved across recasts.");
Check(!new ForgeChargeGate().TrySpend(double.NaN,1,8) && !new ForgeChargeGate().TrySpend(1,1,double.PositiveInfinity) && !new ForgeChargeGate().TrySpend(1,0,8),"Invalid Forge timing or charge capacity was accepted.");
var passive=new ForgePassiveState();passive.Observe(0,false);
Check(!passive.Stationary(.99) && passive.Stationary(1),"Forge stationary repair skipped its one-second settle period.");
passive.Observe(2,true);Check(!passive.Stationary(2.9) && passive.Stationary(3),"Movement did not restart stationary repair.");
passive.EnemyKilledTurret(4);Check(passive.Enraged(8.99) && !passive.Enraged(9),"Enrage did not last five seconds.");
passive.EnemyKilledTurret(8);Check(passive.RageUntil==13 && !passive.Enraged(13),"Repeated Enrage stacked duration instead of refreshing.");
passive.EnemyKilledTurret(double.NaN);Check(passive.RageUntil==13,"Invalid Enrage timing corrupted the expiry.");
Check(ForgeRules.Regen(250,100,ForgeRules.TurretRegen)==3.75f && ForgeRules.Regen(200,100,ForgeRules.StillRegen)==2 && ForgeRules.Regen(200,199,ForgeRules.StillRegen)==1 && ForgeRules.Regen(200,0,ForgeRules.StillRegen)==0,"Passive repair overhealed, revived or used the wrong rate.");
var forgeReceipts=new ForgeReceipts();
Check(forgeReceipts.Claim(1,10,"hit") && !forgeReceipts.Claim(1,10,"hit") && !forgeReceipts.Claim(1,9,"hit"),"Forge replayed damage or accepted an older receipt.");
Check(forgeReceipts.Claim(2,9,"hit") && forgeReceipts.Claim(1,9,"consume") && forgeReceipts.Claim(1,10,"device",25),"Forge mixed up players, events, or sentries after ten deployments.");
Check(!forgeReceipts.Claim(-1,1,"hit") && !forgeReceipts.Claim(1,0,"hit") && !forgeReceipts.Claim(1,1,"hit",-1),"Forge accepted invalid receipt identifiers.");
Check(!forgeReceipts.Claim(1,9,"device",26) && forgeReceipts.Claim(1,11,"device",26),"Forge accepted a stale cross-unit state or rejected a fresh unit on its ordered stream.");
forgeReceipts.Clear();Check(forgeReceipts.Claim(1,1,"hit"),"Forge receipt state leaked between sessions.");
var longForgeRun=new ForgeReceipts();var allLongRunEventsAccepted=true;
for(var shot=1;shot<=40_000;shot++)
{
    allLongRunEventsAccepted &= longForgeRun.Claim(1,shot*3L,"rivet",shot);
    allLongRunEventsAccepted &= longForgeRun.Claim(1,shot*3L+1,"rivet-stop",shot);
    allLongRunEventsAccepted &= longForgeRun.Claim(1,shot*3L+2,"hit");
}
Check(allLongRunEventsAccepted && longForgeRun.Count==3,"A long Forge run exhausted visual receipts or retained retired projectile entries.");
Check(longForgeRun.Claim(1,120_003,"device",40_001) && longForgeRun.Claim(1,120_004,"device",40_002) && longForgeRun.Claim(1,120_005,"grenade",40_003),"Turrets, shields or grenades disappeared after sustained shooting.");
Check(!longForgeRun.Claim(1,119_999,"rivet",40_004) && !longForgeRun.Claim(1,3,"hit"),"Long-run cleanup reopened old visual or damage receipts.");
Check(longForgeRun.Claim(1,120_006,"rivet",1_000_001) && longForgeRun.Claim(1,120_007,"rivet",int.MaxValue),"Forge retained a lifetime projectile-ID cutoff.");
Check(longForgeRun.Claim(2,1,"rivet",1) && longForgeRun.Claim(2,2,"device",2),"One Forge player's long run blocked another player's visuals.");
longForgeRun.Clear();Check(longForgeRun.Count==0 && longForgeRun.Claim(1,1,"rivet",1),"A new Forge session retained sequence history.");
var rosterA=new string('a',64);var rosterB=new string('b',64);
Check(ForgeRules.Compatible(ForgeRules.Revision,rosterA,true,rosterA) && !ForgeRules.Compatible("old",rosterA,true,rosterA) && !ForgeRules.Compatible(ForgeRules.Revision,rosterB,true,rosterA) && !ForgeRules.Compatible(ForgeRules.Revision,rosterA,false,rosterA),"Forge accepted mismatched class revisions, roster order, or disabled peers.");
Check(new[]{20,998}.All(scene=>ForgeRules.Combat(true,scene) && !ForgeRules.Combat(false,scene)),
    "Forge abilities must work during Quiet Bay and Quiet Place gameplay, but not outside gameplay.");
Check(new[]{100,101,400,500,999}.All(scene=>ForgeRules.Combat(true,scene)),
    "Forge combat stages and arenas must remain available.");
Check(new[]{0,3,10,90,91,92,95,1000}.All(scene=>!ForgeRules.Combat(true,scene)) && !ForgeRules.Combat(false,101),
    "Forge devices escaped gameplay, combat stages or the two playable rest areas.");
Check(ForgeRules.Sentries(0)==3 && ForgeRules.Sentries(1)==3 && ForgeRules.Sentries(3)==4,"Forge sentry evolutions exceeded the deployment cap.");
Check(ForgeRules.Protection(0)==.50f && ForgeRules.Protection(3)==.65f && ForgeRules.Reduction(float.NaN)==0 && ForgeRules.Reduction(99)==.65f,"Forge protection was invalid or became invulnerability.");
Check(ForgeRules.Duration(4,0)==11 && Enumerable.Range(1,3).All(l=>ForgeRules.Duration(4,l)==13) && float.IsPositiveInfinity(ForgeRules.Duration(2,0)),"Longer Aegis changed another device's lifetime or lost an evolution.");
Check(ForgeRules.Healing(200,100)==4 && ForgeRules.Healing(200,199)==1 && ForgeRules.Healing(200,200)==0 && ForgeRules.Healing(200,0)==0 && ForgeRules.Healing(float.NaN,100)==0 && ForgeRules.Healing(200,float.PositiveInfinity)==0,"Aegis healing overhealed, revived, or accepted invalid health.");
Check(ForgeRules.Healing(200,100,2)==8 && ForgeRules.Healing(200,100,4)==16 && ForgeRules.Healing(200,199,2)==1 && ForgeRules.Healing(200,100,0)==0 && ForgeRules.Healing(200,0,2)==0,"Independent Aegis fields failed to add healing, overhealed or revived a recipient.");
Check(ForgeRules.Radius(0)==9 && Enumerable.Range(1,3).All(l=>ForgeRules.Radius(l)==10.5f) && ForgeRules.CastRange(4)==36,"Aegis's 50% larger base/evolved radius changed its placement distance or an evolution reverted it.");
Check(ForgeRules.ValidPoint(new[]{1f,2f,3f}) && !ForgeRules.ValidPoint(new[]{1f,float.NaN,3f}) && !ForgeRules.ValidPoint(new[]{1f,2f}),"Forge accepted malformed cast positions.");
Check(new BalanceProfile{Forge=false}.CustomConfig().Contains("[Forge]\nEnabled = false") && new BalanceProfile{Forge=true}.CustomConfig().Contains("[Forge]\nEnabled = true"),"Forge's independent manager setting was lost.");
using(var f=new Fixture())
{
    f.Service.Install();var marker=Path.Combine(f.Game,"BepInEx/config/"+GameService.CustomPluginId+".forge-run.json");
    Storage.Json(marker,new ForgeRunMarker(true,ForgeRules.CharacterId));
    Fails(()=>f.Service.SaveProfile(new BalanceProfile{Forge=false}),"Disabled Forge while a saved run depends on it.");
    Fails(()=>f.Service.SetLoader(false),"Disabled the loader while a saved Forge run depends on it.");
    Fails(()=>f.Service.Uninstall(),"Uninstalled support for an unfinished Forge run.");
    f.Service.SaveProfile(new BalanceProfile{Forge=true});
    Storage.Json(marker,new ForgeRunMarker(false,ForgeRules.CharacterId));f.Service.SaveProfile(new BalanceProfile{Forge=false});
    Check(!f.Service.Profile().Forge,"Could not disable Forge after its saved run ended.");
}
var forgePacket=new SlotMessage{Kind="forge",Forge=new(){Kind="hit",Id=Guid.NewGuid().ToString("N"),World=Guid.NewGuid().ToString("N"),Character=int.MaxValue,Sequence=long.MaxValue,Damage=10_000_000,Position=new[]{99999f,99999f,99999f},Aim=new[]{99999f,99999f,99999f},Targets=Enumerable.Repeat(int.MaxValue,32).ToArray()}};
Check(System.Text.Json.JsonSerializer.Serialize(forgePacket).Length<=ModProtocol.MaxMessageChars,"A full Forge impact exceeded the transport bound.");
var seekingPacket=new SlotMessage{Kind="forge",Forge=new ForgeMessage{Kind="rivet-turn",World=Guid.NewGuid().ToString("N"),Character=int.MaxValue,Owner=int.MaxValue,Unit=int.MaxValue,Scene=20,Slot=2,Level=3,Charged=true,Direct=true,Sequence=long.MaxValue,Position=new[]{9999.125f,-9999.125f,9999.125f},Control=new[]{9999.375f,-9999.375f,9999.375f},Aim=new[]{9999.625f,-9999.625f,9999.625f},Remaining=.15f,Speed=120}};
var seekingJson=System.Text.Json.JsonSerializer.Serialize(seekingPacket);
Check(seekingJson.Length<=ModProtocol.MaxMessageChars && System.Text.Json.JsonSerializer.Deserialize<SlotMessage>(seekingJson)!.Forge!.Control!.SequenceEqual(seekingPacket.Forge.Control!),"A seeking correction exceeded the transport bound or lost its curve.");
var seekingReceipts=new ForgeReceipts();
var seekingAccepted=true;
foreach(var kind in new[]{"rivet","rivet-turn","rivet-stop"})for(var i=1;i<=50000;i++)seekingAccepted&=seekingReceipts.Claim(17,i,kind,i);
Check(seekingAccepted,"A long-running seeking stream rejected a new projectile.");
Check(seekingReceipts.Count==3 && !seekingReceipts.Claim(17,49999,"rivet-turn",1),"Seeking receipts grew per projectile or accepted a stale hop.");

ForgeAssetChecks.Run(Check,Fails);
ForgeLocomotionChecks.Run(Check);
ExpansionKitChecks.Run(Check, Fails);
CacheSecurityChecks.Run(Check, Fails);
FinalReleaseChecks.Run(Check);
ForgeUnlockChecks.Run(Check,Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../")));
ExpansionMusicChecks.Run(Check);

if (args.Length==2 && args[0]=="--payload")
{
    using var f = new Fixture();
    var cache=Path.Combine(f.Manager,"real-payload");
    VerifiedPackage actualPackage;
    using (var source=File.OpenRead(args[1])) actualPackage=PortablePackage.Prepare(source,cache);
    var data=Path.Combine(f.Manager,"portable-state");
    var actual=new GameService(f.Game,data,()=>false,f.Saves,packageRoot:cache,verifiedPackage:actualPackage)
    { ExpectedAssembly=f.Service.ExpectedAssembly,ExpectedMetadata=f.Service.ExpectedMetadata };
    actual.Install();
    Check(actual.Installed && !actual.LoaderEnabled,"The real embedded loader could not be installed without external packages.");
    Check(GameService.Hash(Path.Combine(f.Game,GameService.CustomPlugin))==GameService.Hash(Path.Combine(cache,"packages/mods/Gatekeeper.CustomMods.dll")),"Real payload installation changed the gameplay plugin bytes.");
    Check(File.Exists(Path.Combine(f.Game,"dotnet/coreclr.dll")) && new FileInfo(Path.Combine(f.Game,"dotnet/coreclr.dll")).Length>1_000_000,"The game loader's own runtime was not included.");
    Check(File.Exists(Path.Combine(f.Game,"BepInEx/unity-libs/2022.3.62.zip")) && File.ReadAllText(Path.Combine(f.Game,"BepInEx/config/BepInEx.cfg")).Contains("UnityBaseLibrariesSource = 2022.3.62.zip"),"First setup still depends on downloading the Unity support libraries.");
    actual.SaveProfile(new BalanceProfile { Metalheads25=false,QuietBayShop=true,PrismJackpot=false });
    actual.UpdateCustomMods();
    var config=File.ReadAllText(Path.Combine(f.Game,"BepInEx/config/"+GameService.CustomPluginId+".cfg"));
    Check(config.Contains("[Metalheads]\nEnabled = false") && config.Contains("[QuietBayShop]\nEnabled = true") && config.Contains("[PrismJackpot]\nEnabled = false"),"Real packaged mods ignored independent saved switches.");
    actual.SetLoader(true); Check(actual.LoaderEnabled,"The real packaged proxy could not be enabled.");
    actual.SetLoader(false); Check(!actual.LoaderEnabled,"The real packaged proxy could not be disabled.");
    Check(actual.Uninstall().Count==0 && !actual.Installed,"The real package could not be removed using its own receipt.");
    Check(File.ReadAllText(Path.Combine(f.Saves,"Saves/progress"))=="save-data","Real package setup/removal changed synthetic saved progress.");
    actual.InstallExpansion();
    Check(actual.LoaderEnabled && actual.ExpansionState().Stage == ExpansionStage.Ready, "The real single-file installation kit did not enable the complete expansion.");
    Check(actual.Profile().Forge && actual.Profile().CannonWheel && actual.Profile().QuietBayShop && actual.Profile().PrismJackpot && !actual.Profile().Metalheads25, "The real expansion package retained selectable or disabled content.");
    Check(actual.Uninstall().Count == 0 && File.ReadAllText(Path.Combine(f.Saves,"Saves/progress")) == "save-data", "The real complete kit did not preserve saves through install/removal.");
}
LinuxInstallerChecks.Run(Check, Fails);
Console.WriteLine($"PASS: {passed} checks (portable packages/discovery, Linux/Proton installation, manager safety, native-price markup, co-op owner payments, once-only delivery/refunds, fixed bets, audio timing and bounded transport).");

sealed class Fixture : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GatekeeperModManagerTests-" + Guid.NewGuid().ToString("N")));
    public string Game => Path.Combine(root, "game");
    public string Manager => Path.Combine(root, "manager");
    public string Saves => Path.Combine(root, "saves");
    public bool Running;
    public GameService Service { get; }
    public Fixture()
    {
        Directory.CreateDirectory(Game); Directory.CreateDirectory(Manager); Directory.CreateDirectory(Path.Combine(Saves, "Saves"));
        var exe = new byte[256]; BitConverter.GetBytes((ushort)0x5a4d).CopyTo(exe, 0); BitConverter.GetBytes(128).CopyTo(exe, 60);
        BitConverter.GetBytes(0x4550).CopyTo(exe, 128); BitConverter.GetBytes((ushort)0x8664).CopyTo(exe, 132); File.WriteAllBytes(Path.Combine(Game, "Gatekeeper.exe"), exe);
        File.WriteAllText(Path.Combine(Game, "GameAssembly.dll"), "original native game");
        var meta = Path.Combine(Game, GameService.MetadataFile); Directory.CreateDirectory(Path.GetDirectoryName(meta)!); File.WriteAllBytes(meta, BitConverter.GetBytes(0xfab11bafU));
        var loader = Path.Combine(Manager, "packages/loader"); Directory.CreateDirectory(Path.Combine(loader, "BepInEx/core"));
        File.WriteAllText(Path.Combine(loader, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"), "loader");
        File.WriteAllText(Path.Combine(loader, "winhttp.dll"), "proxy"); File.WriteAllText(Path.Combine(loader, "doorstop_config.ini"), "enabled=true");
        Directory.CreateDirectory(Path.Combine(Manager, "packages/mods")); File.WriteAllText(Path.Combine(Manager, "packages/mods/Gatekeeper.SubtleBalance.dll"), "plugin");
        File.WriteAllText(Path.Combine(Saves, "Saves/progress"), "save-data");
        Service = new GameService(Game, Manager, () => Running, Saves) { ExpectedAssembly = GameService.Hash(Path.Combine(Game, "GameAssembly.dll")), ExpectedMetadata = GameService.Hash(meta) };
    }
    public void Dispose()
    {
        var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "GatekeeperModManagerTests-";
        if (!root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
        Directory.Delete(root, true);
    }
}
