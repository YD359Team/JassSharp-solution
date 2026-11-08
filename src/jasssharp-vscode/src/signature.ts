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

    context.subscriptions.push(
        vscode.languages.registerDocumentFormattingEditProvider("jasssharp", {
            provideDocumentFormattingEdits(doc) {
                const edits: vscode.TextEdit[] = [];
                const text = doc.getText();
                const lines = text.split(/\r?\n/);
                const out: string[] = [];
                let indent = 0;
                const tab = "    ";

                for (let raw of lines) {
                    let line = raw.trimEnd();
                    const trimmed = line.trim();
                    if (trimmed === "") {
                        out.push("");
                        continue;
                    }

                    // decrease indent before closing brace
                    if (trimmed.startsWith("}")) indent = Math.max(0, indent - 1);

                    // if line ends with { and is not empty before it, split
                    if (trimmed.endsWith("{") && trimmed !== "{") {
                        const before = trimmed.slice(0, -1).trimEnd();
                        if (before.length > 0) {
                            out.push(tab.repeat(indent) + before);
                            out.push(tab.repeat(indent) + "{");
                        } else {
                            out.push(tab.repeat(indent) + "{");
                        }
                        indent++;
                        continue;
                    }

                    out.push(tab.repeat(indent) + trimmed);

                    if (trimmed.endsWith("{")) indent++;
                }

                const formatted = out.join("\n");
                if (formatted !== text.replace(/\r\n/g, "\n")) {
                    const full = new vscode.Range(doc.positionAt(0), doc.positionAt(text.length));
                    edits.push(vscode.TextEdit.replace(full, formatted));
                }
                return edits;
            },
        })
    );
}
