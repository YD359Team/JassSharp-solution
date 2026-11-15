# JASS#

JASS# — язык с C#-подобным синтаксисом для разработки логики Warcraft III Reforged и автономной трансляции исходников `.w3js` в Lua.

Репозиторий содержит компилятор/CLI на .NET и расширение VS Code с подсветкой синтаксиса, IntelliSense и диагностикой.

## Возможности

- Классы, поля, методы, конструкторы и статические методы; одиночное наследование классов, множественная реализация/иерархия интерфейсов, проверка контрактов и сигнатур унаследованных методов.
- Типовая совместимость Warcraft III с наследованием `handle → agent → widget → unit/item/destructable` и доступом `value.HandleId` для handle-подтипов.
- Управление потоком `break`, `continue` и `return;` в циклах и методах без возвращаемого значения.
- Примитивные типы `int`, `float`, `bool`, `string`, а также типы Warcraft III.
- Вывод типа локальной переменной через `var`.
- Проверка совместимости типов и диагностика некорректных присваиваний.
- Перегрузка арифметических, сравнительных и пользовательских операторов.
- Массивы и jagged arrays (`T[][]`) с нулевой индексацией в исходном коде.
- Циклы `while`, `do while`, `for` и `foreach` (Lua backend).
- Пространства имён, `using` и разрешение зависимостей между исходными файлами.
- Встроенная библиотека Warcraft III и стандартная библиотека (`Math`, `Strings`, `Color`, векторы, геометрия, таймеры, группы, способности, плавающий текст, молнии, расширенный инвентарь, таланты, RPG-интеграция, эффекты, настройки камеры и тематического аудио, Frame API).

Lua — единственный backend JASS#. Условная компиляция под vJASS/JASS и генерация этих форматов не входят в язык.

## Warcraft III Reforged 3.0.x API

- [Warcraft III 3.0.0 API](docs/warcraft-3-0-0.md) — управление атаками, аурами, перезарядками способностей и анимациями спецэффектов.
- [Warcraft III 3.0.1 API](docs/warcraft-3-0-1.md) — новые функции лечения, талантов, экипировки, эффектов и управления камерой.
- [Object-data field constants](docs/object-data-fields.md) — константы полей способностей, предметов и юнитов для generic field natives.
- [Reforged gameplay stats](docs/reforged-gameplay-stats.md) — аудит Ability Amp, Ability Vamp, Ability Speed, Mana Efficiency, критических характеристик и Resolve; неподтверждённые native API намеренно не добавлены.
- [Warcraft III handle types and control flow](docs/handle-types-and-control-flow.md) — иерархия handle, свойство `HandleId`, `break`, `continue` и `return;`.
- [Classes and interfaces](docs/classes-and-interfaces.md) — наследование классов, реализация и наследование интерфейсов, проверка контрактов и Lua codegen.

## Frame API и Lua backend

Поля экземпляров в методах транслируются в `self.<field>` согласно Lua calling convention. Smoke-тест Frame API проверяет корректную квалификацию handle и trigger/action полей; подробности — в [документации Frame API](docs/frames.md).

## Структура репозитория

- `src/JassSharp.Compiler` — компилятор, проверка семантики и CLI.
- `src/jasssharp-vscode` — расширение VS Code.
- `standard-library` — исходники стандартной библиотеки, включая `Math`, `Strings` и обёртки над Warcraft III Frame API. Документация по строкам: `docs/strings.md`; по математике и векторам: `docs/operators-and-math.md`; по 2D-геометрии: `docs/geometry.md`; по таймерам Warcraft III: `docs/timers.md`; по группам юнитов: `docs/groups.md`; по способностям: `docs/abilities.md`; по Frame API: `docs/frames.md`; по цветам: `docs/colors.md`; по плавающему тексту: `docs/floating-text.md`; по молниям: `docs/lightning.md`; по инвентарю и талантам: `docs/inventory-rpg.md`; по боевым и RPG-характеристикам: `docs/rpg-stats.md`; по спецэффектам: `docs/effects.md`; по камере и аудио: `docs/camera-audio.md`.
- `samples` — демонстрационные проекты.
- `tests` — smoke fixtures и сценарии проверки компилятора.
- `docs` — дополнительные заметки по языку, включая [строковые операции](docs/strings.md) и [встраивание Lua](docs/lua-interop.md). Поддерживаются циклы `while`, `do while`, `for` и `foreach`; Lua-specific блоки описаны в [Lua interop](docs/lua-interop.md).

## Сборка компилятора

Требуется .NET SDK, соответствующий целевому фреймворку проекта.

```powershell
dotnet build "JassSharp solution.slnx" -c Release
```

После сборки CLI можно запускать через проект:

```powershell
dotnet run --project src/JassSharp.Compiler -- check samples/fireball.w3js
dotnet run --project src/JassSharp.Compiler -- build samples/fireball.w3js --target lua
```

Команда `build` сохраняет Lua-файл в каталоге `bin` рядом с исходником.

## Режимы проекта

Режим задаётся необязательным свойством `mode` в `jasssharp.json`. Если свойство отсутствует, используется `wc3`.

### Warcraft III — `wc3`

Обычный режим сборки карты. Использует конфигурацию карты, игровые пути и MPQ-инструменты. Минимальная конфигурация:

```json
{
  "mode": "wc3",
  "map": "map",
  "target": "lua",
  "entry": "src/Main.w3js"
}
```

### Автономная трансляция — `headless`

Режим для использования JASS# как самостоятельного транслятора. Не требует установленной игры, пути к Warcraft III, карты или MPQ-инструментов.

```json
{
  "mode": "headless",
  "entry": "src/Main.w3js",
  "output": "bin/Main.lua"
}
```

Запуск:

```powershell
dotnet run --project src/JassSharp.Compiler -- map --project .
```

Если `entry` не задан, обрабатываются все файлы `.w3js` в `src`. Если `output` не задан, результат записывается в `bin/<имя-проекта>.lua`. В headless-режиме не создаются Warcraft III entry points `config()` и `main()`; встроенный `Print(...)` преобразуется в Lua `print(...)`.

### Модуль Warcraft III

В headless-режиме Warcraft III API не подключается неявно. Для доступа к типам, native-функциям, константам и расширенным членам API требуется:

```csharp
using Warcraft3;
```

Это разрешает компилятору проверять символы Warcraft III, но само по себе не добавляет Warcraft runtime в автономный Lua-файл.

## Массивы и foreach

Массивы объявляются с указанием типа элемента. Поддерживаются массивы массивов:

```csharp
int[][] matrix = new int[][];
matrix[0] = new int[];
matrix[0][1] = 42;

foreach (var row in matrix)
{
    Print(row[0]);
}
```

Индексы в исходном JASS# начинаются с нуля; Lua backend выполняет необходимую коррекцию индексации. Для массивов `foreach` генерирует индексный Lua-цикл. Для Warcraft III `group` используется `ForGroup` и `GetEnumUnit()`. Переменная итерации может объявляться через `var`.

`foreach` поддерживается Lua backend. Для него требуется коллекция-массив либо `group` с подключённым `using Warcraft3;`.

## Расширение VS Code

Исходники расширения находятся в `src/jasssharp-vscode`.

```powershell
cd src/jasssharp-vscode
npm.cmd install
npm.cmd run compile
npm.cmd run package
```

Полученный VSIX можно установить через **Extensions → … → Install from VSIX…**.

Основные настройки:

- `jasssharp.compilerPath` — путь к `jasssharp.exe` или `jasssharp.dll`; пустое значение означает поиск в `PATH`.
- `jasssharp.projectFile` — имя или путь к конфигурации проекта; по умолчанию `jasssharp.json`.
- `jasssharp.gamePath` — путь к Warcraft III для операций в режиме `wc3`.
- `jasssharp.editorPath` — путь к World Editor.
- `jasssharp.mpqToolPath` — путь к MPQ-инструменту.

Расширение выполняет диагностику сохранённых файлов и предоставляет completion, hover, semantic highlighting и форматирование.

## Проверки

Smoke-сценарии компилятора запускаются из корня репозитория:

```powershell
./tests/run-smoke.ps1
```

Сценарий собирает компилятор и проверяет семантические ошибки, операторы, headless-режим, массивы, `foreach` и игровые модули стандартной библиотеки.


## Деструкторы и освобождение ресурсов

Класс может объявить деструктор в C#-подобной форме:

```csharp
public class TemporaryResource
{
    public TemporaryResource()
    {
    }

    ~TemporaryResource()
    {
        // Освобождение ресурсов Warcraft III
    }
}

public void Example()
{
    TemporaryResource resource = new TemporaryResource();
    resource.Dispose();
}
```

Деструктор JASS# — это **ручной механизм освобождения**, а не финализатор CLR: сборщик мусора Lua/Warcraft III не вызывает его автоматически. Компилятор транслирует `~TemporaryResource()` в обычный нестатический метод `Dispose()`; вызов выполняется явно через экземпляр. Деструктор не принимает аргументов, не возвращает значение и не может иметь модификаторы `static`, `public` или `private`. В одном классе нельзя одновременно объявить `~TypeName()` и отдельный `Dispose()`. Повторный вызов `Dispose()` повторно выполняет тело деструктора: если освобождение должно быть идемпотентным, класс должен самостоятельно защищать повторную очистку.

## Числа и битовые операции

Целочисленные литералы поддерживают десятичную и шестнадцатеричную запись: `42`, `0x2A`, `0xFFFFFFFF`. Hex-литералы имеют тип `int` и интерпретируются как 32-битные значения в дополнительном коде: `0xFFFFFFFF` соответствует `-1`, а `0x80000000` — `int.MinValue`. Значения hex выше `0xFFFFFFFF` и положительные десятичные целые литералы выше `int.MaxValue` отклоняются проверкой типов. Запись `-2147483648` поддерживается как минимальное значение `int`. Литералы типа `float` проверяются на переполнение и потерю ненулевого значения при приведении к 32-битному float.

Для `int` доступны битовые операции:

- `a << n` — сдвиг влево.
- `a >> n` — логический сдвиг вправо с заполнением нулями.
- `a & b` — побитовое AND.
- `a ^ b` — побитовое XOR.
- `a | b` — побитовое OR.

Поддерживаются составные присваивания `<<=`, `>>=`, `&=`, `^=` и `|=`. Операции принимают только два операнда типа `int`; неявное преобразование `float` в `int` не выполняется.

Приоритет от высокого к низкому: сдвиги, сравнения, равенство, `&`, `^`, `|`, логическое `&&`, логическое `||`. Бинарные операции одного уровня группируются слева направо. Сдвиг с отрицательным количеством бит меняет направление; сдвиг на 32 бита или больше даёт ноль.

Lua backend использует нативные битовые операторы Lua 5.3. Оператор JASS# `^` транслируется в Lua `~`, поскольку в Lua символ `^` зарезервирован для возведения в степень. Генерация не использует `bit32` и сторонние библиотеки. Целочисленная арифметика использует переполнение по модулю 2³². Битовые операции требуют целочисленных операндов: компилятор не полагается на неявное runtime-преобразование float в integer. Семантика JASS# рассчитана на 32-битные целые числа Warcraft III Reforged, а не на стандартную 64-битную конфигурацию Lua 5.3.
