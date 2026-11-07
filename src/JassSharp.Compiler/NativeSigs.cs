namespace JassSharp;

public sealed record NativeSig(string Name, string ReturnType, string[] Params, string Doc);

public static class NativeSigs
{
    public static readonly Dictionary<string, NativeSig> Map = new(StringComparer.Ordinal)
    {
        ["CreateUnit"] = new("CreateUnit", "unit", ["player", "int", "float", "float", "float"], "Создать юнита"),
        ["CreateTrigger"] = new("CreateTrigger", "trigger", [], "Создать триггер"),
        ["CreateTimer"] = new("CreateTimer", "timer", [], "Создать таймер"),
        ["TriggerRegisterAnyUnitEventBJ"] = new("TriggerRegisterAnyUnitEventBJ", "void", ["trigger", "playerunitevent"], "Подписать триггер на событие"),
        ["TriggerAddAction"] = new("TriggerAddAction", "void", ["trigger", "code"], "Добавить действие"),
        ["TimerStart"] = new("TimerStart", "void", ["timer", "float", "bool", "code"], "Запустить таймер"),
        ["BJDebugMsg"] = new("BJDebugMsg", "void", ["string"], "Вывести сообщение"),
        ["GetTriggerUnit"] = new("GetTriggerUnit", "unit", [], "Юнит, вызвавший триггер"),
        ["GetEventDamage"] = new("GetEventDamage", "float", [], "Урон события"),
        ["BlzSetUnitName"] = new("BlzSetUnitName", "void", ["unit", "string"], "Установить имя юнита"),
        ["SetUnitLife"] = new("SetUnitLife", "void", ["unit", "float"], "Установить жизнь"),
        ["GetUnitLife"] = new("GetUnitLife", "float", ["unit"], "Получить жизнь"),
        ["Player"] = new("Player", "player", ["int"], "Игрок по номеру"),
        ["Location"] = new("Location", "location", ["float", "float"], "Точка"),
        ["Rect"] = new("Rect", "rect", ["float", "float", "float", "float"], "Прямоугольник"),
        ["CreateGroup"] = new("CreateGroup", "group", [], "Группа юнитов"),
        ["GroupEnumUnitsInRange"] = new("GroupEnumUnitsInRange", "void", ["group", "float", "float", "float", "boolexpr"], "Выбрать юнитов в радиусе"),
        ["ForGroup"] = new("ForGroup", "void", ["group", "code"], "Перебрать группу"),
        ["RemoveUnit"] = new("RemoveUnit", "void", ["unit"], "Удалить юнита"),
        ["KillUnit"] = new("KillUnit", "void", ["unit"], "Убить юнита"),
        ["IssueImmediateOrder"] = new("IssueImmediateOrder", "void", ["unit", "string"], "Отдать приказ"),
        ["GetUnitState"] = new("GetUnitState", "float", ["unit", "unitstate"], "Получить состояние юнита"),
        ["SetUnitState"] = new("SetUnitState", "void", ["unit", "unitstate", "float"], "Установить состояние юнита"),
        ["GetOwningPlayer"] = new("GetOwningPlayer", "player", ["unit"], "Владелец юнита"),
        ["IsUnitAliveBJ"] = new("IsUnitAliveBJ", "bool", ["unit"], "Жив ли юнит"),
        ["GetUnitX"] = new("GetUnitX", "float", ["unit"], "Координата X"),
        ["GetUnitY"] = new("GetUnitY", "float", ["unit"], "Координата Y"),
        ["SetUnitPosition"] = new("SetUnitPosition", "void", ["unit", "float", "float"], "Установить позицию"),
        ["CreateItem"] = new("CreateItem", "item", ["int", "float", "float"], "Создать предмет"),
        ["UnitAddItem"] = new("UnitAddItem", "bool", ["unit", "item"], "Дать предмет юниту"),
        ["GetSpellAbilityId"] = new("GetSpellAbilityId", "int", [], "ID способности"),
        ["GetTriggerPlayer"] = new("GetTriggerPlayer", "player", [], "Игрок, вызвавший триггер"),
    };
}
