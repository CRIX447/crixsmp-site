using Mono.Cecil;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: RefCheck <plugin.dll> <reference-dir> [<reference-dir>...]");
    return 2;
}

var resolver = new DefaultAssemblyResolver();
foreach (string dir in resolver.GetSearchDirectories().ToArray())
    resolver.RemoveSearchDirectory(dir);
foreach (string dir in args.Skip(1))
    resolver.AddSearchDirectory(dir);
// The BCL (netstandard/mscorlib) comes from the Mono install, which is what Unity's runtime is based on.
foreach (string dir in new[] { "/usr/lib/mono/4.5", "/usr/lib/mono/4.5/Facades" })
    if (Directory.Exists(dir))
        resolver.AddSearchDirectory(dir);

var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });
int checkedTypes = 0, checkedMembers = 0;
var failures = new List<string>();
var perAssembly = new SortedDictionary<string, int>();

foreach (TypeReference type in module.GetTypeReferences())
{
    checkedTypes++;
    string scope = type.Scope?.Name ?? "?";
    perAssembly[scope] = perAssembly.GetValueOrDefault(scope) + 1;
    try
    {
        if (type.Resolve() == null)
            failures.Add($"type   {type.FullName} [{scope}]");
    }
    catch (Exception e)
    {
        failures.Add($"type   {type.FullName} [{scope}] ({e.GetType().Name})");
    }
}

foreach (MemberReference member in module.GetMemberReferences())
{
    checkedMembers++;
    try
    {
        IMemberDefinition resolved = member switch
        {
            MethodReference m => m.Resolve(),
            FieldReference f => f.Resolve(),
            _ => null,
        };
        if (resolved == null)
            failures.Add($"member {member.FullName} [{member.DeclaringType?.Scope?.Name}]");
    }
    catch (Exception e)
    {
        failures.Add($"member {member.FullName} ({e.GetType().Name}: {e.Message})");
    }
}

Console.WriteLine($"Checked {checkedTypes} type references and {checkedMembers} member references from {Path.GetFileName(args[0])}");
foreach (var pair in perAssembly)
    Console.WriteLine($"  {pair.Key}: {pair.Value} type refs");
foreach (AssemblyNameReference reference in module.AssemblyReferences)
{
    AssemblyDefinition def = null;
    try { def = resolver.Resolve(reference); } catch { }
    Console.WriteLine($"  reference {reference.Name} {reference.Version} -> {(def == null ? "NOT FOUND" : def.Name.Version + " " + def.MainModule.FileName)}");
}
if (failures.Count == 0)
{
    Console.WriteLine("RESULT: all references resolve");
    return 0;
}
foreach (string f in failures)
    Console.WriteLine("UNRESOLVED " + f);
Console.WriteLine($"RESULT: {failures.Count} unresolved reference(s)");
return 1;
