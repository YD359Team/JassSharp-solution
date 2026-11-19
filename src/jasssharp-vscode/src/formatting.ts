import * as vscode from "vscode";
import { getAllmanBraceEdits } from "./braceFormatting";

export function registerFormatting(context: vscode.ExtensionContext): void {
    const selector: vscode.DocumentSelector = { language: "jasssharp" };
    context.subscriptions.push(
        vscode.languages.registerDocumentFormattingEditProvider(selector, {
            provideDocumentFormattingEdits(document) {
                const eol = document.eol === vscode.EndOfLine.CRLF ? "\r\n" : "\n";
                return getAllmanBraceEdits(document.getText(), eol).map(edit =>
                    vscode.TextEdit.replace(new vscode.Range(document.positionAt(edit.start), document.positionAt(edit.end)), edit.text));
            }
        }),
        vscode.languages.registerOnTypeFormattingEditProvider(selector, {
            provideOnTypeFormattingEdits(document, position) {
                const braceOffset = document.offsetAt(position) - 1;
                if (braceOffset < 0 || document.getText()[braceOffset] !== "{") return [];
                const eol = document.eol === vscode.EndOfLine.CRLF ? "\r\n" : "\n";
                const edit = getAllmanBraceEdits(document.getText(), eol).find(candidate => candidate.end === braceOffset);
                if (!edit) return [];
                return [vscode.TextEdit.replace(new vscode.Range(document.positionAt(edit.start), document.positionAt(edit.end)), edit.text)];
            }
        }, "{")
    );
}
