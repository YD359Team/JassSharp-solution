using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class LuaEmitter
{
    public string Emit(NsUnit unit, IEnumerable<NsUnit>? imports = null, bool headless = false) =>
        new Codegen(lua: true, classes: unit.Classes.Select(c => c.Name), headless: headless).Emit(unit, imports);
}
