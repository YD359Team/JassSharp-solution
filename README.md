# JASS# (JassSharp)

Язык для Warcraft III Reforged (1.30+) с синтаксисом, близким к C#. Транслируется в **vJASS** и **Lua**. Без смешивания чужого vJASS/Lua в одном файле.

Файлы — `.w3js`.

## Пример

```csharp
namespace MyMap.Spells
{
    const int FireballId = 'A000';

    class Fireball
    {
        unit Caster;
        float Damage;

        public Fireball(unit caster)
        {
            this.Caster = caster;
            this.Damage = 100.0 + caster.Level * 25.0;
        }

        public void Cast(float targetX, float targetY)
        {
            effect sfx = new effect("Abilities\\Spells\\Other\\Fireball.mdx", targetX, targetY);
            sfx.Destroy();
        }
    }

    void OnInit()
    {
        trigger t = new trigger();
        t.RegisterAnyUnitEvent(EVENT_PLAYER_UNIT_SPELL_EFFECT);
        t.AddAction(OnSpellEffect);

        unit hero = new unit(Player(0), 'Hpal', 0.0, 0.0, 270.0);
        hero.Name = "Arthas";
        hero.Life = 500.0;
        hero.SetPosition(100.0, 200.0).Kill();

        Print("Hero: ", hero, " at ", hero.X);
    }
}
```

- `new unit(...)` → `CreateUnit(...)`
- `u.Name = "..."` → `BlzSetUnitName(u, "...")`
- `u.Life` / `u.Mana` — через `GetUnitState` / `SetUnitState`
- `'XXXX'` в Lua становится знаковым int32 (big-endian), в vJASS остаётся как есть
- `OnInit` — точка входа (в vJASS — `initializer`, в Lua — вызов в конце)
- Цепочки: `hero.SetPosition(x, y).Kill()`
- Триггеры: явный `new trigger()`, методы `Register...` / `AddAction`
- `Print(...)` — любое количество аргументов любых типов → `BJDebugMsg` с автоматическим `ToString`
- `ToString` для unit/player/item и базовых типов
- `#if LUA { ... } else { ... }` и `#if VJASS` — условная компиляция
- `[Trace]` на функции — вставка отладочных сообщений

Каталог нативов — полный `common.j` (jassdoc). Свойства генерируются из пар Get/Set (119) плюс ручные `Life`/`Mana`/`Name`.

## Компилятор

```
dotnet run --project src/JassSharp.Compiler -- build file.w3js --target both
dotnet run --project src/JassSharp.Compiler -- check file.w3js
dotnet run --project src/JassSharp.Compiler -- map --project . --run
```

| Команда | Что делает |
|---|---|
| `build` | Пишет `.j` и/или `.lua` рядом с исходником |
| `check` | JSON-диагностики (ошибки парсера + неизвестные имена), с номером строки |
| `map` | Собирает все `.w3js` (или `entry`) и кладёт скрипт в папку карты |

### jasssharp.json

```json
{
  "map": "map",
  "target": "lua",
  "entry": "src/main.w3js",
  "game": "C:\\\\Program Files\\\\Warcraft III\\\\Warcraft III.exe"
}
```

- `map` — **распакованная папка** карты (не `.w3x`). Скрипт пишется в `war3map.lua` или `war3map.j`.
- `--run` запускает игру с `-loadfile`. Путь к игре: поле `game`, либо переменная `JASSSHARP_GAME`.

## Расширение VS Code

`src/jasssharp-vscode`

- Подсветка `.w3js`, сниппеты
- Диагностика: на сохранении зовёт `jasssharp check` и подчёркивает ошибки
- Команды: **Build**, **Build vJASS**, **Build Lua**, **Build Map**, **Run Map**

Настройки: `jasssharp.compilerPath`, `jasssharp.defaultTarget`, `jasssharp.gamePath`.

Сборка расширения: `npm install && npm run compile` в `src/jasssharp-vscode`, затем `vsce package` или запуск из VS Code как расширение.

## Структура

```
src/JassSharp.Compiler/     компилятор (C#)
src/jasssharp-vscode/       расширение VS Code
samples/                    примеры
```

## Сборка

```
dotnet build src/JassSharp.Compiler
```
