import * as vscode from "vscode";
import { spawn } from "child_process";
import * as path from "path";

export function activate(context: vscode.ExtensionContext) {
  context.subscriptions.push(
    vscode.commands.registerCommand("jasssharp.build", () => build("")),
    vscode.commands.registerCommand("jasssharp.buildVjass", () => build("vjass")),
    vscode.commands.registerCommand("jasssharp.buildLua", () => build("lua"))
  );
}

async function build(forced: string) {
  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== "jasssharp") {
    vscode.window.showWarningMessage("Откройте файл .w3js");
    return;
  }
  const doc = editor.document;
  if (doc.isDirty) await doc.save();
  const cfg = vscode.workspace.getConfiguration("jasssharp");
  const target = forced || cfg.get<string>("defaultTarget") || "both";
  const configured = cfg.get<string>("compilerPath")?.trim() ?? "";
  const cmd = configured || "jasssharp";
  const args = ["build", doc.fileName, "--target", target];

  await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: `JASS# → ${target}` },
    () =>
      new Promise<void>((resolve) => {
        const child = spawn(cmd, args, { cwd: path.dirname(doc.fileName), shell: false });
        let out = "";
        let err = "";
        child.stdout.on("data", (d) => (out += d.toString()));
        child.stderr.on("data", (d) => (err += d.toString()));
        child.on("error", (e) => {
          vscode.window.showErrorMessage(
            `Не удалось запустить компилятор (${cmd}). Укажите jasssharp.compilerPath. ${e.message}`
          );
          resolve();
        });
        child.on("close", (code) => {
          if (code === 0) {
            const files = out.trim().split(/\r?\n/).filter(Boolean);
            vscode.window.showInformationMessage(
              files.length ? `Собрано: ${files.map((f) => path.basename(f)).join(", ")}` : "Собрано"
            );
          } else {
            vscode.window.showErrorMessage(err.trim() || `компилятор завершился с кодом ${code}`);
          }
          resolve();
        });
      })
  );
}

export function deactivate() {}
