# Strings

JASS# supports ordinary string literals, raw multiline string literals and interpolation.

## Ordinary strings

Use double quotes. Escape sequences include \`\n\`, \`\r\`, \`\t\`, \`\0\`, \`\\"\` and \`\\\\\`.

\`\`\`csharp
string message = "First line\nSecond line";
string quoted = "He said: \"hello\"";
\`\`\`

Single-quoted literals remain Warcraft rawcode literals; they are not character or string literals.

## Raw multiline strings

Triple quotes preserve the contents without processing backslash escapes:

\`\`\`csharp
string help = """
    First line
    Second line
    Third line
    """;
\`\`\`

When the opening delimiter is followed by a newline, the compiler removes that first newline and trims the indentation used before the closing delimiter from each nonblank line. The newline immediately before the closing delimiter is not included. Inline raw strings such as \`"""text"""\` are also supported.

## Interpolated strings

Prefix a string with \`$\` and place expressions inside braces:

\`\`\`csharp
int x = 2;
int y = 3;
int z = 4;
string result = $"Product: {x * y * z}";
string status = $"Result={x + y}; enabled={true}";
\`\`\`

Expressions are parsed by the normal JASS# expression parser and checked by the semantic analyzer. Arithmetic, variables, member access and function calls can be used inside interpolation. Values are converted to strings using the same conversions as \`Print\` and string concatenation.

Escape braces by doubling them:

\`\`\`csharp
string text = $"{{literal}} and {{{x}}}";
\`\`\`

This produces \`{literal} and {2}\`.

## Interpolated multiline strings

Combine \`$\` with triple quotes:

\`\`\`csharp
string report = $"""
    Unit: {unitName}
    Health: {currentHealth}
    """;
\`\`\`

Raw multiline interpolation uses the same indentation trimming as a non-interpolated raw string. Backslashes in its literal portions are preserved.

Alignment and format specifiers (for example \`{value,10}\` and \`{value:N2}\`) are not currently supported. Interpolation currently accepts a single expression per brace pair.
