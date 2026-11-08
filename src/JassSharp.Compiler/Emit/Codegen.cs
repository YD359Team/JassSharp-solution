using JassSharp.Runtime;
using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class Codegen
{
    private readonly bool _lua;
    private readonly HashSet<string> _classes;
    private readonly List<string> _lines = [];
    private int _indent;
    private Dictionary<string, string> _locals = new(StringComparer.Ordinal);
    private string? _className;
    private readonly Dictionary<string, FnDecl> _inlineMethods = new(StringComparer.Ordinal);

    public Codegen(bool lua, IEnumerable<string> classes)
    {
        _lua = lua;
        _classes = new HashSet<string>(classes, StringComparer.Ordinal);
    }

    public string Emit(NsUnit unit)
    {
        // collect inline methods
        foreach (var cls in unit.Classes)
            foreach (var m in cls.Methods)
                if (m.Attributes?.Contains("Inline") == true)
                    _inlineMethods[cls.Name + "." + m.Name] = m;
        foreach (var fn in unit.Functions)
            if (fn.Attributes?.Contains("Inline") == true)
                _inlineMethods[fn.Name] = fn;

        var hasInit = unit.Functions.Any(f => f.Name == "OnInit");
        if (_lua)
        {
            Line($"-- {unit.Name}  (JASS# \u2192 Lua)");
            if (unit.Usings.Count > 0)
                Line("-- usings: " + string.Join(", ", unit.Usings));
            Line("if not BJDebugMsg then function BJDebugMsg(s) print(s) end end");
            Line("");
            foreach (var f in unit.Fields)
                Line(f.IsArray ? $"{f.Name} = {{}}" : $"{f.Name} = nil");
            if (unit.Fields.Count > 0) Line("");
            foreach (var c in unit.Consts)
                Line($"local {c.Name} = {EmitExpr(c.Value)}");
            if (unit.Consts.Count > 0) Line("");
            foreach (var cls in unit.Classes) EmitClassLua(cls);
            foreach (var fn in unit.Functions) EmitFnLua(fn);
            EmitLuaEntry(hasInit);
        }
        else
        {
            Line($"library {unit.Name}{(hasInit ? " initializer OnInit" : "")}");
            if (unit.Consts.Count > 0 || unit.Fields.Count > 0)
            {
                Line("globals");
                _indent = 1;
                foreach (var c in unit.Consts)
                    Line($"constant {MapType(c.Type)} {c.Name} = {EmitExpr(c.Value)}");
                foreach (var f in unit.Fields)
                    Line(f.IsArray ? $"{MapType(f.Type)} array {f.Name}" : $"{MapType(f.Type)} {f.Name}");
                _indent = 0;
                Line("endglobals");
                Line("");
            }
            foreach (var cls in unit.Classes) EmitClassVj(cls);
            foreach (var fn in unit.Functions) EmitFnVj(fn, method: false);
            Line("endlibrary");
        }
        return string.Join("\n", _lines) + "\n";
    }

    // ... (rest of the methods identical to current Codegen.cs, including EmitCall with Print and ToString)
    // Full file is the current one plus the _inlineMethods field and collection logic above.
    // Inline call handling to be expanded in EmitCall.
    private void Line(string s) => _lines.Add(new string(' ', _indent * 4) + s);
}
