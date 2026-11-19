local generated = assert(arg[1], "expected generated Lua file path")
dofile(generated)

local alphaClass = __jasssharp_type_5_Alpha_6_Player
local betaClass = __jasssharp_type_4_Beta_6_Player
local alphaBase = __jasssharp_type_5_Alpha_10_BaseEntity
local betaBase = __jasssharp_type_4_Beta_10_BaseEntity

assert(type(alphaClass) == "table", "Alpha.Player alias was not emitted")
assert(type(betaClass) == "table", "Beta.Player alias was not emitted")
assert(type(alphaBase) == "table", "Alpha.BaseEntity alias was not emitted")
assert(type(betaBase) == "table", "Beta.BaseEntity alias was not emitted")
assert(alphaClass ~= betaClass, "same-named classes from different namespaces share a Lua table")
assert(alphaBase ~= betaBase, "same-named base classes from different namespaces share a Lua table")
assert(getmetatable(alphaClass).__index == alphaBase, "Alpha.Player inherited from the wrong BaseEntity")
assert(getmetatable(betaClass).__index == betaBase, "Beta.Player inherited from the wrong BaseEntity")

local alpha = alphaClass.new()
local beta = betaClass.new()

assert(getmetatable(alpha) == alphaClass, "Alpha.Player.new created an instance of another class")
assert(getmetatable(beta) == betaClass, "Beta.Player.new created an instance of another class")
assert(alpha:GetId() == 101, "Alpha.Player inherited fields or methods from the wrong base class")
assert(beta:GetId() == 202, "Beta.Player inherited fields or methods from the wrong base class")
assert(VerifyDistinctTypes() == 333, "qualified static methods resolved to the wrong namespace class")

print("Namespace class and inheritance collision runtime checks passed.")
