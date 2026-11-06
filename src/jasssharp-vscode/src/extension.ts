import * as vscode from "vscode";
import { spawn } from "child_process";
import * as path from "path";
import * as fs from "fs";

export function activate(context: vscode.ExtensionContext) {
  const diags = vscode.languages.createDiagnosticCollection("jasssharp");
  context.subscriptions.push(diags);

  const runBtn = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  runBtn.command = "jasssharp.runMap";
  runBtn.text = "$(play) Run Map";
  runBtn.tooltip = "JASS#: собрать и запустить карту";
  context.subscriptions.push(runBtn);

  const updateBtn = () => {
    const ed = vscode.window.activeTextEditor;
    if (ed?.document.languageId === "jasssharp") runBtn.show();
    else runBtn.hide();
  };
  updateBtn();
  context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(updateBtn));

  context.subscriptions.push(
    vscode.commands.registerCommand("jasssharp.build", () => build("")),
    vscode.commands.registerCommand("jasssharp.buildVjass", () => build("vjass")),
    vscode.commands.registerCommand("jasssharp.buildLua", () => build("lua")),
    vscode.commands.registerCommand("jasssharp.buildMap", () => mapBuild(false)),
    vscode.commands.registerCommand("jasssharp.runMap", () => mapBuild(true))
  );

  context.subscriptions.push(
    vscode.workspace.onDidOpenTextDocument((d) => check(d, diags)),
    vscode.workspace.onDidSaveTextDocument((d) => check(d, diags)),
    vscode.workspace.onDidChangeTextDocument((e) => {
      if (e.document.languageId === "jasssharp") debounce(() => check(e.document, diags));
    })
  );

  for (const d of vscode.workspace.textDocuments) check(d, diags);
}

function compilerCmd(): string {
  const cfg = vscode.workspace.getConfiguration("jasssharp");
  const configured = cfg.get<string>("compilerPath")?.trim() ?? "";
  return configured || "jasssharp";
}

function runCompiler(args: string[], cwd: string): Promise<{ code: number; out: string; err: string }> {
  return new Promise((resolve) => {
    const child = spawn(compilerCmd(), args, { cwd, shell: false });
    let out = "";
    let err = "";
    child.stdout.on("data", (d) => (out += d.toString()));
    child.stderr.on("data", (d) => (err += d.toString()));
    child.on("error", (e) => resolve({ code: -1, out: "", err: e.message }));
    child.on("close", (code) => resolve({ code: code ?? 1, out, err }));
  });
}

async function check(doc: vscode.TextDocument, diags: vscode.DiagnosticCollection) {
  if (doc.languageId !== "jasssharp" || doc.uri.scheme !== "file") return;
  if (doc.isDirty) await doc.save();
  const res = await runCompiler(["check", doc.fileName], path.dirname(doc.fileName));
  if (res.code === -1) {
    diags.set(doc.uri, [
      new vscode.Diagnostic(
        new vscode.Range(0, 0, 0, 1),
        `Не удалось запустить компилятор (${compilerCmd()}). Укажите jasssharp.compilerPath. ${res.err}`,
        vscode.DiagnosticSeverity.Error
      ),
    ]);
    return;
  }
  const list: vscode.Diagnostic[] = [];
  try {
    const raw = JSON.parse(res.out || "[]") as { Line: number; Col: number; Severity: string; Message: string }[];
    for (const d of raw) {
      const line = Math.max(0, (d.Line || 1) - 1);
      const col = Math.max(0, (d.Col || 1) - 1);
      const sev = d.Severity === "error" ? vscode.DiagnosticSeverity.Error : vscode.DiagnosticSeverity.Warning;
      list.push(new vscode.Diagnostic(new vscode.Range(line, col, line, col + 1), d.Message, sev));
    }
  } catch {
    if (res.err.trim()) {
      list.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), res.err.trim(), vscode.DiagnosticSeverity.Error));
    }
  }
  diags.set(doc.uri, list);
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
  const args = ["build", doc.fileName, "--target", target];

  await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: `JASS# → ${target}` },
    async () => {
      const res = await runCompiler(args, path.dirname(doc.fileName));
      if (res.code === -1) {
        vscode.window.showErrorMessage(
          `Не удалось запустить компилятор (${compilerCmd()}). Укажите jasssharp.compilerPath. ${res.err}`
        );
        return;
      }
      if (res.code === 0) {
        const files = res.out.trim().split(/\r?\n/).filter(Boolean);
        vscode.window.showInformationMessage(
          files.length ? `Собрано: ${files.map((f) => path.basename(f)).join(", ")}` : "Собрано"
        );
      } else {
        vscode.window.showErrorMessage(res.err.trim() || `компилятор завершился с кодом ${res.code}`);
      }
    }
  );
}

async function mapBuild(run: boolean) {
  const folder = vscode.workspace.workspaceFolders?.[0];
  if (!folder) {
    vscode.window.showWarningMessage("Откройте папку проекта с jasssharp.json");
    return;
  }
  const root = folder.uri.fsPath;
  const cfgName = vscode.workspace.getConfiguration("jasssharp").get<string>("projectFile") || "jasssharp.json";
  const cfgPath = path.join(root, cfgName);
  if (!fs.existsSync(cfgPath)) {
    vscode.window.showErrorMessage(`Нет ${cfgName} в корне рабочей папки`);
    return;
  }
  const args = ["map", "--project", root];
  if (run) args.push("--run");

  const game = vscode.workspace.getConfiguration("jasssharp").get<string>("gamePath")?.trim();
  const env = { ...process.env };
  if (game) env.JASSSHARP_GAME = game;

  await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: run ? "JASS#: Run Map" : "JASS#: Build Map" },
    () =>
      new Promise<void>((resolve) => {
        const child = spawn(compilerCmd(), args, { cwd: root, shell: false, env });
        let out = "";
        let err = "";
        child.stdout.on("data", (d) => (out += d.toString()));
        child.stderr.on("data", (d) => (err += d.toString()));
        child.on("error", (e) => {
          vscode.window.showErrorMessage(`Не удалось запустить компилятор. ${e.message}`);
          resolve();
        });
        child.on("close", (code) => {
          if (code === 0) {
            vscode.window.showInformationMessage(out.trim().split(/\r?\n/).filter(Boolean).join("\n") || "Готово");
          } else {
            vscode.window.showErrorMessage(err.trim() || `код ${code}`);
          }
          resolve();
        });
      })
  );
}

let timer: NodeJS.Timeout | undefined;
function debounce(fn: () => void) {
  if (timer) clearTimeout(timer);
  timer = setTimeout(fn, 400);
}

export function deactivate() {}
