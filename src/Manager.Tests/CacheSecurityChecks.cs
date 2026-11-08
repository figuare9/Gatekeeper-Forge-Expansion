using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Gatekeeper.ModManager;

internal static class CacheSecurityChecks
{
    public static void Run(Action<bool,string> check, Action<Action,string> fails)
    {
        static MemoryStream Bundle(Fixture f)
        {
            File.WriteAllText(Path.Combine(f.Manager,"packages/mods/Gatekeeper.CustomMods.dll"),"trusted gameplay");
            var stream=new MemoryStream();
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
            {
                var files=Directory.GetFiles(Path.Combine(f.Manager,"packages"),"*",SearchOption.AllDirectories).Select(path=>
                {
                    var relative=Path.GetRelativePath(f.Manager,path).Replace('\\','/');var bytes=File.ReadAllBytes(path);
                    using(var target=zip.CreateEntry(relative).Open())target.Write(bytes);
                    return new BundleFile(relative,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
                }).ToArray();
                using var manifest=zip.CreateEntry("bundle.json").Open();
                JsonSerializer.Serialize(manifest,new BundleManifest(ExpansionKit.ContentVersion,files));
            }
            stream.Position=0;return stream;
        }
        static GameService Service(Fixture f,VerifiedPackage package)=>new(f.Game,Path.Combine(f.Manager,"verified-state"),()=>false,f.Saves,package.Root,package)
            {ExpectedAssembly=f.Service.ExpectedAssembly,ExpectedMetadata=f.Service.ExpectedMetadata};
        using(var f=new Fixture())
        {
            using var bundle=Bundle(f);var cache=Path.Combine(f.Manager,"cache");
            var package=PortablePackage.Prepare(bundle,cache);var plugin=Path.Combine(cache,"packages/mods/Gatekeeper.CustomMods.dll");
            File.WriteAllText(plugin+".extracting","interrupted write");bundle.Position=0;
            package=PortablePackage.Prepare(bundle,cache);
            check(!File.Exists(plugin+".extracting"),"An interrupted extraction temp survived a valid cache hit.");
            var sentinel=Path.Combine(cache,"packages/loader/BepInEx/patchers/UnlistedAuditSentinel.dll");Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);File.WriteAllText(sentinel,"benign unlisted sentinel");
            bundle.Position=0;fails(()=>PortablePackage.Extract(bundle,cache),"Extraction accepted an unlisted cached patcher.");
            check(File.ReadAllText(sentinel)=="benign unlisted sentinel","Rejecting a cache destroyed the unexpected file.");
            bundle.Position=0;package=PortablePackage.Prepare(bundle,cache);
            var preserved=Directory.GetDirectories(f.Manager,"cache.preserved-*").Single();
            check(!File.Exists(sentinel) && File.ReadAllText(Path.Combine(preserved,Path.GetRelativePath(cache,sentinel)))=="benign unlisted sentinel","Cache recovery failed to isolate and preserve an unlisted component.");
            var service=Service(f,package);service.InstallExpansion();
            check(service.ExpansionState().Stage==ExpansionStage.Ready && !File.Exists(Path.Combine(f.Game,"BepInEx/patchers/UnlistedAuditSentinel.dll")),"The rebuilt cache installed an unexpected patcher.");
            service.Uninstall();File.WriteAllText(plugin,"tampered after preparation");
            fails(()=>service.InstallExpansion(),"A listed cached component changed after preparation was trusted.");
            check(!File.Exists(service.ReceiptPath) && !File.Exists(Path.Combine(f.Game,"winhttp.dll")),"Changed cache bytes mutated a clean game.");
            bundle.Position=0;package=PortablePackage.Prepare(bundle,cache);service=Service(f,package);
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);File.WriteAllText(sentinel,"late extra");fails(()=>service.InstallExpansion(),"A cache patcher added after preparation was accepted.");
            check(File.ReadAllText(sentinel)=="late extra","Late unexpected content was deleted.");
        }
        using(var f=new Fixture())
        {
            using var bundle=Bundle(f);var package=PortablePackage.Prepare(bundle,Path.Combine(f.Manager,"cache"));
            var service=Service(f,package);var plugin=Path.Combine(package.Root,"packages/mods/Gatekeeper.CustomMods.dll");
            fails(()=>service.InstallExpansion(new ChangeAfterValidation(()=>File.WriteAllText(plugin,"changed during install"))),"Installation recalculated trusted hashes after validating the cache.");
            check(!File.Exists(Path.Combine(f.Game,GameService.CustomPlugin)),"A component changed during installation was promoted from staging.");
            check(service.Receipt() is {Complete:false},"An integrity failure was reported as a completed install.");
        }
        using(var f=new Fixture())
        {
            File.WriteAllText(Path.Combine(f.Manager,"packages/mods/Gatekeeper.CustomMods.dll"),"gameplay");
            var old=Path.Combine(f.Manager,"packages/loader/dotnet/obsolete.dll");Directory.CreateDirectory(Path.GetDirectoryName(old)!);File.WriteAllText(old,"old runtime");
            f.Service.InstallExpansion();File.Delete(old);
            var extra=Path.Combine(f.Game,"dotnet/unowned.txt");File.WriteAllText(extra,"user-owned");
            f.Service.InstallExpansion();
            check(!File.Exists(Path.Combine(f.Game,"dotnet/obsolete.dll")) && File.ReadAllText(extra)=="user-owned","Runtime migration left obsolete owned files or removed unrelated files.");
            check(Directory.GetFiles(Path.Combine(f.Manager,"state/setup-backups"),"obsolete.dll",SearchOption.AllDirectories).Any(),"Runtime migration did not back up the retired file.");
            check(!f.Service.Receipt()!.Files.Any(p=>p.RelativePath=="dotnet/obsolete.dll"),"The completed receipt still claims a retired runtime file.");
        }
        using(var f=new Fixture())
        {
            File.WriteAllText(Path.Combine(f.Manager,"packages/mods/Gatekeeper.CustomMods.dll"),"gameplay");
            var old=Path.Combine(f.Manager,"packages/loader/dotnet/obsolete.dll");Directory.CreateDirectory(Path.GetDirectoryName(old)!);File.WriteAllText(old,"old runtime");
            f.Service.InstallExpansion();File.Delete(old);File.WriteAllText(Path.Combine(f.Game,"dotnet/obsolete.dll"),"external change");
            fails(()=>f.Service.InstallExpansion(),"Runtime migration removed an externally changed retired file.");
            check(File.ReadAllText(Path.Combine(f.Game,"dotnet/obsolete.dll"))=="external change","Runtime migration lost externally changed bytes.");
        }
        using(var f=new Fixture())
        {
            using var bundle=Bundle(f);var package=PortablePackage.Prepare(bundle,Path.Combine(f.Manager,"cache"));
            var plugin=Path.Combine(package.Root,"packages/mods/Gatekeeper.CustomMods.dll");
            File.WriteAllText(plugin,"damaged cache");File.WriteAllText(plugin+".extracting","interrupted extraction");
            bundle.Position=0;package=PortablePackage.Prepare(bundle,package.Root);
            check(File.ReadAllText(plugin)=="trusted gameplay" && !File.Exists(plugin+".extracting"),"Interrupted extraction did not replace damaged cached bytes safely.");
            var service=Service(f,package);service.Install();
            service.SetLoader(false);service.Uninstall();
            File.WriteAllText(plugin,"late change");fails(()=>service.Install(),"The legacy install path trusted changed cached bytes.");
        }
    }
    private sealed class ChangeAfterValidation(Action change):IProgress<InstallProgress>
    { private bool done;public void Report(InstallProgress p){if(!done && p.Percent==15){done=true;change();}} }
}
