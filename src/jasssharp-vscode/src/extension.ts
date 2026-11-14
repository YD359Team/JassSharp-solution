import * as vscode from "vscode";
import { spawn, ChildProcessWithoutNullStreams } from "child_process";
import * as path from "path";
import * as fs from "fs";
import { registerLanguageFeatures } from "./languageFeatures";

let log: vscode.OutputChannel;
const checkTimers = new Map<string, NodeJS.Timeout>();

export function activate(context: vscode.ExtensionContext) {
    log = vscode.window.createOutputChannel("JASS#");
    context.subscriptions.push(log);

    const diagnostics = vscode.languages.createDiagnosticCollection("jasssharp");
    context.subscriptions.push(diagnostics);

    registerLanguageFeatures(context);

    const runButton = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    runButton.command = "jasssharp.runMap";
    runButton.text = "$(play) Run Map";
    runButton.tooltip = "JASS#: собрать и запустить карту";
    context.subscriptions.push(runButton);

    const editorButton = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 99);
    editorButton.command = "jasssharp.openEditor";
    editorButton.text = "$(edit) Editor";
    editorButton.tooltip = "JASS#: открыть карту в World Editor";
    context.subscriptions.push(editorButton);

    const updateButtons = () => {
        const editor = vscode.window.activeTextEditor;
        if (editor?.document.languageId === "jasssharp") {
            runButton.show();
            editorButton.show();
        } else {
            runButton.hide();
            editorButton.hide();
        }
    };
    updateButtons();
    context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(updateButtons));

    context.subscriptions.push(
        vscode.commands.registerCommand("jasssharp.build", () => build("")),
        vscode.commands.registerCommand("jasssharp.buildLua", () => build("lua")),
        vscode.commands.registerCommand("jasssharp.buildMap", () => mapBuild(false)),
        vscode.commands.registerCommand("jasssharp.runMap", () => mapBuild(true)),
        vscode.commands.registerCommand("jasssharp.openEditor", openEditor),
        vscode.commands.registerCommand("jasssharp.packMap", packMap)
    );

    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(document => scheduleCheck(document, diagnostics)),
        vscode.workspace.onDidSaveTextDocument(document => scheduleCheck(document, diagnostics)),
        vscode.workspace.onDidChangeTextDocument(event => {
            if (event.document.languageId === "jasssharp" && event.document.isDirty)
                diagnostics.delete(event.document.uri); // The CLI checks saved source, not an unsaved editor buffer.
        })
    );

    for (const document of vscode.workspace.textDocuments) scheduleCheck(document, diagnostics);
}

function info(message: string): void {
    log.appendLine(`[${new Date().toLocaleTimeString()}] ${message}`);
}

function compilerPath(): string {
    const configured = vscode.workspace.getConfiguration("jasssharp").get<string>("compilerPath")?.trim().replace(/^"|"$/g, "") ?? "";
    return configured || "jasssharp";
}

function compilerInvocation(args: string[]): { command: string; args: string[] } {
    const executable = compilerPath();
    if (executable.toLowerCase().endsWith(".dll"))
        return { command: "dotnet", args: [executable, ...args] };
    return { command: executable, args };
}

function spawnCompiler(args: string[], options: { cwd?: string; env?: NodeJS.ProcessEnv } = {}): ChildProcessWithoutNullStreams {
    const invocation = compilerInvocation(args);
    return spawn(invocation.command, invocation.args, { cwd: options.cwd, env: options.env, shell: false });
}

function runCompiler(args: string[], cwd: string): Promise<{ code: number; out: string; err: string }> {
    return new Promise(resolve => {
        const child = spawnCompiler(args, { cwd });
        let stdout = "";
        let stderr = "";
        let settled = false;
        child.stdout.on("data", data => stdout += data.toString());
        child.stderr.on("data", data => stderr += data.toString());
        child.on("error", error => {
            if (settled) return;
            settled = true;
            resolve({ code: -1, out: stdout, err: error.message });
        });
        child.on("close", code => {
            if (settled) return;
            settled = true;
            resolve({ code: code ?? 1, out: stdout, err: stderr });
        });
    });
}

function projectConfigPath(document: vscode.TextDocument): string {
    const root = vscode.workspace.getWorkspaceFolder(document.uri)?.uri.fsPath ?? path.dirname(document.uri.fsPath);
    const configured = vscode.workspace.getConfiguration("jasssharp").get<string>("projectFile")?.trim() || "jasssharp.json";
    return path.isAbsolute(configured) ? configured : path.join(root, configured);
}

function scheduleCheck(document: vscode.TextDocument, diagnostics: vscode.DiagnosticCollection): void {
    if (document.languageId !== "jasssharp" || document.uri.scheme !== "file") return;
    const key = document.uri.toString();
    const existing = checkTimers.get(key);
    if (existing) clearTimeout(existing);
    const timer = setTimeout(() => {
        checkTimers.delete(key);
        void check(document, diagnostics);
    }, 250);
    checkTimers.set(key, timer);
}

async function check(document: vscode.TextDocument, diagnostics: vscode.DiagnosticCollection): Promise<void> {
    if (document.languageId !== "jasssharp" || document.uri.scheme !== "file" || document.isDirty) return;
    const version = document.version;
    const result = await runCompiler(
        ["check", document.fileName, "--config", projectConfigPath(document)],
        path.dirname(document.fileName));
    if (document.isClosed || document.version !== version || document.isDirty) return;

    if (result.code === -1) {
        diagnostics.set(document.uri, [new vscode.Diagnostic(
            new vscode.Range(0, 0, 0, 1),
            `Не удалось запустить компилятор (${compilerPath()}). Укажите jasssharp.compilerPath. ${result.err}`,
            vscode.DiagnosticSeverity.Error
        )]);
        return;
    }

    const items: vscode.Diagnostic[] = [];
    try {
        const raw = JSON.parse(result.out || "[]") as Array<{ Line: number; Col: number; Severity: string; Message: string }>;
        for (const diagnostic of raw) {
            const line = Math.min(document.lineCount - 1, Math.max(0, (diagnostic.Line || 1) - 1));
            const column = Math.min(document.lineAt(line).text.length, Math.max(0, (diagnostic.Col || 1) - 1));
            const severity = diagnostic.Severity === "error" ? vscode.DiagnosticSeverity.Error : vscode.DiagnosticSeverity.Warning;
            const end = Math.min(document.lineAt(line).text.length, column + 1);
            const range = new vscode.Range(line, column, line, Math.max(column, end));
            items.push(new vscode.Diagnostic(range, diagnostic.Message, severity));
        }
    } catch {
        const message = result.err.trim() || result.out.trim();
        if (message) items.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), message, vscode.DiagnosticSeverity.Error));
    }
    diagnostics.set(document.uri, items);
}

async function build(forcedTarget: string): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== "jasssharp") {
        vscode.window.showWarningMessage("Откройте файл .w3js");
        return;
    }
    const document = editor.document;
    if (document.isDirty && !(await document.save())) return;

    const target = forcedTarget || "lua";
    await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: `JASS# → ${target}` },
        async () => {
            const result = await runCompiler(
                ["build", document.fileName, "--target", target, "--config", projectConfigPath(document)],
                path.dirname(document.fileName));
            if (result.code === -1) {
                vscode.window.showErrorMessage(`Не удалось запустить компилятор (${compilerPath()}). Укажите jasssharp.compilerPath. ${result.err}`);
            } else if (result.code === 0) {
                const files = result.out.trim().split(/\r?\n/).filter(Boolean);
                vscode.window.showInformationMessage(files.length ? `Собрано: ${files.map(file => path.basename(file)).join(", ")}` : "Собрано");
            } else {
                vscode.window.showErrorMessage(result.err.trim() || `Компилятор завершился с кодом ${result.code}`);
            }
        }
    );
}

function projectRoot(): string | undefined {
    return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
}

function readProject(): { map?: string; game?: string } | undefined {
    const root = projectRoot();
    if (!root) return undefined;
    const fileName = vscode.workspace.getConfiguration("jasssharp").get<string>("projectFile") || "jasssharp.json";
    const filePath = path.isAbsolute(fileName) ? fileName : path.join(root, fileName);
    if (!fs.existsSync(filePath)) return undefined;
    try { return JSON.parse(fs.readFileSync(filePath, "utf8")) as { map?: string; game?: string }; }
    catch { return undefined; }
}

function mapPath(): string | undefined {
    const root = projectRoot();
    const project = readProject();
    if (!root || !project?.map) return undefined;
    return path.isAbsolute(project.map) ? project.map : path.join(root, project.map);
}

function findEditor(): string | undefined {
    const config = vscode.workspace.getConfiguration("jasssharp");
    const configured = config.get<string>("editorPath")?.trim();
    if (configured && fs.existsSync(configured)) return configured;

    const game = config.get<string>("gamePath")?.trim();
    if (game) {
        const directory = path.dirname(game);
        const candidates = [
            path.join(directory, "World Editor.exe"),
            path.join(directory, "worldedit.exe"),
            path.join(path.dirname(directory), "World Editor.exe")
        ];
        for (const candidate of candidates) if (fs.existsSync(candidate)) return candidate;
    }
    return undefined;
}

async function openEditor(): Promise<void> {
    log.show(true);
    info("=== Open Editor ===");
    const map = mapPath();
    info(`map: ${map ?? "(not found)"}`);
    if (!map) { vscode.window.showErrorMessage("Не найдена карта в jasssharp.json"); return; }
    const editor = findEditor();
    info(`editor: ${editor ?? "(not found)"}`);
    if (!editor) { vscode.window.showErrorMessage("Не найден World Editor.exe. Укажите jasssharp.editorPath"); return; }

    const args = ["-launch", "-loadfile", map];
    info(`cmd: \"${editor}\" ${args.map(arg => `\"${arg}\"`).join(" ")}`);
    const child = spawn(editor, args, { detached: true, stdio: "ignore" });
    child.on("error", error => info(`error: ${error.message}`));
    child.unref();
    info("started");
}

async function packMap(): Promise<void> {
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
    if (!tool || !fs.existsSync(tool)) { vscode.window.showErrorMessage("Укажите jasssharp.mpqToolPath к mpqtool.exe"); return; }

    const output = map.replace(/[\\/]$/, "") + ".w3x";
    info(`out: ${output}`);
    await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: "JASS#: Pack Map" },
        () => new Promise<void>(resolve => {
            const child = spawn(tool, ["new", map, output], { shell: false });
            child.stdout.on("data", data => info(data.toString().trim()));
            child.stderr.on("data", data => info(`err: ${data.toString().trim()}`));
            child.on("close", code => {
                info(`code: ${code}`);
                if (code === 0) vscode.window.showInformationMessage(`Упаковано: ${output}`);
                else vscode.window.showErrorMessage(`MPQ tool завершился с кодом ${code}`);
                resolve();
            });
            child.on("error", error => {
                info(`error: ${error.message}`);
                vscode.window.showErrorMessage(error.message);
                resolve();
            });
        })
    );
}

async function mapBuild(run: boolean): Promise<void> {
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
    const configName = vscode.workspace.getConfiguration("jasssharp").get<string>("projectFile") || "jasssharp.json";
    const configPath = path.isAbsolute(configName) ? configName : path.join(root, configName);
    info(`config: ${configPath} exists=${fs.existsSync(configPath)}`);
    if (!fs.existsSync(configPath)) { vscode.window.showErrorMessage(`Нет ${configName} в корне рабочей папки`); return; }

    const args = ["map", "--project", root, "--config", configName];
    if (run) args.push("--run");
    const config = vscode.workspace.getConfiguration("jasssharp");
    const game = config.get<string>("gamePath")?.trim();
    const mpq = config.get<string>("mpqToolPath")?.trim();
    const env: NodeJS.ProcessEnv = { ...process.env };
    if (game) { env.JASSSHARP_GAME = game; info(`gamePath: ${game} exists=${fs.existsSync(game)}`); }
    else info("gamePath: (not set, from jasssharp.json)");
    if (mpq) { env.JASSSHARP_MPQ = mpq; info(`mpqTool: ${mpq} exists=${fs.existsSync(mpq)}`); }
    else info("mpqTool: (not set)");
    info(`cmd: ${compilerPath()} ${args.join(" ")}`);
    info(`cwd: ${root}`);

    await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: run ? "JASS#: Run Map" : "JASS#: Build Map" },
        () => new Promise<void>(resolve => {
            const child = spawnCompiler(args, { cwd: root, env });
            child.stdout.on("data", data => info(data.toString().trimEnd()));
            child.stderr.on("data", data => info(`err: ${data.toString().trimEnd()}`));
            child.on("error", error => {
                info(`error: ${error.message}`);
                vscode.window.showErrorMessage(`Не удалось запустить компилятор. ${error.message}`);
                resolve();
            });
            child.on("close", code => {
                info(`exit: ${code}`);
                if (code === 0) vscode.window.showInformationMessage("Done");
                else vscode.window.showErrorMessage(`Компилятор завершился с кодом ${code}`);
                resolve();
            });
        })
    );
}

export function deactivate(): void {
    for (const timer of checkTimers.values()) clearTimeout(timer);
    checkTimers.clear();
}
