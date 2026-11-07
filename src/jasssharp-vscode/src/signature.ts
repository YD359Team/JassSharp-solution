import * as vscode from "vscode";
import { NATIVES } from "./completion";

export function registerSignatureHelp(context: vscode.ExtensionContext) {
  const provider: vscode.SignatureHelpProvider = {
    provideSignatureHelp(doc, pos) {
      const line = doc.lineAt(pos.line).text.slice(0, pos.character);
      const match = line.match(/(\w+)\s*\(([^()]*)$/);
      if (!match) return;
      const name = match[1];
      const argsSoFar = match[2].split(",").length - 1;
      const native = NATIVES.find(n => n.name === name);
      if (!native) return;
      const params = native.sig.slice(native.sig.indexOf("(") + 1, native.sig.lastIndexOf(")")).split(",").map(s => s.trim()).filter(Boolean);
      const help = new vscode.SignatureHelp();
      const sig = new vscode.SignatureInformation(native.sig, native.doc || "");
      sig.parameters = params.map(p => new vscode.ParameterInformation(p));
      help.signatures = [sig];
      help.activeSignature = 0;
      help.activeParameter = Math.min(argsSoFar, Math.max(0, params.length - 1));
      return help;
    },
  };

  context.subscriptions.push(
    vscode.languages.registerSignatureHelpProvider({ language: "jasssharp" }, provider, "(", ",")
  );
}
