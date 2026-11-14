# Lua interop

JASS# uses Lua as its only code-generation backend. Most Warcraft III logic should use the typed JASS# language and the standard library. For cases where direct access to Lua is necessary, a method body may contain a raw Lua statement:

```csharp
void Debug()
{
    Lua.Raw("""
        local value = GetRandomInt(1, 100)
        print(value)
    """);
}
```

The argument must be one ordinary or triple-quoted string literal. Triple-quoted strings are recommended for multiline blocks. The compiler inserts the literal contents at the statement location and applies the surrounding indentation.

The block is not parsed as JASS#, is not type-checked, and is not rewritten by the compiler. It can reference Lua globals, locals and APIs directly. Consequently, it is an explicit escape hatch: syntax errors and runtime errors inside the block are the author's responsibility. It is not a runtime `eval`/ `exec` facility and does not evaluate a string dynamically.

`Lua.Raw` is a standalone statement, not an expression. It cannot be assigned to a variable or used as an argument. It is only available in the Lua backend; JASS# no longer has a vJASS/JASS backend or target-specific conditional compilation.
