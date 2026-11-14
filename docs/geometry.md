# 2D geometry standard library

The compiler embeds `standard-library/Geometry.w3js`. Import both the vector and geometry namespaces:

```csharp
namespace Gameplay
{
    using Math;
    using Geometry;

    public void OnInit()
    {
        Vector2 position = new Vector2(100.0, 200.0);
        Circle attackRange = new Circle(position, 350.0);
        Rect2 arena = new Rect2(0.0, 0.0, 2048.0, 2048.0);

        if (attackRange.Contains(new Vector2(120.0, 220.0))
            && arena.Contains(position))
        {
            // The point is inside both gameplay regions.
        }
    }
}
```

## Circle

`Circle(Vector2 center, float radius)` stores a center and a radius. The constructor normalizes a negative radius to its absolute value. Since fields are public and mutable, query methods also use the absolute value of `Radius`.

- `Contains(point)` — boundary-inclusive point test.
- `ClosestPoint(point)` — returns the input point when it is inside/on the circle; otherwise returns its radial projection onto the circumference. At the center, returns the center.
- `Area()` and `Circumference()`.
- `Intersects(other)` — boundary-inclusive circle/circle intersection.

## Rect2

`Rect2(x, y, width, height)` is an axis-aligned rectangle. Its fields are not rewritten; every query normalizes the edges, so negative width and height are supported.

- `Left()`, `Right()`, `Bottom()`, `Top()`.
- `Center()`, `Area()`, `Perimeter()`.
- `Contains(point)` — boundary-inclusive.
- `ClosestPoint(point)` — clamps each coordinate to the rectangle.
- `Intersects(other)` and `IntersectsCircle(circle)` — boundary-inclusive.

## LineSegment2

`LineSegment2(a, b)` represents the finite segment from A to B.

- `Length()`.
- `ClosestPoint(point)` — projects onto the segment and clamps the parameter to [0, 1].
- `DistanceToPoint(point)`.
- `ContainsPoint(point)` — uses a fixed distance tolerance of 0.0001 game-coordinate units. A zero-length segment is treated as a point.

All types are mutable reference types. Geometry is two-dimensional and uses the same coordinate units as Warcraft III gameplay coordinates. These helpers do not call Warcraft III natives and are available in both `wc3` and `headless` translation.
