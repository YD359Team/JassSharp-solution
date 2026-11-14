# Operator overloading and standard Math library

The compiler embeds the JASS# standard library automatically. Import its namespace explicitly:

~~~csharp
namespace Gameplay
{
    using Math;

    public void OnInit()
    {
        Vector2 position = new Vector2(10.0, 20.0);
        Vector2 velocity = new Vector2(2.0, 0.0);
        Vector2 nextPosition = position + velocity;
        float distance = position.Distance(nextPosition);
        float clamped = Math.Clamp(distance, 0.0, 100.0);
    }
}
~~~

You do not need to copy standard-library files into the map project. Files in `standard-library` are embedded into the compiler and loaded as regular JASS# source units.

## Math

Static methods on `Math.Math` (imported with `using Math;`):

| Method | Description |
|---|---|
| `Pi()` | Returns π |
| `DegToRad(degrees)` / `RadToDeg(radians)` | Converts angular units |
| `Min(a, b)` / `Max(a, b)` | Returns the smaller/larger value |
| `Clamp(value, min, max)` | Clamps a value to the inclusive range; requires min ≤ max |
| `Lerp(a, b, t)` | Linear interpolation; t is not clamped |
| `InverseLerp(a, b, value)` | Returns a clamped interpolation factor; returns 0 when a == b |
| `SmoothStep(a, b, t)` | Smooth Hermite interpolation with t clamped to [0, 1] |
| `MoveTowards(current, target, maxDelta)` | Moves toward target by at most maxDelta; non-positive maxDelta leaves current unchanged |
| `Repeat(value, length)` | Repeats value in [0, length); non-positive length returns 0 |
| `LerpAngle(a, b, t)` | Interpolates degrees using the shortest angular path; t is not clamped |
| `Round(value)` | Midpoint-to-even rounding |
| `Floor(value)` / `Ceil(value)` | Rounds down/up |
| `Pow(value, power)` / `Sqrt(value)` | Power and square root |
| `Abs(value)` | Absolute value |
| `Sin(value)` / `Cos(value)` / `Tan(value)` | Trigonometric functions; arguments are radians |

The initial API uses float arguments and returns float, including the rounding functions. Static methods are called through the type name (for example, `Math.Clamp(...)`); calling a static method through an instance or an instance method through a type name is a semantic error.

## Vectors

`Vector2` and `Vector3` are declared in the same `Math` namespace, in the separate `standard-library/Vectors.w3js` module. Both are mutable reference types with public coordinate fields.

Methods:

- `Length()`
- `Normalized()` — returns the zero vector when the input length is zero
- `Dot(other)`
- `Distance(other)`
- `Lerp(other, t)`
- `Vector3.Cross(other)`

Supported operators include addition, subtraction, unary negation, scalar multiplication, division by a scalar, and value equality/inequality. Scalar multiplication is supported in both operand orders. Division by zero follows the generated Lua numeric semantics; callers should guard a zero divisor.

For the 2D gameplay primitives built on top of these vectors, see [the geometry standard library](geometry.md). It provides circles, axis-aligned rectangles, and finite line segments.

## User-defined operators

Declare operators as public static class methods using C#-style syntax:

~~~csharp
public class Distance
{
    public float Value;

    public Distance(float value)
    {
        this.Value = value;
    }

    public static Distance operator +(Distance left, Distance right)
    {
        return new Distance(left.Value + right.Value);
    }
}
~~~

The current Lua backend lowers overloaded operators to generated typed helper functions and installs Lua metamethod adapters for arithmetic, equality, and supported ordering operations. This keeps generated JASS# expressions deterministic while allowing the same objects to participate in native Lua operators.

Currently supported declarations are binary +, -, *, /, %, ==, !=, <, >, <=, >=, plus unary -. Operators must be static, accept the containing type as at least one parameter, and comparison operators must return bool. Equality and relational operators must be declared in C#-style pairs.

Logical && and || are intentionally not overloadable because their short-circuit semantics cannot be represented by Lua metamethods.

## Smoke fixture

`tests/fixtures/MathOperatorsSmoke.w3js` exercises import resolution, static calls, vector operations, and operator overloads. Run:

~~~powershell
dotnet run --project src/JassSharp.Compiler -- check tests/fixtures/MathOperatorsSmoke.w3js
dotnet run --project src/JassSharp.Compiler -- build tests/fixtures/MathOperatorsSmoke.w3js

./tests/run-smoke.ps1
~~~
