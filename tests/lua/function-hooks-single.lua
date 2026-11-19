local generatedPath = assert(arg[1], "generated Lua path is required")

-- Warcraft III natives are supplied by the game runtime. Stub the one native
-- used by this fixture so the generated hook chain can execute under stock Lua.
GetUnitAbilityLevel = function(_, abilityId)
    return abilityId + 100
end

dofile(generatedPath)

assert(__jasssharp_hooks_applied == true, "hook finalizer did not run")
assert(Compute(2) == 16, "multiple hooks did not execute in declaration order")
assert(Compute(-1) == 0, "an early return in the outer hook was not preserved")
assert(ReplaceValue(1) == 42, "a hook that skips original() did not replace the result")
assert(GetUnitAbilityLevel({}, 1) == 101, "native hook did not delegate to the original native")

-- Finalization must be idempotent and late registrations must be rejected.
__jasssharp_apply_hooks()
assert(Compute(2) == 16, "applying hooks twice changed the result")

local registered, registrationError = pcall(function()
    __jasssharp_register_hook("Compute", function(original)
        return original
    end)
end)
assert(not registered, "a hook was accepted after finalization")
assert(tostring(registrationError):match("after finalization"), "late-hook error was not descriptive")

print("Single-module hook runtime checks passed.")
