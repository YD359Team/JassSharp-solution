import * as vscode from "vscode";

// Базовый набор для подсказок. Полный каталог подтянем позже из компилятора.
const KEYWORDS = [
  "namespace", "class", "void", "int", "float", "bool", "string", "const",
  "new", "this", "if", "else", "while", "return", "true", "false", "null",
];

const TYPES = [
  "unit", "player", "trigger", "timer", "group", "effect", "item", "location",
  "rect", "force", "region", "sound", "quest", "leaderboard", "multiboard",
  "dialog", "texttag", "lightning", "image", "ubersplat", "fogmodifier", "hashtable",
];

const NATIVES: { name: string; sig: string; doc: string }[] = [
  { name: "CreateUnit", sig: "CreateUnit(player, rawcode, x, y, face)", doc: "Создать юнита" },
  { name: "CreateTrigger", sig: "CreateTrigger()", doc: "Создать триггер" },
  { name: "CreateTimer", sig: "CreateTimer()", doc: "Создать таймер" },
  { name: "TriggerRegisterAnyUnitEventBJ", sig: "TriggerRegisterAnyUnitEventBJ(trigger, event)", doc: "Подписать триггер на событие" },
  { name: "TriggerAddAction", sig: "TriggerAddAction(trigger, action)", doc: "Добавить действие" },
  { name: "TimerStart", sig: "TimerStart(timer, timeout, periodic, handler)", doc: "Запустить таймер" },
  { name: "BJDebugMsg", sig: "BJDebugMsg(msg)", doc: "Вывести сообщение" },
  { name: "GetTriggerUnit", sig: "GetTriggerUnit()", doc: "Юнит, вызвавший триггер" },
  { name: "GetEventDamage", sig: "GetEventDamage()", doc: "Урон события" },
  { name: "BlzSetUnitName", sig: "BlzSetUnitName(unit, name)", doc: "Установить имя юнита" },
  { name: "SetUnitLife", sig: "SetUnitLife(unit, life)", doc: "Установить жизнь" },
  { name: "GetUnitLife", sig: "GetUnitLife(unit)", doc: "Получить жизнь" },
  { name: "Player", sig: "Player(id)", doc: "Игрок по номеру" },
  { name: "Location", sig: "Location(x, y)", doc: "Точка" },
  { name: "Rect", sig: "Rect(minx, miny, maxx, maxy)", doc: "Прямоугольник" },
  { name: "CreateGroup", sig: "CreateGroup()", doc: "Группа юнитов" },
  { name: "GroupEnumUnitsInRange", sig: "GroupEnumUnitsInRange(group, x, y, radius, filter)", doc: "Выбрать юнитов в радиусе" },
  { name: "ForGroup", sig: "ForGroup(group, callback)", doc: "Перебрать группу" },
  { name: "RemoveUnit", sig: "RemoveUnit(unit)", doc: "Удалить юнита" },
  { name: "KillUnit", sig: "KillUnit(unit)", doc: "Убить юнита" },
  { name: "IssueImmediateOrder", sig: "IssueImmediateOrder(unit, order)", doc: "Отдать приказ" },
];

export function registerCompletions(context: vscode.ExtensionContext) {
  const provider: vscode.CompletionItemProvider = {
    provideCompletionItems(doc, pos) {
      const range = doc.getWordRangeAtPosition(pos) ?? new vscode.Range(pos, pos);
      const items: vscode.CompletionItem[] = [];

      for (const k of KEYWORDS) {
        const i = new vscode.CompletionItem(k, vscode.CompletionItemKind.Keyword);
        i.range = range;
        items.push(i);
      }
      for (const t of TYPES) {
        const i = new vscode.CompletionItem(t, vscode.CompletionItemKind.Class);
        i.range = range;
        i.detail = "handle type";
        items.push(i);
      }
      for (const n of NATIVES) {
        const i = new vscode.CompletionItem(n.name, vscode.CompletionItemKind.Function);
        i.range = range;
        i.detail = n.sig;
        i.documentation = n.doc;
        i.insertText = new vscode.SnippetString(`${n.name}($0)`);
        items.push(i);
      }
      return items;
    },
  };

  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider({ language: "jasssharp" }, provider, ".")
  );
}
