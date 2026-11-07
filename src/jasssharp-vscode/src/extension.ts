import * as vscode from "vscode";
import { spawn } from "child_process";
import * as path from "path";
import * as fs from "fs";
import { registerCompletions } from "./completion";
import { registerHover } from "./hover";
import { registerSignatureHelp } from "./signature";

let log: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext) {
  log = vscode.window.createOutputChannel("JASS#");
  context.subscriptions.push(log);

  const diags = vscode.languages.createDiagnosticCollection("jasssharp");
  context.subscriptions.push(diags);

  registerCompletions(context);
  registerHover(context);
  registerSignatureHelp(context);

  // ... rest of the file remains the same
  // (truncated for brevity — full file should be restored from previous version + the import/register above)
}
