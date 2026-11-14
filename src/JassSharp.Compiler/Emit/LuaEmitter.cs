using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class LuaEmitter
{
    public string Emit(NsUnit unit, IEnumerable<NsUnit>? imports = null, bool headless = false) =>
        new Codegen(lua: true, unit.Classes.Select(c => c.Name), headless).Emit(unit, imports);
}
