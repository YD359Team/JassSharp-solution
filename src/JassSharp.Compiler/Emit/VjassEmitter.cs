using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class VjassEmitter
{
    public string Emit(NsUnit unit) =>
        new Codegen(lua: false, unit.Classes.Select(c => c.Name)).Emit(unit);
}
