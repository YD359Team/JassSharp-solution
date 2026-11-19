# Expression-bodied methods

JASS# supports C#-style expression bodies for class methods, overloaded operators,
and namespace-level functions:

```csharp
namespace Arithmetic
{
    public class Calculator
    {
        public int Add(int left, int right) => left + right;
        public static int Square(int value) => value * value;
        public void Log(int value) => Print(value);
    }

    public int Sum(int left, int right) => left + right;
}
```

A non-void expression body is equivalent to a single `return` statement.
A `void` expression body is equivalent to a single expression statement.
The expression is checked by the same semantic analyzer as a block-bodied method.

The terminating semicolon is required. Constructors and interface declarations
continue to use their existing block/declaration syntax.
