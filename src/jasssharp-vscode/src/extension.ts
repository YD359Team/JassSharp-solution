import * as vscode from "vscode";
import { spawn } from "child_process";
import * as path from "path";
import * as fs from "fs";

let log: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext) {
  log = vscode.window.createOutputChannel("JASS#");
  context.subscriptions.push(log);

  const diags = vscode.languages.createDiagnosticCollection("jasssharp");
  context.subscriptions.push(diags);

  const runBtn = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  runBtn.command = "jasssharp.runMap";
  runBtn.text = "$(play) Run Map";
  runBtn.tooltip = "JASS#: собрать и запустить карту";
  context.subscriptions.push(runBtn);

  const editBtn = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 99);
  editBtn.command = "jasssharp.openEditor";
  editBtn.text = "$(edit) Editor";
  editBtn.tooltip = "JASS#: открыть карту в World Editor";
  context.subscriptions.push(editBtn);

  const updateBtns = () => {
    const ed = vscode.window.activeTextEditor;
    if (ed?.document.languageId === "jasssharp") {
      runBtn.show();
      editBtn.show();
    } else {
      runBtn.hide();
      editBtn.hide();
    }
  };
  updateBtns();
  context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(updateBtns));

  context.subscriptions.push(
    vscode.commands.registerCommand("jasssharp.build", () => build("")),
    vscode.commands.registerCommand("jasssharp.buildVjass", () => build("vjass")),
    vscode.commands.registerCommand("jasssharp.buildLua", () => build("lua")),
    vscode.commands.registerCommand("jasssharp.buildMap", () => mapBuild(false)),
    vscode.commands.registerCommand("jasssharp.runMap", () => mapBuild(true)),
    vscode.commands.registerCommand("jasssharp.openEditor", openEditor),
    vscode.commands.registerCommand("jasssharp.packMap", packMap)
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

function info(msg: string) {
  log.appendLine(`[${new Date().toLocaleTimeString()}] ${msg}`);
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

function projectRoot(): string | undefined {
  return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
}

function readProject(): { map?: string; game?: string } | undefined {
  const root = projectRoot();
  if (!root) return;
  const cfgName = vscode.workspace.getConfiguration("jasssharp").get<string>("projectFile") || "jasssharp.json";
  const cfgPath = path.join(root, cfgName);
  if (!fs.existsSync(cfgPath)) return;
  try {
    return JSON.parse(fs.readFileSync(cfgPath, "utf8"));
  } catch {
    return;
  }
}

function mapPath(): string | undefined {
  const root = projectRoot();
  const proj = readProject();
  if (!root || !proj?.map) return;
  return path.isAbsolute(proj.map) ? proj.map : path.join(root, proj.map);
}

function findEditor(): string | undefined {
  const cfg = vscode.workspace.getConfiguration("jasssharp");
  const configured = cfg.get<string>("editorPath")?.trim();
  if (configured && fs.existsSync(configured)) return configured;

  const game = cfg.get<string>("gamePath")?.trim();
  if (game) {
    const dir = path.dirname(game);
    const candidates = [
      path.join(dir, "World Editor.exe"),
      path.join(dir, "worldedit.exe"),
      path.join(path.dirname(dir), "World Editor.exe"),
    ];
    for (const c of candidates) if (fs.existsSync(c)) return c;
  }
  return;
}

async function openEditor() {
  log.show(true);
  info("=== Open Editor ===");
  const map = mapPath();
  info(`map: ${map ?? "(not found)"}`);
  if (!map) {
    vscode.window.showErrorMessage("Не найдена карта в jasssharp.json");
    return;
  }
  const editor = findEditor();
  info(`editor: ${editor ?? "(not found)"}`);
  if (!editor) {
    vscode.window.showErrorMessage("Не найден World Editor.exe. Укажите jasssharp.editorPath");
    return;
  }
  const args = ["-launch", "-loadfile", map];
  info(`cmd: "${editor}" ${args.map((a) => `"${a}"`).join(" ")}`);
  const child = spawn(editor, args, { detached: true, stdio: "ignore" });
  child.on("error", (e) => info(`error: ${e.message}`));
  child.unref();
  info("started");
}

async function packMap() {
  log.show(true);
  info("=== Pack Map ===");
  const map = mapPath();
  info(`map: ${map ?? "(none)"}`);
  if (!map || !fs.existsSync(map) || !fs.statSync(map).isDirectory()) {
    vscode.window.showErrorMessage("Карта должна быть папкой");
    return;
  }
  const tool = vscode.workspace.getConfiguration("jasssharp").get<string>("mpqToolPath")?.trim();
  info(`tool: ${tool ?? "(not set)"}`);
  if (!tool || !fs.existsSync(tool)) {
    vscode.window.showErrorMessage("Укажите jasssharp.mpqToolPath к mpqtool.exe");
    return;
  }
  const out = map.replace(/[\\/]$/, "") + ".w3x";
  info(`out: ${out}`);
  await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: "JASS#: Pack Map" },
    () =>
      new Promise<void>((resolve) => {
        const child = spawn(tool, ["new", map, out], { shell: false });
        child.stdout.on("data", (d) => info(d.toString().trim()));
        child.stderr.on("data", (d) => info("err: " + d.toString().trim()));
        child.on("close", (code) => {
          info(`code: ${code}`);
          if (code === 0) vscode.window.showInformationMessage(`Упаковано: ${out}`);
          else vscode.window.showErrorMessage(`code ${code}`);
          resolve();
        });
        child.on("error", (e) => {
          info(`error: ${e.message}`);
          vscode.window.showErrorMessage(e.message);
          resolve();
        });
      })
  );
}

async function mapBuild(run: boolean) {
  log.show(true);
  info(`=== ${run ? "Run Map" : "Build Map"} ===`);
  const folder = vscode.workspace.workspaceFolders?.[0];
  if (!folder) {
    info("no workspace folder");
    vscode.window.showWarningMessage("Откройте папку проекта с jasssharp.json");
    return;
  }
  const root = folder.uri.fsPath;
  info(`project: ${root}`);
  const cfgName = vscode.workspace.getConfiguration("jasssharp").get<string>("projectFile") || "jasssharp.json";
  const cfgPath = path.join(root, cfgName);
  info(`config: ${cfgPath} exists=${fs.existsSync(cfgPath)}`);
  if (!fs.existsSync(cfgPath)) {
    vscode.window.showErrorMessage(`Нет ${cfgName} в корне рабочей папки`);
    return;
  }
  const args = ["map", "--project", root];
  if (run) args.push("--run");

  const game = vscode.workspace.getConfiguration("jasssharp").get<string>("gamePath")?.trim();
  const env = { ...process.env };
  if (game) {
    env.JASSSHARP_GAME = game;
    info(`gamePath: ${game} exists=${fs.existsSync(game)}`);
  } else {
    info("gamePath: (not set, from jasssharp.json)");
  }
  info(`cmd: ${compilerCmd()} ${args.join(" ")}`);
  info(`cwd: ${root}`);

  await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: run ? "JASS#: Run Map" : "JASS#: Build Map" },
    () =>
      new Promise<void>((resolve) => {
        const child = spawn(compilerCmd(), args, { cwd: root, shell: false, env });
        child.stdout.on("data", (d) => info(d.toString().trimEnd()));
        child.stderr.on("data", (d) => info("err: " + d.toString().trimEnd()));
        child.on("error", (e) => {
          info(`error: ${e.message}`);
          vscode.window.showErrorMessage(`Не удалось запустить компилятор. ${e.message}`);
          resolve();
        });
        child.on("close", (code) => {
          info(`exit: ${code}`);
          if (code === 0) {
            vscode.window.showInformationMessage("Done");
          } else {
            vscode.window.showErrorMessage(`code ${code}`);
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
