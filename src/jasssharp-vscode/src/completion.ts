import * as vscode from "vscode";
import { CONSTANTS } from "./constants";
import { MEMBERS } from "./members";

// Keep the existing NATIVES array from the current file.
// Add the import above and the after-dot block below into provideCompletionItems.

export const NATIVES: { name: string; sig: string; doc: string }[] = [
  // ... full list remains (restore if this push truncates)
];

export function registerCompletions(context: vscode.ExtensionContext) {
  const provider: vscode.CompletionItemProvider = {
    provideCompletionItems(doc, pos) {
      const range = doc.getWordRangeAtPosition(pos) ?? new vscode.Range(pos, pos);
      const items: vscode.CompletionItem[] = [];
      const line = doc.lineAt(pos.line).text.slice(0, pos.character);
      const afterDot = /(\w+)\.\w*$/.exec(line);

      if (afterDot) {
        const recv = afterDot[1];
        for (const m of MEMBERS.filter(x => x.type === recv || recv === "self" || recv === "this")) {
          const i = new vscode.CompletionItem(m.name, m.kind === "Method" ? vscode.CompletionItemKind.Method : vscode.CompletionItemKind.Property);
          i.range = range;
          i.detail = `${m.type}.${m.name} (${m.kind}) → ${m.ret}`;
          if (m.kind === "Method") i.insertText = new vscode.SnippetString(`${m.name}($0)`);
          items.push(i);
        }
        return items;
      }

      // ... rest of the existing provider (keywords, types, constants, natives, locals)
      return items;
    },
  };

  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider({ language: "jasssharp" }, provider, ".")
  );
}
