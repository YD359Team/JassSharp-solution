local generatedPath = assert(arg[1], "generated Lua path is required")
dofile(generatedPath)

-- HookBase registers first (+10), then HookRoot (*2). The generated chain
-- must preserve dependency/declaration order at runtime, not just in source.
assert(__jasssharp_hooks_applied == true, "cross-module hook finalizer did not run")
assert(Compute(1) == 14, "cross-module hooks executed in the wrong order")

print("Cross-module hook runtime checks passed.")
