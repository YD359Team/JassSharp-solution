# JASS#

JASS# — язык с C#-подобным синтаксисом для разработки логики Warcraft III Reforged и автономной трансляции исходников `.w3js` в Lua.

Репозиторий содержит компилятор/CLI на .NET и расширение VS Code с подсветкой синтаксиса, IntelliSense и диагностикой.

## Возможности

- Классы, поля, методы, конструкторы и статические методы.
- Примитивные типы `int`, `float`, `bool`, `string`, а также типы Warcraft III.
- Вывод типа локальной переменной через `var`.
- Проверка совместимости типов и диагностика некорректных присваиваний.
- Перегрузка арифметических, сравнительных и пользовательских операторов.
- Массивы и jagged arrays (`T[][]`) с нулевой индексацией в исходном коде.
- `foreach` по массивам и Warcraft III `group` (Lua backend).
- Пространства имён, `using` и разрешение зависимостей между исходными файлами.
- Встроенная библиотека Warcraft III и стандартная библиотека.

Текущая цель генерации — Lua. Генерация vJASS/JASS через CLI больше не поддерживается.

## Структура репозитория

- `src/JassSharp.Compiler` — компилятор, проверка семантики и CLI.
- `src/jasssharp-vscode` — расширение VS Code.
- `standard-library` — исходники стандартной библиотеки, включая обёртки над Warcraft III Frame API. Документация по математике и векторам: `docs/operators-and-math.md`; по 2D-геометрии: `docs/geometry.md`; по таймерам Warcraft III: `docs/timers.md`; по группам юнитов: `docs/groups.md`; по способностям: `docs/abilities.md`; по Frame API: `docs/frames.md`.
- `samples` — демонстрационные проекты.
- `tests` — smoke fixtures и сценарии проверки компилятора.
- `docs` — дополнительные заметки по языку.

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
