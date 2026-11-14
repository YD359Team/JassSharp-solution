# Strings

JASS# supports ordinary string literals, raw multiline string literals and interpolation.

## Ordinary strings

Use double quotes. Escape sequences include `\n`, `\r`, `\t`, `\0`, `\"` and `\\`.

```csharp
string message = "First line\nSecond line";
string quoted = "He said: \"hello\"";
```

Single-quoted literals remain Warcraft rawcode literals; they are not character or string literals.

## Raw multiline strings

Triple quotes preserve the contents without processing backslash escapes:

```csharp
string help = """
    First line
    Second line
    Third line
    """;
```

When the opening delimiter is followed by a newline, the compiler removes that first newline and trims the indentation used before the closing delimiter from each nonblank line. The newline immediately before the closing delimiter is not included. Inline raw strings such as `"""text"""` are also supported.

## Interpolated strings

Prefix a string with `$` and place expressions inside braces:

```csharp
int x = 2;
int y = 3;
int z = 4;
string result = $"Product: {x * y * z}";
string status = $"Result={x + y}; enabled={true}";
```

Expressions are parsed by the normal JASS# expression parser and checked by the semantic analyzer. Arithmetic, variables, member access and function calls can be used inside interpolation. Values are converted to strings using the same conversions as `Print` and string concatenation.

Escape braces by doubling them:

```csharp
string text = $"{{literal}} and {{{x}}}";
```

This produces `{literal} and {2}`.

## Interpolated multiline strings

Combine `$` with triple quotes:

```csharp
string report = $"""
    Unit: {unitName}
    Health: {currentHealth}
    """;
```

Raw multiline interpolation uses the same indentation trimming as a non-interpolated raw string. Backslashes in its literal portions are preserved.

Alignment and format specifiers (for example `{value,10}` and `{value:N2}`) are not currently supported. Interpolation currently accepts a single expression per brace pair.

## Стандартный модуль `Strings`

Подключите модуль:

```csharp
using Strings;
```

Все операции доступны через статический класс `Strings`:

```csharp
int length = Strings.Length("Warcraft");
string part = Strings.Substring("Warcraft", 4, 4);
string replaced = Strings.Replace("a-b-a", "a", "x");
int first = Strings.IndexOf("one two one", "one");
int last = Strings.LastIndexOf("one two one", "one");
bool found = Strings.Contains("Warcraft III", "craft");
string clean = Strings.Trim("  text  ");
string leftClean = Strings.TrimStart("  text");
string rightClean = Strings.TrimEnd("text  ");
string lower = Strings.ToLower("HELLO");
string upper = Strings.ToUpper("hello");
string[] parts = Strings.Split("a,,b,", ",");
string joined = Strings.Join("|", parts);
```

Индексы `Substring`, `IndexOf` и `LastIndexOf` отсчитываются от нуля и измеряются в Unicode code points, а не в UTF-8 байтах; входные строки должны содержать корректный UTF-8. `Length` возвращает количество Unicode code points. `Substring` принимает начальный индекс и длину; начальный индекс за концом строки возвращает пустую строку, а длина, выходящая за конец, обрезается. Отрицательные индексы и длина приводят к runtime error. Поиск возвращает `-1`, если подстрока не найдена. Для пустой искомой строки `IndexOf` возвращает `0`, а `LastIndexOf` — длину исходной строки.

`Replace` заменяет все неперекрывающиеся вхождения буквальной подстроки; пустая искомая строка оставляет исходное значение без изменений. `Split` сохраняет пустые элементы, в том числе между соседними разделителями и в конце строки; пустой разделитель недопустим. `Join` объединяет элементы массива указанным разделителем.

`Trim`, `TrimStart` и `TrimEnd` удаляют ASCII whitespace. `ToLower` и `ToUpper` выполняют ASCII-преобразование регистра. Эти операции не обещают полноценное Unicode-преобразование регистра или Unicode-классификацию пробельных символов.
