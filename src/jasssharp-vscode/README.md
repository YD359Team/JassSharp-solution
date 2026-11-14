# JASS# для VS Code

Подсветка \`.w3js\`, IntelliSense, диагностика и запуск команд компилятора JASS#.

## Установка и сборка VSIX

Из папки расширения:

\`\`\`powershell
cd src/jasssharp-vscode
npm.cmd install
npm.cmd run package
\`\`\`

Установка в VS Code: **Extensions** → \`…\` → **Install from VSIX…**.

## Команды

- **JASS#: Build** / **Build Lua** — трансляция текущего файла в Lua.
- **JASS#: Build Map** — сборка карты в режиме \`wc3\`.
- **JASS#: Run Map** — сборка и запуск карты в режиме \`wc3\`.
- **JASS#: Open in World Editor** — открытие карты в World Editor.

В режиме \`headless\` команды карты и запуска скрыты. Используйте **Build Lua** или CLI-команду \`jasssharp map\`.

## Режим проекта

Режим задаётся в \`jasssharp.json\`:

\`\`\`json
{
  "mode": "wc3",
  "map": "map",
  "target": "lua",
  "entry": "src/Main.w3js"
}
\`\`\`

\`mode\` необязателен. Если он отсутствует, используется \`wc3\`.

### wc3

Обычный режим разработки карт Warcraft III. Сохраняется текущий pipeline сборки карты, включая MPQ-инструменты, игровые пути и стандартные Lua entry points.

### headless

Автономная трансляция W3JS в Lua. Не требует установленной игры, пути к Warcraft III, карты или MPQ-инструментов. Например:

\`\`\`json
{
  "mode": "headless",
  "entry": "src/Main.w3js",
  "output": "bin/Main.lua"
}
\`\`\`

Если \`entry\` не задан, компилируются все исходники из \`src\`. Если \`output\` не задан, результат сохраняется в \`bin/<имя-проекта>.lua\`.

В headless-режиме не генерируются Warcraft III entry points \`config()\` и \`main()\`, а встроенный \`Print(...)\` транслируется в Lua \`print(...)\`.

## Массивы и foreach

Поддерживаются массивы массивов с сохранением ранга типа:

```csharp
int[][] matrix = new int[][];
matrix[0] = new int[];
matrix[0][1] = 42;
```

Индексация в исходном коде JASS# нулевая; Lua backend корректирует её при генерации.

Для массивов `foreach` транслируется в индексный цикл Lua backend. Коллекция вычисляется один раз, затем обходится от первого до последнего элемента Lua-последовательности:

```csharp
foreach (var row in matrix)
{
    Print(row[0]);
}
```

Для Warcraft III `group` используется callback-механизм `ForGroup`, а переменная итерации получает тип `unit`. Поэтому `group` требует `using Warcraft3;`.

На текущем этапе `foreach` поддерживается только при генерации Lua. Для JASS backend компилятор выдаёт ошибку: у JASS-массива нет доступной длины, а Lua-табличный обход нельзя корректно подменить JASS-циклом без изменения семантики.

## Модуль Warcraft III

Типы, native-функции, константы и расширенные члены Warcraft III доступны в IntelliSense после явного подключения:

\`\`\`csharp
using Warcraft3;
\`\`\`

В headless-режиме это подключение также включает семантическую проверку Warcraft III API. Оно не добавляет игру или Warcraft runtime в Lua-файл — headless остаётся режимом трансляции.

## Настройки расширения

- \`jasssharp.compilerPath\` — путь к \`jasssharp.exe\` или \`jasssharp.dll\`. Пустое значение означает поиск в \`PATH\`.
- \`jasssharp.projectFile\` — имя или путь к конфигурации проекта; по умолчанию \`jasssharp.json\`.
- \`jasssharp.gamePath\` — запасной путь к Warcraft III, передаваемый как \`JASSSHARP_GAME\` в режиме \`wc3\`.
- \`jasssharp.mpqToolPath\` — путь к MPQ-инструменту для сборки карты.

Диагностика запускается автоматически для сохранённых файлов через \`jasssharp check\`. Ошибки несовместимых присваиваний также подсвечиваются непосредственно при редактировании.


## Деструкторы

Объявление `~TypeName()` доступно как метод `Dispose()` в IntelliSense. Он вызывается вручную через экземпляр: `resource.Dispose()`. Автоматического вызова при сборке мусора нет.
