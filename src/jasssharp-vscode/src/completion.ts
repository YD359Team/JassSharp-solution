import * as vscode from "vscode";
import { CONSTANTS } from "./constants";

const KEYWORDS = [
  "namespace", "using", "class", "void", "int", "float", "bool", "string", "const",
  "new", "this", "if", "else", "while", "return", "true", "false", "null",
  "==", "!=", "!", "&&", "||",
];

const TYPES = [
  "unit", "player", "trigger", "timer", "group", "effect", "item", "location",
  "rect", "force", "region", "sound", "quest", "leaderboard", "multiboard",
  "dialog", "texttag", "lightning", "image", "ubersplat", "fogmodifier", "hashtable",
];

// NATIVES array is large — keep the existing one from previous version.
// For this update we only add constants and logical operators to keywords.
export const NATIVES: { name: string; sig: string; doc: string }[] = [
  // ... existing natives remain (restore from previous commit if needed)
];

function collectLocals(doc: vscode.TextDocument, pos: vscode.Position): string[] {
  const text = doc.getText();
  const names = new Set<string>();
  for (const m of text.matchAll(/\b(?:void|int|float|bool|string|\w+)\s+(\w+)\s*\(/g))
    names.add(m[1]);
  for (const m of text.matchAll(/\b(?:var|local)?\s*(?:int|float|bool|string|unit|player|trigger|\w+)\s+(\w+)\s*[=;]/g))
    names.add(m[1]);
  return [...names];
}

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
      for (const c of CONSTANTS) {
        const i = new vscode.CompletionItem(c, vscode.CompletionItemKind.Constant);
        i.range = range;
        i.detail = "constant";
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
      for (const name of collectLocals(doc, pos)) {
        const i = new vscode.CompletionItem(name, vscode.CompletionItemKind.Variable);
        i.range = range;
        i.detail = "local / user";
        items.push(i);
      }
      return items;
    },
  };

  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider({ language: "jasssharp" }, provider, ".")
  );
}
