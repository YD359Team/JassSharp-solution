# Operator overloading and standard Math library

The compiler includes a built-in Math namespace. Import it explicitly:

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

The compiler resolves the import automatically. You do not need to copy standard-library files into the map project.

## Math

Static methods on Math:

- Min(float, float), Max(float, float)
- Clamp(float value, float min, float max)
- Lerp(float a, float b, float t)
- Round(float) — midpoint-to-even rounding
- Floor(float), Ceil(float)
- Pow(float, float), Sqrt(float)
- Abs(float), Sin(float), Cos(float), Tan(float)

The initial API uses float arguments and returns float, including the rounding functions.

## Vector2 and Vector3

Both types are mutable reference types with public X, Y and (for Vector3) Z fields.

Methods:

- Length()
- Normalized() — returns the zero vector when the input length is zero
- Dot(other)
- Distance(other)
- Lerp(other, t)
- Vector3.Cross(other)

Supported operators include addition, subtraction, unary negation, scalar multiplication, division by a scalar, and value equality/inequality. Scalar multiplication is supported in both operand orders.

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

tests/fixtures/MathOperatorsSmoke.w3js exercises import resolution, static calls, vector operations, and operator overloads. Run:

~~~powershell
dotnet run --project src/JassSharp.Compiler -- check tests/fixtures/MathOperatorsSmoke.w3js
dotnet run --project src/JassSharp.Compiler -- build tests/fixtures/MathOperatorsSmoke.w3js
~~~
