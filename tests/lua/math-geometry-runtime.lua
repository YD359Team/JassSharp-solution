local generatedPath = assert(arg[1], "generated Lua path is required")
dofile(generatedPath)

local function near(actual, expected, epsilon, message)
    assert(math.abs(actual - expected) <= (epsilon or 0.00001),
        (message or "numeric result mismatch") .. ": expected " .. expected .. ", got " .. actual)
end

near(MathGeometryApiSmoke.Round(2.5), 2, 0.00001, "banker's rounding for even midpoint")
near(MathGeometryApiSmoke.Round(3.5), 4, 0.00001, "banker's rounding for odd midpoint")
near(MathGeometryApiSmoke.Round(-2.5), -2, 0.00001, "negative banker's rounding")
near(MathGeometryApiSmoke.Repeat(-1, 360), 359, 0.00001, "negative Repeat")
near(MathGeometryApiSmoke.Repeat(721, 360), 1, 0.00001, "positive Repeat")
near(MathGeometryApiSmoke.Repeat(10, 0), 0, 0.00001, "zero-length Repeat")
near(MathGeometryApiSmoke.InverseLerp(5, 5, 5), 0, 0.00001, "degenerate InverseLerp")
near(MathGeometryApiSmoke.InverseLerp(0, 10, 20), 1, 0.00001, "clamped InverseLerp")
near(MathGeometryApiSmoke.MoveTowards(0, 10, 3), 3, 0.00001, "MoveTowards step")
near(MathGeometryApiSmoke.MoveTowards(0, 10, 20), 10, 0.00001, "MoveTowards target clamp")
near(MathGeometryApiSmoke.VectorLength(3, 4), 5, 0.00001, "Vector2 length")
near(MathGeometryApiSmoke.Vector3CrossZ(1, 0, 0, 1), 1, 0.00001, "Vector3 cross product")
near(MathGeometryApiSmoke.RectArea(4, 5, -3, -2), 6, 0.00001, "negative rectangle dimensions")
assert(MathGeometryApiSmoke.RectContains(4, 5, -3, -2, 1, 3),
    "Rect2 must normalize negative dimensions")
assert(MathGeometryApiSmoke.CircleIntersectsRect(3, 0, 1, 0, 0, 2, 2),
    "circle touching rectangle boundary must intersect")
near(MathGeometryApiSmoke.SegmentDistance(0, 0, 2, 0, 3, 0), 1, 0.00001,
    "segment closest point must clamp beyond endpoint")

assert(MathGeometryApiSmoke.VerifyNullGameplayWrappers(),
    "gameplay wrappers must not call natives with null owners or missing abilities")

assert(MathGeometryApiSmoke.VerifyNullFrameWrappers(),
    "all frame wrappers must treat null native handles as disposed")

print("Math, vectors, geometry, and frame runtime checks passed.")
