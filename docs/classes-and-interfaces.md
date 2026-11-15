# Classes and interfaces

JASS# supports single class inheritance and multiple interface inheritance/implementation.

## Class inheritance

Use C#-style base lists. The base class, if present, must be the first entry:

```csharp
class Entity
{
    public int Id;

    Entity()
    {
        Id = 10;
    }

    public int GetId()
    {
        return Id;
    }
}

class UnitEntity : Entity
{
    public void Update()
    {
        Id = Id + 1;
    }
}
```

A class may inherit from one class. Base fields and instance/static methods are visible through derived types, and a derived instance is assignable to a variable or parameter of its base type.

Lua generation uses metatable inheritance. Each class has an `__index` table; the class table itself delegates missing members to its base class. Constructors are emitted as `Class.__init(self, ...)` and `Class.new(...)`. Base initialization is invoked before derived field initialization and constructor body execution.

For now, an inherited base class must have a parameterless constructor. The compiler reports a diagnostic when the base constructor requires arguments; constructor chaining with `base(args)` is not yet part of the syntax.

## Interfaces

Interfaces declare method signatures only:

```csharp
interface IEntity
{
    int GetId();
}

interface IUpdatable : IEntity
{
    void Update(float deltaTime);
}

class UnitEntity : IUpdatable
{
    public int GetId()
    {
        return 10;
    }

    public void Update(float deltaTime)
    {
        // Update state
    }
}
```

A class may implement multiple interfaces and may combine one base class with multiple interfaces:

```csharp
class UnitEntity : Entity, IUpdatable, IDisposableLike
{
    // Required methods may be implemented here or inherited from Entity.
}
```

The checker verifies:
- Every referenced base class/interface exists.
- A class has at most one base class, and it appears first in the base list.
- Class and interface inheritance graphs are acyclic.
- Every required interface method is implemented by the class or a base class.
- Implementations are instance methods with matching return and parameter types.
- Interface methods are callable through variables typed as the interface.
- Interface types cannot be instantiated with `new`.

Interfaces are compile-time contracts. They do not generate Lua tables or runtime interface objects. A variable typed as an interface contains the implementing class instance; calls are emitted as ordinary Lua instance calls.

## Current limitations

- No multiple class inheritance.
- No default interface method bodies, properties, events, generic interfaces or explicit interface implementation.
- No `abstract`, `virtual`, `override` or `sealed` modifiers yet.
- Base constructors must be parameterless; explicit `base(args)` chaining is not supported yet.
- Access modifiers are parsed for existing syntax compatibility but are not yet enforced as a complete C# accessibility model.

These are deliberate boundaries of the current implementation, not claims of full C# language compatibility.
