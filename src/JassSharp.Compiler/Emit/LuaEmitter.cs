using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class LuaEmitter
{
    public string Emit(NsUnit unit, IEnumerable<NsUnit>? imports = null) =>
        new Codegen(lua: true, unit.Classes.Select(c => c.Name)).Emit(unit, imports);
}
