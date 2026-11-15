using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class LuaEmitter
{
    public string Emit(
        NsUnit unit,
        IEnumerable<NsUnit>? imports = null,
        bool headless = false,
        bool finalizeHooks = true) =>
        new Codegen(headless: headless).Emit(unit, imports, finalizeHooks);

    public static string EmitHookFinalizer() =>
        "if __jasssharp_apply_hooks then __jasssharp_apply_hooks() end\n";
}
