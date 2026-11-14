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

 
## Сборка GitHub Release

Workflow `.github/workflows/release.yml` запускается при публикации GitHub Release. Он:

1. Собирает компилятор и запускает `tests/run-smoke.ps1`.
2. Устанавливает зависимости расширения через `npm ci` и компилирует TypeScript.
3. Берёт версию VSIX из тега релиза (например, `v0.2.0` → `jasssharp-0.2.0.vsix`).
4. Прикладывает готовый VSIX к опубликованному GitHub Release.

Тот же workflow можно запустить вручную через **Actions → Release → Run workflow**. В этом случае версия берётся из `package.json`, а VSIX публикуется как workflow artifact, без изменения GitHub Release.

Для автоматической публикации требуется тег с SemVer-версией: `vMAJOR.MINOR.PATCH` (например, `v0.2.0`). Workflow не публикует расширение в Visual Studio Marketplace — он прикладывает VSIX к GitHub Release.

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

На текущем этапе `foreach` поддерживается только при генерации Lua. JASS# генерирует только Lua, поэтому обход выполняется индексным Lua-циклом с сохранением нулевой индексации исходного кода.

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

Объявление `~TypeName()` доступно как метод `Dispose()` в IntelliSense. Он вызывается вручную через экземпляр: `resource.Dispose()`. Автоматического вызова при сборке мусора нет; каждый повторный вызов снова выполняет тело деструктора, поэтому идемпотентность при необходимости реализуется в самом классе.


## Сниппеты

Расширение регистрирует сниппеты для языка JASS#. Основные префиксы:

| Префикс | Вставка |
| --- | --- |
| `namespace`, `wc3namespace` | Пространство имён, с подключением Warcraft3 или без него |
| `class`, `classctor` | Класс, в том числе с конструктором |
| `ctor` | Конструктор класса |
| `dtor` | Ручной деструктор `~TypeName()` |
| `method`, `staticmethod`, `function` | Методы и функции |
| `main`, `entry`, `oninit` | Точка входа пользовательской логики Warcraft III |
| `enum`, `usingwc3` | Перечисление и подключение API Warcraft III |
| `foreach`, `jagged` | Обход коллекций и массивы массивов |
| `trigger`, `action` | Триггеры и обработчики событий Warcraft III |
| `timer`, `timerperiodic`, `timercallback` | Одноразовые и периодические таймеры |
| `groupenum`, `groupcallback` | Перечисление юнитов через `group` / `ForGroup` |
| `createunit` | Создание юнита по rawcode |
| `spelltrigger`, `spellcallback` | Регистрация события применения способности и callback |
| `effectpoint`, `effectattach` | Special effects в точке и на объекте |
| `unitdamage` | Нанесение урона через `UnitDamageTarget` |
| `spellclass` | Класс способности с `Caster`, `Damage` и `Cast()` |
| `spellhandler` | Обработчик применения способности с проверкой rawcode |
| `spellmodule` | Полный модуль способности: класс, обработчик и регистрация события |
| `debug`, `print` | Отладочный вывод |

**Важно о точке входа:** префикс `main` вставляет `void OnInit()`, а не объявление `main()`. В режиме `wc3` компилятор сам генерирует Warcraft III-функции `config()` и `main()` и вызывает `OnInit()` из сгенерированной точки входа. Объявлять собственную функцию `main()` не нужно.


## Циклы и встроенный Lua

Поддерживаются `while`, `do { ... } while (...);`, `for` и `foreach`. Цикл `do while` генерируется как Lua `repeat ... until`.

Для узкоспециализированного кода, который должен быть записан непосредственно на Lua, используйте инструкцию `Lua.Raw`:

```csharp
Lua.Raw("""
    local value = GetRandomInt(1, 100)
    print(value)
""");
```

Содержимое блока вставляется в Lua без синтаксического анализа JASS#. Поэтому этот механизм намеренно является escape hatch: он не проверяет имена, типы и корректность Lua-кода и не переносим на другие backend-ы.
