# Generic collections

JASS# provides generic collection types on the Lua backend:

- `List<T>`
- `Queue<T>`
- `Stack<T>`
- `Dictionary<TKey, TValue>`

Type arguments are preserved by the parser and checked by the semantic analyzer. A value of the wrong element/key/value type is rejected at compile time when its type is known.

## List<T>

```csharp
List<int> values = new List<int>();
values.Add(10);
values.Add(20);

int first = values[0];
int last = values[^1];
int count = values.Count;
int index = values.IndexOf(20);
bool removed = values.Remove(10);
values.Clear();

foreach (var value in values)
{
    Print(value);
}
```

List indices are zero-based in source code. `^1` addresses the last element. `Count` and the existing `Length` property return the number of elements.

## Queue<T>

```csharp
Queue<int> queue = new Queue<int>();
queue.Enqueue(10);
queue.Enqueue(20);

int next = queue.Peek();
int value = queue.Dequeue();
int count = queue.Count;
```

The queue is FIFO. `Peek` returns the head without removing it. `Dequeue` removes and returns the head. Both throw a runtime error when the queue is empty. `foreach` visits elements from head to tail.

## Stack<T>

```csharp
Stack<string> stack = new Stack<string>();
stack.Push("first");
stack.Push("second");

string next = stack.Peek();
string value = stack.Pop();
int count = stack.Count;
```

The stack is LIFO. `Peek` returns the top without removing it. `Pop` removes and returns the top. Both throw a runtime error when the stack is empty. `foreach` visits elements from top to bottom, matching .NET Stack enumeration order.

## Dictionary<TKey, TValue>

```csharp
Dictionary<string, int> scores = new Dictionary<string, int>();
scores.Add("Player1", 100);
scores["Player1"] = 150;

bool added = scores.TryAdd("Player2", 200);
bool contains = scores.ContainsKey("Player1");
int score = scores["Player1"];
bool removed = scores.Remove("Player2");
bool tryRemoved = scores.TryRemove("Player1");

foreach (var pair in scores)
{
    string key = pair.Key;
    int value = pair.Value;
}
```

- `Add(key, value)` throws if the key already exists.
- `TryAdd(key, value)` returns `false` if the key already exists.
- `Remove(key)` and `TryRemove(key)` return whether an entry was removed.
- `ContainsKey(key)` checks for a key.
- The indexer reads or replaces a value. Reading a missing key throws a runtime error; assigning a missing key creates an entry.
- `Count` returns the number of entries.
- `foreach` visits key/value pairs. Dictionary iteration order is unspecified.

JASS# currently has no `out` parameters, so `TryRemove` returns only a boolean; read the value through the indexer before removing it when it is needed.

Lua tables cannot represent `nil` keys or values directly. The runtime stores collection null values through an internal sentinel, so List, Queue, Stack and Dictionary values can be `null`; Dictionary keys cannot. Keys must be valid Lua table keys (for example, `null` is not a valid key). As with .NET dictionaries, mutating the collection while enumerating it is not supported.

## Backend and runtime notes

These collection types are compiler-provided runtime types and currently target Lua. They are not Warcraft III native handle types. Queue uses a head/tail indexed buffer, Stack uses a dense array, and Dictionary uses a Lua table plus an explicit count. Empty Queue/Stack access throws a Lua runtime error.

Collection methods and properties are type-checked against their generic arguments. `foreach` infers element types for List, Queue and Stack, and infers `KeyValuePair<TKey, TValue>` for Dictionary.
