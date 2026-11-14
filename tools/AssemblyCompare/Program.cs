using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)=>types.SelectMany(t=>new[]{t}.Concat(Types(t.NestedTypes)));
string Operand(object operand)=>operand switch {
 null=>"", Instruction i=>"IL_"+i.Offset.ToString("x4"), Instruction[] items=>string.Join(",",items.Select(i=>"IL_"+i.Offset.ToString("x4"))),
 MethodReference m=>m.FullName, FieldReference f=>f.FullName, TypeReference t=>t.FullName,
 VariableDefinition v=>v.Index+":"+v.VariableType.FullName, ParameterDefinition p=>p.Index+":"+p.ParameterType.FullName,
 IFormattable f=>f.ToString(null,CultureInfo.InvariantCulture), _=>operand.ToString()??""
};
Dictionary<string,string> Code(AssemblyDefinition a)=>Types(a.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToDictionary(m=>m.FullName,m=>
 string.Join("|",m.Body.Variables.Select(v=>v.VariableType.FullName))+"\n"+
 string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Name+" "+Operand(i.Operand)))+"\n"+
 string.Join("|",m.Body.ExceptionHandlers.Select(h=>$"{h.HandlerType}:{h.TryStart?.Offset}:{h.TryEnd?.Offset}:{h.HandlerStart?.Offset}:{h.HandlerEnd?.Offset}:{h.FilterStart?.Offset}:{h.CatchType?.FullName}")));
using var left=AssemblyDefinition.ReadAssembly(args[0]);using var right=AssemblyDefinition.ReadAssembly(args[1]);
var lc=Code(left);var rc=Code(right);var changed=lc.Keys.Union(rc.Keys).Where(k=>!lc.TryGetValue(k,out var l)||!rc.TryGetValue(k,out var r)||l!=r).ToArray();
var lr=left.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
var rr=right.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
var assets=lr.Keys.Union(rr.Keys).Where(k=>!lr.TryGetValue(k,out var l)||!rr.TryGetValue(k,out var r)||l!=r).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new{assembly=left.Name.Name,version=left.Name.Version.ToString(),methods=lc.Count,normalizedMethodBodiesMatch=changed.Length==0,changedMethods=changed,resources=lr.Count,resourceDifferences=assets},new JsonSerializerOptions{WriteIndented=true}));
if(changed.Length>0 || assets.Any(n=>!n.EndsWith("Payload.zip")))Environment.Exit(1);
