local generated = assert(arg[1], "expected generated Lua file path")
dofile(generated)

local alphaClass = __jasssharp_type_5_Alpha6_Player
local betaClass = __jasssharp_type_4_Beta6_Player

assert(type(alphaClass) == "table", "Alpha.Player alias was not emitted")
assert(type(betaClass) == "table", "Beta.Player alias was not emitted")
assert(alphaClass ~= betaClass, "same-named classes from different namespaces share a Lua table")

local alpha = alphaClass.new()
local beta = betaClass.new()

assert(getmetatable(alpha) == alphaClass, "Alpha.Player.new created an instance of another class")
assert(getmetatable(beta) == betaClass, "Beta.Player.new created an instance of another class")
assert(alpha:GetId() == 1, "Alpha.Player method dispatch resolved to the wrong class")
assert(beta:GetId() == 2, "Beta.Player method dispatch resolved to the wrong class")

print("Namespace class collision runtime checks passed.")
