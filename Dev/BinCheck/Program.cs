using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Binary check: every member a mod DLL references in the game assembly must exist there with the same signature.
// usage: bincheck <game dll> <mod dll>...
class P : ISignatureTypeProvider<string, object>
{
    readonly MetadataReader r; public P(MetadataReader r) { this.r = r; }
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool req) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader rd, TypeDefinitionHandle h, byte k) => Name(rd, h);
    public string GetTypeFromReference(MetadataReader rd, TypeReferenceHandle h, byte k) => Name(rd, h);
    public string GetTypeFromSpecification(MetadataReader rd, object c, TypeSpecificationHandle h, byte k) => rd.GetTypeSpecification(h).DecodeSignature(this, c);
    public static string Name(MetadataReader rd, TypeDefinitionHandle h)
    {
        var t = rd.GetTypeDefinition(h);
        string n = rd.GetString(t.Name);
        if (t.GetDeclaringType() is { IsNil: false } d) return Name(rd, d) + "/" + n;
        string ns = rd.GetString(t.Namespace); return ns == "" ? n : ns + "." + n;
    }
    public static string Name(MetadataReader rd, TypeReferenceHandle h)
    {
        var t = rd.GetTypeReference(h);
        string n = rd.GetString(t.Name);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference) return Name(rd, (TypeReferenceHandle)t.ResolutionScope) + "/" + n;
        string ns = rd.GetString(t.Namespace); return ns == "" ? n : ns + "." + n;
    }
    public static string Scope(MetadataReader rd, TypeReferenceHandle h)
    {
        var t = rd.GetTypeReference(h);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference) return Scope(rd, (TypeReferenceHandle)t.ResolutionScope);
        if (t.ResolutionScope.Kind == HandleKind.AssemblyReference) return rd.GetString(rd.GetAssemblyReference((AssemblyReferenceHandle)t.ResolutionScope).Name);
        return "";
    }
}
static class Program
{
    static int Main(string[] a)
    {
        using var gpe = new PEReader(File.OpenRead(a[0]));
        var g = gpe.GetMetadataReader(); var gp = new P(g);
        string gameAsm = g.GetString(g.GetAssemblyDefinition().Name);
        var members = new HashSet<string>(); var types = new Dictionary<string, TypeDefinition>(); var bases = new Dictionary<string, string>();
        foreach (var th in g.TypeDefinitions)
        {
            var t = g.GetTypeDefinition(th); string tn = P.Name(g, th); types[tn] = t;
            if (!t.BaseType.IsNil)
                bases[tn] = t.BaseType.Kind switch { HandleKind.TypeDefinition => P.Name(g, (TypeDefinitionHandle)t.BaseType), HandleKind.TypeReference => P.Name(g, (TypeReferenceHandle)t.BaseType),
                    HandleKind.TypeSpecification => Strip(g.GetTypeSpecification((TypeSpecificationHandle)t.BaseType).DecodeSignature(gp, null)), _ => "" };
            foreach (var mh in t.GetMethods()) { var m = g.GetMethodDefinition(mh); var s = m.DecodeSignature(gp, null);
                members.Add(tn + "::" + g.GetString(m.Name) + "(" + string.Join(",", s.ParameterTypes) + ")" + s.ReturnType); }
            foreach (var fh in t.GetFields()) { var f = g.GetFieldDefinition(fh); members.Add(tn + "::" + g.GetString(f.Name) + ":" + f.DecodeSignature(gp, null)); }
        }
        int total = 0, bad = 0;
        foreach (string dll in a.Skip(1))
        {
            using var pe = new PEReader(File.OpenRead(dll));
            var r = pe.GetMetadataReader(); var p = new P(r);
            var missing = new List<string>(); int n = 0;
            foreach (var trh in r.TypeReferences)
                if (P.Scope(r, trh) == gameAsm && !types.ContainsKey(P.Name(r, trh))) missing.Add("TYPE " + P.Name(r, trh));
            foreach (var mrh in r.MemberReferences)
            {
                var mr = r.GetMemberReference(mrh); string owner;
                if (mr.Parent.Kind == HandleKind.TypeReference) { var h = (TypeReferenceHandle)mr.Parent; if (P.Scope(r, h) != gameAsm) continue; owner = P.Name(r, h); }
                else if (mr.Parent.Kind == HandleKind.TypeSpecification)
                {
                    var spec = r.GetTypeSpecification((TypeSpecificationHandle)mr.Parent);
                    string full = spec.DecodeSignature(p, null); owner = Strip(full);
                    if (!types.ContainsKey(owner)) continue;   // a generic of another assembly
                }
                else continue;
                n++;
                string name = r.GetString(mr.Name), key;
                if (mr.GetKind() == MemberReferenceKind.Method) { var s = mr.DecodeMethodSignature(p, null); key = "::" + name + "(" + string.Join(",", s.ParameterTypes) + ")" + s.ReturnType; }
                else key = "::" + name + ":" + mr.DecodeFieldSignature(p, null);
                bool found = false;
                for (string t = owner; t != null && !found; t = bases.GetValueOrDefault(t)) found = members.Contains(t + key);
                if (!found) missing.Add(owner + key);
            }
            total += n; bad += missing.Count;
            Console.WriteLine($"{Path.GetFileName(dll),-26} {n,4} references to the game; missing: {missing.Count}");
            foreach (string m in missing.Distinct()) Console.WriteLine("    " + m);
        }
        Console.WriteLine($"total {total}, missing {bad}");
        return bad == 0 ? 0 : 1;
    }
    static string Strip(string s) { int i = s.IndexOf('<'); return i < 0 ? s : s[..i]; }
}
