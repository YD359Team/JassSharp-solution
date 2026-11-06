# JASS# для VS Code

Подсветка `.w3js`, диагностика, сборка скрипта и карты.

## Команды

- **JASS#: Build** / **Build vJASS** / **Build Lua** — трансляция текущего файла
- **JASS#: Build Map** — по `jasssharp.json` пишет `war3map.lua` или `war3map.j` в папку карты
- **JASS#: Run Map** — то же и запускает Warcraft III с `-loadfile`

Диагностика идёт сама: расширение зовёт `jasssharp check` и подчёркивает ошибки парсера и неизвестные имена (с номером строки).

## Проект карты

```json
{
  "map": "map",
  "target": "lua",
  "entry": "src/main.w3js",
  "game": "C:\\\\Program Files\\\\Warcraft III\\\\Warcraft III.exe"
}
```

`map` — распакованная папка карты, не `.w3x`. Скрипт кладётся в папку, игра грузит её через `-loadfile`.

## Настройка

`jasssharp.compilerPath` — путь к собранному `jasssharp`. Пусто — ищется в PATH.
`jasssharp.gamePath` — запасной путь к игре (передаётся как `JASSSHARP_GAME`, если в json пусто).
