# JASS# для VS Code

Подсветка `.w3js`, диагностика, сборка скрипта и карты.

## Сборка VSIX

Из папки расширения:

```bash
cd src/jasssharp-vscode
npm install
npm run package
```

`npm run package` компилирует TypeScript и вызывает `@vscode/vsce package`. Появится файл `jasssharp-0.1.0.vsix`.

Установка в VS Code: **Extensions** → `…` → **Install from VSIX…**.

Если `vsce` не в PATH после `npm install`, можно так:

```bash
npx @vscode/vsce package
```

## Иконка

Положите логотип как `icon.png` (128×128) в эту папку. Он указан в `package.json` как иконка расширения и языка.

## Команды

- **JASS#: Build** / **Build vJASS** / **Build Lua** — трансляция текущего файла
- **JASS#: Build Map** — по `jasssharp.json` пишет `war3map.lua` или `war3map.j` в папку карты
- **JASS#: Run Map** — то же и запускает Warcraft III с `-loadfile`

Диагностика идёт сама: расширение зовёт `jasssharp check` и подчёркивает ошибки.

## Настройка

`jasssharp.compilerPath` — путь к собранному `jasssharp`. Пусто — ищется в PATH.
`jasssharp.gamePath` — запасной путь к игре (передаётся как `JASSSHARP_GAME`).
