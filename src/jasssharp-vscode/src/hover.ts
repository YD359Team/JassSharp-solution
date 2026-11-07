import * as vscode from "vscode";

const SIGS: Record<string, { sig: string; doc: string }> = {
  CreateUnit: { sig: "CreateUnit(player, rawcode, x, y, face) → unit", doc: "Создать юнита" },
  CreateTrigger: { sig: "CreateTrigger() → trigger", doc: "Создать триггер" },
  CreateTimer: { sig: "CreateTimer() → timer", doc: "Создать таймер" },
  TriggerRegisterAnyUnitEventBJ: { sig: "TriggerRegisterAnyUnitEventBJ(trigger, event) → void", doc: "Подписать триггер на событие" },
  TriggerAddAction: { sig: "TriggerAddAction(trigger, action) → void", doc: "Добавить действие" },
  TimerStart: { sig: "TimerStart(timer, timeout, periodic, handler) → void", doc: "Запустить таймер" },
  BJDebugMsg: { sig: "BJDebugMsg(msg) → void", doc: "Вывести сообщение" },
  GetTriggerUnit: { sig: "GetTriggerUnit() → unit", doc: "Юнит, вызвавший триггер" },
  GetEventDamage: { sig: "GetEventDamage() → float", doc: "Урон события" },
  BlzSetUnitName: { sig: "BlzSetUnitName(unit, name) → void", doc: "Установить имя юнита" },
  SetUnitLife: { sig: "SetUnitLife(unit, life) → void", doc: "Установить жизнь" },
  GetUnitLife: { sig: "GetUnitLife(unit) → float", doc: "Получить жизнь" },
  Player: { sig: "Player(id) → player", doc: "Игрок по номеру" },
  Location: { sig: "Location(x, y) → location", doc: "Точка" },
  Rect: { sig: "Rect(minx, miny, maxx, maxy) → rect", doc: "Прямоугольник" },
  CreateGroup: { sig: "CreateGroup() → group", doc: "Группа юнитов" },
  GroupEnumUnitsInRange: { sig: "GroupEnumUnitsInRange(group, x, y, radius, filter) → void", doc: "Выбрать юнитов в радиусе" },
  ForGroup: { sig: "ForGroup(group, callback) → void", doc: "Перебрать группу" },
  RemoveUnit: { sig: "RemoveUnit(unit) → void", doc: "Удалить юнита" },
  KillUnit: { sig: "KillUnit(unit) → void", doc: "Убить юнита" },
  IssueImmediateOrder: { sig: "IssueImmediateOrder(unit, order) → void", doc: "Отдать приказ" },
  GetUnitState: { sig: "GetUnitState(unit, state) → float", doc: "Получить состояние юнита" },
  SetUnitState: { sig: "SetUnitState(unit, state, value) → void", doc: "Установить состояние юнита" },
  GetOwningPlayer: { sig: "GetOwningPlayer(unit) → player", doc: "Владелец юнита" },
  IsUnitAliveBJ: { sig: "IsUnitAliveBJ(unit) → bool", doc: "Жив ли юнит" },
  GetUnitX: { sig: "GetUnitX(unit) → float", doc: "Координата X" },
  GetUnitY: { sig: "GetUnitY(unit) → float", doc: "Координата Y" },
  SetUnitPosition: { sig: "SetUnitPosition(unit, x, y) → void", doc: "Установить позицию" },
  CreateItem: { sig: "CreateItem(rawcode, x, y) → item", doc: "Создать предмет" },
  UnitAddItem: { sig: "UnitAddItem(unit, item) → bool", doc: "Дать предмет юниту" },
  GetSpellAbilityId: { sig: "GetSpellAbilityId() → int", doc: "ID способности" },
  GetTriggerPlayer: { sig: "GetTriggerPlayer() → player", doc: "Игрок, вызвавший триггер" },
};

export function registerHover(context: vscode.ExtensionContext) {
  const provider: vscode.HoverProvider = {
    provideHover(doc, pos) {
      const range = doc.getWordRangeAtPosition(pos);
      if (!range) return;
      const word = doc.getText(range);
      const info = SIGS[word];
      if (!info) return;
      const md = new vscode.MarkdownString();
      md.appendCodeblock(info.sig, "jasssharp");
      md.appendMarkdown(`\n\n${info.doc}`);
      return new vscode.Hover(md, range);
    },
  };

  context.subscriptions.push(
    vscode.languages.registerHoverProvider({ language: "jasssharp" }, provider)
  );
}
