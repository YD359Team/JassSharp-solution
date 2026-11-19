local generatedPath = assert(arg[1], "generated Lua path is required")
dofile(generatedPath)

assert(Strings.Length("AéBC") == 4,
    "Strings.Length must count Unicode code points")
assert(Strings.Substring("AéBC", 1, 2) == "éB",
    "Substring must use Unicode code-point offsets")
assert(Strings.IndexOf("AéBC", "é") == 1,
    "IndexOf must return a Unicode code-point index")
assert(Strings.LastIndexOf("one two one", "one") == 8,
    "ASCII LastIndexOf returned the wrong zero-based index")
assert(Strings.LastIndexOf("éxé", "é") == 2,
    "Unicode LastIndexOf must return a code-point index")
assert(Strings.LastIndexOf("éé", "é") == 1,
    "Unicode LastIndexOf must handle adjacent multibyte matches")
assert(Strings.LastIndexOf("aaa", "aa") == 1,
    "LastIndexOf must find overlapping matches")
assert(Strings.LastIndexOf("abc", "z") == -1,
    "LastIndexOf must return -1 when no match exists")
assert(Strings.LastIndexOf("abc", "") == 3,
    "LastIndexOf with an empty search value must return the string length")

print("Strings Unicode runtime checks passed.")
