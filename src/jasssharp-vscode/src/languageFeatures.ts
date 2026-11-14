import * as vscode from "vscode";
import { execFile } from "child_process";
import * as path from "path";
import { MEMBERS } from "./members";
import { CONSTANTS } from "./constants";
import { NATIVES } from "./completion";

interface NamespaceSymbols {
    classes: string[];
    functions: string[];
    consts: string[];
    fields: string[];
}

interface SymbolsResult {
    namespaces: Record<string, NamespaceSymbols>;
    currentImports: string[];
}

interface FunctionInfo {
    name: string;
    returnType: string;
    parameters: string[];
    signature: string;
    offset: number;
    isMethod: boolean;
    bodyStart?: number;
    bodyEnd?: number;
}

interface VariableInfo {
    name: string;
    type: string;
    offset: number;
    isParameter: boolean;
    scopeStart?: number;
    scopeEnd?: number;
}

interface ClassInfo {
    name: string;
    start: number;
    bodyStart: number;
    bodyEnd: number;
}

interface LexToken {
    kind: "identifier" | "number" | "string" | "comment" | "punctuation";
    text: string;
    start: number;
    end: number;
}

const KEYWORDS = new Set([
    "namespace", "using", "class", "enum", "const", "public", "private", "static",
    "void", "int", "integer", "float", "real", "bool", "boolean", "string", "code",
    "new", "this", "if", "else", "while", "for", "return", "break", "continue",
    "true", "false", "null", "var", "in", "is", "as", "get", "set"
]);

const PRIMITIVE_TYPES = new Set(["void", "int", "integer", "float", "real", "bool", "boolean", "string", "code"]);
const DECLARATION_STOPWORDS = new Set(["namespace", "using", "class", "enum", "const", "public", "private", "static", "new", "this", "if", "else", "while", "for", "return", "break", "continue", "true", "false", "null", "var", "in", "is", "as", "get", "set"]);

const BUILTIN_TYPES = new Set((`
ability abilitybooleanfield abilitybooleanlevelarrayfield abilitybooleanlevelfield abilityintegerfield
abilityintegerlevelarrayfield abilityintegerlevelfield abilityrealfield abilityreallevelarrayfield
abilityreallevelfield abilitystringfield abilitystringlevelarrayfield abilitystringlevelfield agent aidifficulty
alliancetype animtype armortype attacktype boolexpr buff button camerafield camerasetup code commandbuttoneffect
conditionfunc damagetype defeatcondition defensetype destructable dialog dialogevent effect effecttype event eventid
fgamestate filterfunc fogmodifier fogstate fogstyle force frameeventtype framehandle framepointtype gamecache
 gamedifficulty gameevent gamespeed gamestate gametype group handle hashtable heroattribute igamestate image integer
item itembooleanfield itemintegerfield itempool itemrealfield itemstringfield itemtag itemtype leaderboard lightning
limitop loadoutslot location mapcontrol mapdensity mapflag mapsetting mapvisibility metakeytype minimapicon
mousebuttontype movetype multiboard multiboarditem nothing originframetype oskeytype pathingflag pathingtype
placement player playercolor playergameresult playerevent playerscore playerslotstate playerstate playerunitevent
quest questitem race racepreference raritycontrol real rect regentype region sound soundtype startlocprio string
subanimtype targetflag terraindeformation texmapflags textaligntype texttag timerdialog timer trackable trigger
triggeraction triggercondition ubersplat unit unitbooleanfield unitcategory unitevent unitintegerfield unitpool
unitrealfield unitstate unitstringfield unittype unitweaponbooleanfield unitweaponintegerfield unitweaponrealfield
unitweaponstringfield version volumegroup weapontype weathereffect widget widgetevent
`).trim().split(/\s+/));

const nativeNames = new Set(NATIVES.map(n => n.name));
const constantNames = new Set(CONSTANTS);
const nativeByName = new Map(NATIVES.map(n => [n.name, n]));
const symbolCache = new Map<string, { expiresAt: number; value: SymbolsResult | null }>();
const symbolPending = new Map<string, Promise<SymbolsResult | null>>();

function compilerInvocation(args: string[]): { command: string; args: string[] } {
    const configured = vscode.workspace.getConfiguration("jasssharp").get<string>("compilerPath")?.trim().replace(/^"|"$/g, "") ?? "";
    const executable = configured || "jasssharp";
    if (executable.toLowerCase().endsWith(".dll"))
        return { command: "dotnet", args: [executable, ...args] };
    return { command: executable, args };
}

function projectDirectory(document: vscode.TextDocument): string {
    return vscode.workspace.getWorkspaceFolder(document.uri)?.uri.fsPath ?? path.dirname(document.uri.fsPath);
}

function fetchProjectSymbols(document: vscode.TextDocument): Promise<SymbolsResult | null> {
    if (document.uri.scheme !== "file") return Promise.resolve(null);

    const root = projectDirectory(document);
    const invocation = compilerInvocation(["symbols", document.uri.fsPath, "--project", root]);
    const cacheKey = `${invocation.command}\0${invocation.args.join("\0")}`;
    const cached = symbolCache.get(cacheKey);
    if (cached && cached.expiresAt > Date.now()) return Promise.resolve(cached.value);

    const pending = symbolPending.get(cacheKey);
    if (pending) return pending;

    const request = new Promise<SymbolsResult | null>(resolve => {
        execFile(invocation.command, invocation.args, {
            cwd: root,
            encoding: "utf8",
            timeout: 3500,
            maxBuffer: 2 * 1024 * 1024,
            windowsHide: true
        }, (error, stdout) => {
            let value: SymbolsResult | null = null;
            if (!error) {
                try {
                    const parsed = JSON.parse(stdout) as SymbolsResult;
                    if (parsed && typeof parsed === "object" && parsed.namespaces && Array.isArray(parsed.currentImports))
                        value = parsed;
                } catch {
                    value = null;
                }
            }

            const fallback = symbolCache.get(cacheKey)?.value ?? null;
            const result = value ?? fallback;
            symbolCache.set(cacheKey, { expiresAt: Date.now() + (value ? 1800 : 700), value: result });
            symbolPending.delete(cacheKey);
            resolve(result);
        });
    });

    symbolPending.set(cacheKey, request);
    return request;
}

function splitParameters(text: string): string[] {
    const result: string[] = [];
    let start = 0;
    let depth = 0;
    let inString = false;
    let escaped = false;

    for (let i = 0; i < text.length; i++) {
        const ch = text[i];
        if (inString) {
            if (escaped) escaped = false;
            else if (ch === "\\") escaped = true;
            else if (ch === '"') inString = false;
            continue;
        }
        if (ch === '"') { inString = true; continue; }
        if (ch === "<" || ch === "(") depth++;
        else if (ch === ">" || ch === ")") depth = Math.max(0, depth - 1);
        else if (ch === "," && depth === 0) {
            const part = text.slice(start, i).trim();
            if (part) result.push(part);
            start = i + 1;
        }
    }

    const last = text.slice(start).trim();
    if (last) result.push(last);
    return result;
}

function findMatchingBrace(text: string, openOffset: number): number {
    let depth = 0;
    let state: "normal" | "string" | "char" | "lineComment" | "blockComment" = "normal";
    let escaped = false;

    for (let i = openOffset; i < text.length; i++) {
        const ch = text[i];
        const next = text[i + 1];

        if (state === "lineComment") {
            if (ch === "\n") state = "normal";
            continue;
        }
        if (state === "blockComment") {
            if (ch === "*" && next === "/") { state = "normal"; i++; }
            continue;
        }
        if (state === "string" || state === "char") {
            if (escaped) escaped = false;
            else if (ch === "\\") escaped = true;
            else if ((state === "string" && ch === '"') || (state === "char" && ch === "'")) state = "normal";
            continue;
        }

        if (ch === "/" && next === "/") { state = "lineComment"; i++; continue; }
        if (ch === "/" && next === "*") { state = "blockComment"; i++; continue; }
        if (ch === '"') { state = "string"; continue; }
        if (ch === "'") { state = "char"; continue; }
        if (ch === "{") depth++;
        else if (ch === "}" && --depth === 0) return i;
    }

    return text.length;
}

function maskNonCode(text: string): string {
    const chars = text.split("");
    for (const token of tokenize(text)) {
        if (token.kind !== "comment" && token.kind !== "string") continue;
        for (let i = token.start; i < token.end; i++) {
            if (chars[i] !== "\n" && chars[i] !== "\r") chars[i] = " ";
        }
    }
    return chars.join("");
}

function readClasses(text: string): ClassInfo[] {
    const result: ClassInfo[] = [];
    const source = maskNonCode(text);
    const re = /\bclass\s+([A-Za-z_]\w*)[^\{]*\{/g;
    for (const match of source.matchAll(re)) {
        const open = (match.index ?? 0) + match[0].lastIndexOf("{");
        result.push({
            name: match[1],
            start: match.index ?? 0,
            bodyStart: open + 1,
            bodyEnd: findMatchingBrace(text, open)
        });
    }
    return result;
}

function readFunctions(text: string, classes = readClasses(text)): FunctionInfo[] {
    const result: FunctionInfo[] = [];
    const source = maskNonCode(text);
    const re = /\b([A-Za-z_]\w*(?:<[^>]+>)?)\s+([A-Za-z_]\w*)\s*\(([^()]*)\)\s*(?=\{|;)/g;

    for (const match of source.matchAll(re)) {
        const start = match.index ?? 0;
        const returnType = match[1];
        const name = match[2];
        if (DECLARATION_STOPWORDS.has(returnType) || name === "if" || name === "while" || name === "for" || name === "switch") continue;
        const nameOffset = start + match[0].lastIndexOf(name);
        const parameters = splitParameters(match[3]);
        const owner = classes.find(c => nameOffset >= c.bodyStart && nameOffset <= c.bodyEnd);
        const signatureEnd = start + match[0].length;
        const openingBrace = source.indexOf("{", signatureEnd);
        const hasBody = openingBrace >= 0 && source.slice(signatureEnd, openingBrace).trim() === "";
        result.push({
            name,
            returnType,
            parameters,
            signature: `${returnType} ${name}(${parameters.join(", ")})`,
            offset: nameOffset,
            isMethod: !!owner,
            bodyStart: hasBody ? openingBrace + 1 : undefined,
            bodyEnd: hasBody ? findMatchingBrace(text, openingBrace) : undefined
        });
    }

    return result;
}

function readVariables(text: string, classes = readClasses(text), functions = readFunctions(text, classes)): VariableInfo[] {
    const result: VariableInfo[] = [];
    const source = maskNonCode(text);
    const classNames = new Set(classes.map(c => c.name));
    const typePattern = /\b([A-Za-z_]\w*)\s+([A-Za-z_]\w*)\s*(?==|;|,|\))/g;

    for (const match of source.matchAll(typePattern)) {
        const type = match[1];
        const name = match[2];
        const offset = (match.index ?? 0) + match[0].lastIndexOf(name);
        if (DECLARATION_STOPWORDS.has(type) || name === "this" || name === "true" || name === "false") continue;
        if (!PRIMITIVE_TYPES.has(type.toLowerCase()) && !BUILTIN_TYPES.has(type.toLowerCase()) && !classNames.has(type) && !/^[A-Z]/.test(type)) continue;
        result.push({ name, type, offset, isParameter: false });
    }

    const parameterVariables: VariableInfo[] = [];
    for (const fn of functions) {
        if (fn.bodyStart === undefined || fn.bodyEnd === undefined) continue;
        for (const parameter of fn.parameters) {
            const match = /^\s*([A-Za-z_]\w*)\s+([A-Za-z_]\w*)/.exec(parameter);
            if (!match) continue;
            const fnText = text.slice(fn.offset);
            const paren = fnText.indexOf("(");
            const parameterTextOffset = fnText.slice(paren + 1).indexOf(parameter);
            if (paren < 0 || parameterTextOffset < 0) continue;
            const paramOffset = fn.offset + paren + 1 + parameterTextOffset + parameter.indexOf(match[2]);
            parameterVariables.push({
                name: match[2], type: match[1], offset: paramOffset, isParameter: true,
                scopeStart: fn.bodyStart, scopeEnd: fn.bodyEnd
            });
        }
    }

    const parameterOffsets = new Set(parameterVariables.map(variable => variable.offset));
    for (let i = result.length - 1; i >= 0; i--) {
        if (parameterOffsets.has(result[i].offset)) result.splice(i, 1);
    }
    for (const variable of result) {
        if (parameterOffsets.has(variable.offset)) continue;
        const ownerFunction = functions.find(fn => fn.bodyStart !== undefined &&
            fn.bodyEnd !== undefined && variable.offset >= fn.bodyStart && variable.offset <= fn.bodyEnd);
        const ownerClass = classes.find(cls => variable.offset >= cls.bodyStart && variable.offset <= cls.bodyEnd);
        if (ownerFunction) {
            variable.scopeStart = ownerFunction.bodyStart;
            variable.scopeEnd = ownerFunction.bodyEnd;
        } else if (ownerClass) {
            variable.scopeStart = ownerClass.bodyStart;
            variable.scopeEnd = ownerClass.bodyEnd;
        }
    }

    const varDeclarations = Array.from(source.matchAll(/\bvar\s+([A-Za-z_]\w*)\s*=\s*([^;\r\n]+)/g))
        .map(match => ({
            name: match[1],
            initializer: match[2].trim(),
            offset: (match.index ?? 0) + match[0].indexOf(match[1])
        }))
        .sort((a, b) => a.offset - b.offset);

    const priorDeclarations = [...result.filter(variable => !parameterOffsets.has(variable.offset)), ...parameterVariables]
        .sort((a, b) => a.offset - b.offset);

    for (const declaration of varDeclarations) {
        const ownerFunction = functions.find(fn => fn.bodyStart !== undefined &&
            fn.bodyEnd !== undefined && declaration.offset >= fn.bodyStart && declaration.offset <= fn.bodyEnd);
        const ownerClass = classes.find(cls => declaration.offset >= cls.bodyStart && declaration.offset <= cls.bodyEnd);
        const scopeStart = ownerFunction?.bodyStart ?? ownerClass?.bodyStart;
        const scopeEnd = ownerFunction?.bodyEnd ?? ownerClass?.bodyEnd;
        const visibleDeclarations = priorDeclarations.filter(prior => {
            if (prior.offset >= declaration.offset) return false;
            if (prior.scopeStart === undefined) return true;
            if (ownerFunction && prior.scopeStart === ownerFunction.bodyStart) return true;
            return !!ownerClass && prior.scopeStart === ownerClass.bodyStart && prior.scopeEnd === ownerClass.bodyEnd;
        });
        const knownTypes = new Map<string, string>();
        for (const prior of visibleDeclarations) knownTypes.set(prior.name, prior.type);

        const initializer = declaration.initializer;
        let inferred: string | undefined;
        const creation = /^new\s+([A-Za-z_]\w*)/.exec(initializer);
        if (creation) inferred = creation[1];
        else if (/^(?:true|false)$/.test(initializer)) inferred = "bool";
        else if (/^null$/.test(initializer)) inferred = undefined;
        else if (/^["']/.test(initializer)) inferred = "string";
        else if (/^\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(initializer))
            inferred = /[.eE]/.test(initializer) ? "float" : "int";
        else {
            const identifier = /^([A-Za-z_]\w*)$/.exec(initializer);
            if (identifier) inferred = knownTypes.get(identifier[1]);

            if (!inferred) {
                const call = /^([A-Za-z_]\w*)\s*\(/.exec(initializer);
                if (call) inferred = functions.find(fn => fn.name === call[1])?.returnType ?? nativeReturnType(call[1]);
            }

            if (!inferred && /[+*/%-]/.test(initializer)) {
                const operands = initializer.split(/[+*/%-]/).map(part => part.trim()).filter(Boolean);
                const operandTypes = operands.map(part => {
                    if (/^\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(part))
                        return /[.eE]/.test(part) ? "float" : "int";
                    return knownTypes.get(part);
                });
                if (operandTypes.length && operandTypes.every(Boolean) && operandTypes.some(type => type === "float"))
                    inferred = "float";
                else if (operandTypes.length && operandTypes.every(type => type === "int"))
                    inferred = "int";
            }
        }

        if (inferred) {
            result.push({
                name: declaration.name, type: inferred, offset: declaration.offset, isParameter: false,
                scopeStart, scopeEnd
            });
        }
    }

    result.push(...parameterVariables);

    // The latest declaration wins for a variable name in a simple lexical scan.
    result.sort((a, b) => a.offset - b.offset);
    return result;
}

function readClassMembers(text: string, classInfo: ClassInfo): Array<{ name: string; type: string; kind: "Method" | "Property"; parameters: string[] }> {
    const body = maskNonCode(text.slice(classInfo.bodyStart, classInfo.bodyEnd));
    const depthAt = new Int32Array(body.length);
    let depth = 0;
    for (let i = 0; i < body.length; i++) {
        depthAt[i] = depth;
        if (body[i] === "{") depth++;
        else if (body[i] === "}") depth = Math.max(0, depth - 1);
    }

    const result: Array<{ name: string; type: string; kind: "Method" | "Property"; parameters: string[] }> = [];
    const re = /\b(?:public\s+|private\s+|static\s+|const\s+)*([A-Za-z_]\w*)\s+([A-Za-z_]\w*)\s*(\(([^()]*)\))?/g;
    for (const match of body.matchAll(re)) {
        const matchOffset = match.index ?? 0;
        if (depthAt[matchOffset] !== 0 || DECLARATION_STOPWORDS.has(match[1])) continue;
        result.push({
            name: match[2],
            type: match[1],
            kind: match[3] ? "Method" : "Property",
            parameters: match[4] ? splitParameters(match[4]) : []
        });
    }
    return result;
}

function getCurrentClass(text: string, offset: number): ClassInfo | undefined {
    return readClasses(text).filter(c => c.start < offset && offset <= c.bodyEnd).sort((a, b) => b.start - a.start)[0];
}

function visibleVariables(text: string, offset: number): VariableInfo[] {
    const classes = readClasses(text);
    const functions = readFunctions(text, classes);
    const currentFunction = functions
        .filter(fn => fn.bodyStart !== undefined && fn.bodyEnd !== undefined &&
            fn.bodyStart <= offset && offset <= fn.bodyEnd)
        .sort((a, b) => b.offset - a.offset)[0];
    const currentClass = classes
        .filter(cls => cls.bodyStart <= offset && offset <= cls.bodyEnd)
        .sort((a, b) => b.start - a.start)[0];

    return readVariables(text, classes, functions).filter(variable => {
        if (variable.offset >= offset) return false;
        if (variable.scopeStart === undefined) return true;
        if (currentFunction && variable.scopeStart === currentFunction.bodyStart) return true;
        return !!currentClass && variable.scopeStart === currentClass.bodyStart &&
            variable.scopeEnd === currentClass.bodyEnd;
    });
}

function latestVariableType(name: string, text: string, offset: number): string | undefined {
    const variables = visibleVariables(text, offset).filter(variable => variable.name === name);
    return variables.length ? variables[variables.length - 1].type : undefined;
}

function inferExpressionType(expression: string, documentText: string, offset: number): string | undefined {
    const expr = expression.trim().replace(/^\(+|\)+$/g, "");
    if (!expr) return undefined;
    if (expr === "this") return getCurrentClass(documentText, offset)?.name;
    if (/^new\s+([A-Za-z_]\w*)/.test(expr)) return /^new\s+([A-Za-z_]\w*)/.exec(expr)?.[1];
    if (/^\d+\.\d/.test(expr)) return "float";
    if (/^\d/.test(expr)) return "int";
    if (/^"/.test(expr)) return "string";
    if (/^(true|false)$/.test(expr)) return "bool";

    const segments = expr.split(".");
    let type = latestVariableType(segments[0], documentText, offset);
    if (!type && (BUILTIN_TYPES.has(segments[0].toLowerCase()) || readClasses(documentText).some(c => c.name === segments[0])))
        type = segments[0];
    if (!type && segments[0] === "this") type = getCurrentClass(documentText, offset)?.name;
    if (!type) {
        const callName = /^([A-Za-z_]\w*)\s*\(/.exec(segments[0])?.[1];
        if (callName) type = readFunctions(documentText).find(f => f.name === callName)?.returnType ?? nativeReturnType(callName);
    }

    for (let i = 1; i < segments.length && type; i++) {
        const memberName = segments[i].replace(/\(.*$/, "");
        const nativeMember = MEMBERS.find(m => m.type === type && m.name === memberName);
        if (nativeMember) {
            type = nativeMember.ret;
            continue;
        }
        const classInfo = readClasses(documentText).find(c => c.name === type);
        const customMember = classInfo && readClassMembers(documentText, classInfo).find(m => m.name === memberName);
        type = customMember?.type;
    }
    return type;
}

function nativeReturnType(name: string): string | undefined {
    const native = nativeByName.get(name);
    if (!native) return undefined;
    const arrow = /→\s*([A-Za-z_]\w*)/.exec(native.sig);
    return arrow?.[1];
}

function signatureFromNative(name: string): string | undefined {
    const native = nativeByName.get(name);
    if (!native) return undefined;
    return native.sig;
}

function signatureFromFunction(fn: FunctionInfo): string {
    return fn.signature;
}

function addCompletion(items: vscode.CompletionItem[], seen: Set<string>, name: string, kind: vscode.CompletionItemKind,
    detail: string, range: vscode.Range, sortPrefix: string, insertText?: vscode.SnippetString, documentation?: string): void {
    if (seen.has(name)) return;
    seen.add(name);
    const item = new vscode.CompletionItem(name, kind);
    item.range = range;
    item.detail = detail;
    item.sortText = `${sortPrefix}_${name}`;
    item.filterText = name;
    if (insertText) item.insertText = insertText;
    if (documentation) item.documentation = new vscode.MarkdownString(documentation);
    items.push(item);
}

function isInsideCommentOrString(text: string, offset: number): boolean {
    let state: "normal" | "string" | "char" | "lineComment" | "blockComment" = "normal";
    let escaped = false;
    for (let i = 0; i < offset && i < text.length; i++) {
        const ch = text[i], next = text[i + 1];
        if (state === "lineComment") { if (ch === "\n") state = "normal"; continue; }
        if (state === "blockComment") { if (ch === "*" && next === "/") { state = "normal"; i++; } continue; }
        if (state === "string" || state === "char") {
            if (escaped) escaped = false;
            else if (ch === "\\") escaped = true;
            else if ((state === "string" && ch === '"') || (state === "char" && ch === "'")) state = "normal";
            continue;
        }
        if (ch === "/" && next === "/") { state = "lineComment"; i++; }
        else if (ch === "/" && next === "*") { state = "blockComment"; i++; }
        else if (ch === '"') state = "string";
        else if (ch === "'") state = "char";
    }
    return state !== "normal";
}

function parameterNames(parameters: string[]): string[] {
    return parameters.map((p, index) => {
        const match = /([A-Za-z_]\w*)\s*$/.exec(p);
        return match?.[1] ?? `arg${index + 1}`;
    });
}

function parseOpenCall(text: string, offset: number): { name: string; argumentIndex: number } | undefined {
    const stack: Array<{ start: number; commas: number }> = [];
    let state: "normal" | "string" | "char" | "lineComment" | "blockComment" = "normal";
    let escaped = false;

    for (let i = 0; i < offset; i++) {
        const ch = text[i], next = text[i + 1];
        if (state === "lineComment") { if (ch === "\n") state = "normal"; continue; }
        if (state === "blockComment") { if (ch === "*" && next === "/") { state = "normal"; i++; } continue; }
        if (state === "string" || state === "char") {
            if (escaped) escaped = false;
            else if (ch === "\\") escaped = true;
            else if ((state === "string" && ch === '"') || (state === "char" && ch === "'")) state = "normal";
            continue;
        }
        if (ch === "/" && next === "/") { state = "lineComment"; i++; continue; }
        if (ch === "/" && next === "*") { state = "blockComment"; i++; continue; }
        if (ch === '"') { state = "string"; continue; }
        if (ch === "'") { state = "char"; continue; }
        if (ch === "(") stack.push({ start: i, commas: 0 });
        else if (ch === ")") stack.pop();
        else if (ch === "," && stack.length) stack[stack.length - 1].commas++;
    }

    const open = stack[stack.length - 1];
    if (!open) return undefined;
    const before = text.slice(0, open.start).trimEnd();
    const match = /([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)$/.exec(before);
    if (!match) return undefined;
    return { name: match[1].split(".").pop()!, argumentIndex: open.commas };
}

function buildFunctionSignatures(document: vscode.TextDocument): Map<string, FunctionInfo> {
    const classes = readClasses(document.getText());
    return new Map(readFunctions(document.getText(), classes).map(f => [f.name, f]));
}

export function registerLanguageFeatures(context: vscode.ExtensionContext): void {
    const completionProvider: vscode.CompletionItemProvider = {
        async provideCompletionItems(document, position, _token, _context) {
            const fullText = document.getText();
            const offset = document.offsetAt(position);
            if (isInsideCommentOrString(fullText, offset)) return [];

            const range = document.getWordRangeAtPosition(position) ?? new vscode.Range(position, position);
            const beforeCursor = document.lineAt(position.line).text.slice(0, position.character);
            const dotMatch = /([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\.([A-Za-z_]\w*)?$/.exec(beforeCursor);
            const items: vscode.CompletionItem[] = [];
            const seen = new Set<string>();

            if (dotMatch) {
                const receiver = dotMatch[1];
                const receiverType = inferExpressionType(receiver, fullText, offset);
                if (!receiverType) return [];

                const classInfo = readClasses(fullText).find(c => c.name === receiverType);
                const custom = classInfo ? readClassMembers(fullText, classInfo) : [];
                for (const member of MEMBERS.filter(m => m.type === receiverType)) {
                    const isMethod = member.kind === "Method";
                    addCompletion(items, seen, member.name,
                        isMethod ? vscode.CompletionItemKind.Method : vscode.CompletionItemKind.Property,
                        `${receiverType}.${member.name} : ${member.ret}${isMethod ? " (method)" : ""}`,
                        range, "00", isMethod ? new vscode.SnippetString(`${member.name}($0)`) : undefined);
                }
                for (const member of custom) {
                    const isMethod = member.kind === "Method";
                    addCompletion(items, seen, member.name,
                        isMethod ? vscode.CompletionItemKind.Method : vscode.CompletionItemKind.Property,
                        `${member.name} : ${member.type}${isMethod ? " (method)" : ""}`,
                        range, "01", isMethod ? new vscode.SnippetString(`${member.name}($0)`) : undefined);
                }
                return items;
            }

            const classes = readClasses(fullText);
            const functions = readFunctions(fullText, classes);
            const variables = visibleVariables(fullText, offset);
            for (const fn of functions) {
                const info = `${fn.signature} : ${fn.returnType}`;
                addCompletion(items, seen, fn.name,
                    fn.isMethod ? vscode.CompletionItemKind.Method : vscode.CompletionItemKind.Function,
                    info, range, "00", new vscode.SnippetString(`${fn.name}($0)`));
            }

            const projectSymbols = await fetchProjectSymbols(document);
            if (projectSymbols) {
                for (const namespace of projectSymbols.currentImports) {
                    const ns = projectSymbols.namespaces[namespace];
                    if (!ns) continue;
                    for (const name of ns.classes)
                        addCompletion(items, seen, name, vscode.CompletionItemKind.Class, `${namespace} class`, range, "02");
                    for (const name of ns.functions)
                        addCompletion(items, seen, name, vscode.CompletionItemKind.Function, `${namespace} function`, range, "02", new vscode.SnippetString(`${name}($0)`));
                    for (const name of ns.consts)
                        addCompletion(items, seen, name, vscode.CompletionItemKind.Constant, `${namespace} const`, range, "02");
                    for (const name of ns.fields)
                        addCompletion(items, seen, name, vscode.CompletionItemKind.Field, `${namespace} field`, range, "03");
                }
            }

            for (const variable of variables.slice().reverse()) {
                addCompletion(items, seen, variable.name, vscode.CompletionItemKind.Variable,
                    `${variable.type} ${variable.name}${variable.isParameter ? " (parameter)" : ""}`,
                    range, "00");
            }
            for (const cls of classes)
                addCompletion(items, seen, cls.name, vscode.CompletionItemKind.Class, "JASS# class", range, "01");

            for (const native of NATIVES)
                addCompletion(items, seen, native.name, vscode.CompletionItemKind.Function,
                    native.sig, range, "04", new vscode.SnippetString(`${native.name}($0)`), native.doc);
            for (const constant of CONSTANTS)
                addCompletion(items, seen, constant, vscode.CompletionItemKind.Constant, "Warcraft III constant", range, "05");
            for (const type of BUILTIN_TYPES)
                addCompletion(items, seen, type, vscode.CompletionItemKind.Class, "Warcraft III handle / enum type", range, "06");
            for (const keyword of KEYWORDS)
                addCompletion(items, seen, keyword, vscode.CompletionItemKind.Keyword, "JASS# keyword", range, "09");

            return items;
        }
    };

    context.subscriptions.push(
        vscode.languages.registerCompletionItemProvider({ language: "jasssharp" }, completionProvider, ".")
    );

    const signatureProvider: vscode.SignatureHelpProvider = {
        provideSignatureHelp(document, position) {
            const text = document.getText();
            const call = parseOpenCall(text, document.offsetAt(position));
            if (!call) return undefined;

            const functions = buildFunctionSignatures(document);
            const functionInfo = functions.get(call.name);
            const native = nativeByName.get(call.name);
            const member = MEMBERS.find(m => m.name === call.name && m.kind === "Method");
            let label: string;
            let parameters: string[];
            let documentation = "";

            if (functionInfo) {
                label = signatureFromFunction(functionInfo);
                parameters = functionInfo.parameters;
            } else if (native) {
                label = native.sig;
                parameters = splitParameters(native.sig.slice(native.sig.indexOf("(") + 1, native.sig.lastIndexOf(")")));
                documentation = native.doc;
            } else if (member) {
                label = `${member.name}(...) : ${member.ret}`;
                parameters = [];
            } else {
                return undefined;
            }

            const help = new vscode.SignatureHelp();
            const signature = new vscode.SignatureInformation(label, documentation);
            signature.parameters = parameters.map(p => new vscode.ParameterInformation(p));
            help.signatures = [signature];
            help.activeSignature = 0;
            help.activeParameter = parameters.length ? Math.min(call.argumentIndex, parameters.length - 1) : 0;
            return help;
        }
    };
    context.subscriptions.push(vscode.languages.registerSignatureHelpProvider({ language: "jasssharp" }, signatureProvider, "(", ","));

    const hoverProvider: vscode.HoverProvider = {
        provideHover(document, position) {
            const range = document.getWordRangeAtPosition(position);
            if (!range) return undefined;
            const word = document.getText(range);
            const native = nativeByName.get(word);
            const functionInfo = buildFunctionSignatures(document).get(word);
            const constant = constantNames.has(word);
            const member = MEMBERS.find(m => m.name === word);
            const documentText = document.getText();
            const cls = readClasses(documentText).find(c => c.name === word);
            const variables = visibleVariables(documentText, document.offsetAt(position));
            const variable = variables.filter(v => v.name === word).slice(-1)[0];

            let code: string | undefined;
            let description = "";
            if (native) { code = native.sig; description = native.doc; }
            else if (functionInfo) { code = functionInfo.signature; description = `Возвращаемый тип: ${functionInfo.returnType}`; }
            else if (member) { code = `${member.type}.${member.name} : ${member.ret}`; description = member.kind === "Method" ? "Нативный метод JASS#" : "Нативное свойство JASS#"; }
            else if (cls) { code = `class ${cls.name}`; description = "Класс текущего исходного файла"; }
            else if (constant) { code = word; description = "Константа Warcraft III"; }
            else if (variable) { code = `${variable.type} ${variable.name}`; description = variable.isParameter ? "Параметр" : "Локальная переменная / поле"; }
            if (!code) return undefined;

            const markdown = new vscode.MarkdownString();
            markdown.appendCodeblock(code, "jasssharp");
            if (description) markdown.appendMarkdown(`\n\n${description}`);
            return new vscode.Hover(markdown, range);
        }
    };
    context.subscriptions.push(vscode.languages.registerHoverProvider({ language: "jasssharp" }, hoverProvider));

    const semanticLegend = new vscode.SemanticTokensLegend(
        ["namespace", "class", "type", "function", "method", "variable", "parameter", "property", "enumMember", "keyword"],
        ["declaration", "readonly", "static", "defaultLibrary"]
    );

    const semanticProvider: vscode.DocumentSemanticTokensProvider = {
        provideDocumentSemanticTokens(document) {
            const text = document.getText();
            const classes = readClasses(text);
            const functions = readFunctions(text, classes);
            const variables = readVariables(text, classes, functions);
            const classNames = new Set(classes.map(c => c.name));
            const functionNames = new Set(functions.map(f => f.name));
            const parameterNamesSet = new Set(variables.filter(v => v.isParameter).map(v => v.name));
            const variableNames = new Set(variables.map(v => v.name));
            const customMembers = classes.flatMap(cls => readClassMembers(text, cls));
            const memberMethods = new Set([...MEMBERS.filter(m => m.kind === "Method").map(m => m.name), ...customMembers.filter(m => m.kind === "Method").map(m => m.name)]);
            const memberProperties = new Set([...MEMBERS.filter(m => m.kind !== "Method").map(m => m.name), ...customMembers.filter(m => m.kind === "Property").map(m => m.name)]);
            const declarationOffsets = new Map<number, { type: string; modifiers: string[] }>();

            for (const cls of classes) declarationOffsets.set(text.indexOf(cls.name, cls.start), { type: "class", modifiers: ["declaration"] });
            for (const fn of functions) declarationOffsets.set(fn.offset, { type: fn.isMethod ? "method" : "function", modifiers: ["declaration"] });
            for (const variable of variables) {
                declarationOffsets.set(variable.offset, {
                    type: variable.isParameter ? "parameter" : "variable",
                    modifiers: ["declaration"]
                });
            }

            const builder = new vscode.SemanticTokensBuilder(semanticLegend);
            for (const token of tokenize(text)) {
                if (token.kind !== "identifier") continue;
                const declaration = declarationOffsets.get(token.start);
                let type: string | undefined;
                let modifiers: string[] = [];

                if (declaration) {
                    type = declaration.type;
                    modifiers = declaration.modifiers;
                } else if (classNames.has(token.text)) {
                    type = "class";
                } else if (PRIMITIVE_TYPES.has(token.text.toLowerCase()) || BUILTIN_TYPES.has(token.text.toLowerCase())) {
                    type = "type";
                    modifiers = ["defaultLibrary"];
                } else if (KEYWORDS.has(token.text)) {
                    type = "keyword";
                } else if (nativeNames.has(token.text)) {
                    type = "function";
                    modifiers = ["defaultLibrary"];
                } else if (constantNames.has(token.text)) {
                    type = "enumMember";
                    modifiers = ["readonly", "defaultLibrary"];
                } else if (functionNames.has(token.text)) {
                    type = functions.find(f => f.name === token.text)?.isMethod ? "method" : "function";
                } else if (parameterNamesSet.has(token.text)) {
                    type = "parameter";
                } else if (variableNames.has(token.text)) {
                    type = "variable";
                } else {
                    const previous = text.slice(0, token.start).trimEnd();
                    if (previous.endsWith(".")) {
                        if (memberMethods.has(token.text)) type = "method";
                        else if (memberProperties.has(token.text)) type = "property";
                    }
                }

                if (type) {
                    const start = document.positionAt(token.start);
                    builder.push(start.line, start.character, token.end - token.start, type, modifiers);
                }
            }
            return builder.build();
        }
    };
    context.subscriptions.push(vscode.languages.registerDocumentSemanticTokensProvider({ language: "jasssharp" }, semanticProvider, semanticLegend));

    const formatter: vscode.DocumentFormattingEditProvider = {
        provideDocumentFormattingEdits(document, options) {
            const original = document.getText();
            const eol = document.eol === vscode.EndOfLine.CRLF ? "\r\n" : "\n";
            const lines = original.split(/\r\n|\n|\r/);
            const unit = options.insertSpaces ? " ".repeat(Math.max(1, options.tabSize)) : "\t";
            const output: string[] = [];
            let indent = 0;
            let inBlockComment = false;

            for (const rawLine of lines) {
                const trimmed = rawLine.trim();
                if (!trimmed) { output.push(""); continue; }

                const structure = structuralBraces(rawLine, inBlockComment);
                inBlockComment = structure.inBlockComment;
                let leadingClosers = 0;
                let consumed = rawLine.search(/\S/);
                if (consumed < 0) consumed = rawLine.length;
                for (const brace of structure.braces) {
                    if (brace.char !== "}" || rawLine.slice(consumed, brace.index).trim() !== "") break;
                    leadingClosers++;
                    consumed = brace.index + 1;
                }
                const lineIndent = Math.max(0, indent - leadingClosers);
                const elseOpen = /^\}\s*else\b([\s\S]*?)\{\s*(\/\/.*)?$/.exec(trimmed);

                if (elseOpen && structure.braces.some(b => b.char === "{")) {
                    const suffix = elseOpen[2] ? ` ${elseOpen[2]}` : "";
                    output.push(unit.repeat(lineIndent) + `} else${elseOpen[1].trimEnd()}`);
                    output.push(unit.repeat(lineIndent) + `{${suffix}`);
                    indent = Math.max(0, indent + structure.braces.filter(b => b.char === "{").length - structure.braces.filter(b => b.char === "}").length);
                    continue;
                }

                const trailingOpen = [...structure.braces].reverse().find(b => b.char === "{");
                const hasCloseOnSameLine = structure.braces.some(b => b.char === "}" && (!trailingOpen || b.index > trailingOpen.index));
                if (trailingOpen && trailingOpen.index === rawLine.lastIndexOf("{") && !hasCloseOnSameLine && !inBlockComment) {
                    const header = rawLine.slice(0, trailingOpen.index).trim();
                    const tail = rawLine.slice(trailingOpen.index + 1).trim();
                    if (header) output.push(unit.repeat(lineIndent) + header);
                    output.push(unit.repeat(lineIndent) + `{${tail ? ` ${tail}` : ""}`);
                } else {
                    output.push(unit.repeat(lineIndent) + trimmed);
                }

                const delta = structure.braces.filter(b => b.char === "{").length - structure.braces.filter(b => b.char === "}").length;
                indent = Math.max(0, indent + delta);
            }

            const formatted = output.join(eol);
            if (formatted === original) return [];
            return [vscode.TextEdit.replace(new vscode.Range(document.positionAt(0), document.positionAt(original.length)), formatted)];
        }
    };
    context.subscriptions.push(vscode.languages.registerDocumentFormattingEditProvider({ language: "jasssharp" }, formatter));

    const onTypeFormatter: vscode.OnTypeFormattingEditProvider = {
        provideOnTypeFormattingEdits(document, position, character, options) {
            if (character !== "}") return [];
            const currentLine = document.lineAt(position.line).text;
            const leadingWhitespace = /^\s*/.exec(currentLine)?.[0].length ?? 0;
            const typedColumn = position.character - 1;
            if (typedColumn !== leadingWhitespace || currentLine[typedColumn] !== "}") return [];

            const precedingText = document.getText(new vscode.Range(new vscode.Position(0, 0), new vscode.Position(position.line, 0)));
            let depth = 0;
            let inBlockComment = false;
            for (const line of precedingText.split(/\r\n|\n|\r/)) {
                const structure = structuralBraces(line, inBlockComment);
                inBlockComment = structure.inBlockComment;
                depth = Math.max(0, depth + structure.braces.filter(b => b.char === "{").length - structure.braces.filter(b => b.char === "}").length);
            }

            const unit = options.insertSpaces ? " ".repeat(Math.max(1, options.tabSize)) : "\t";
            const expectedIndent = unit.repeat(Math.max(0, depth - 1));
            const range = new vscode.Range(position.line, 0, position.line, leadingWhitespace);
            return [vscode.TextEdit.replace(range, expectedIndent)];
        }
    };
    context.subscriptions.push(vscode.languages.registerOnTypeFormattingEditProvider({ language: "jasssharp" }, onTypeFormatter, "}"));
}

function tokenize(text: string): LexToken[] {
    const result: LexToken[] = [];
    let i = 0;
    while (i < text.length) {
        const start = i;
        const ch = text[i], next = text[i + 1];
        if (/\s/.test(ch)) { i++; continue; }
        if (ch === "/" && next === "/") {
            i += 2;
            while (i < text.length && text[i] !== "\n") i++;
            result.push({ kind: "comment", text: text.slice(start, i), start, end: i });
            continue;
        }
        if (ch === "/" && next === "*") {
            i += 2;
            while (i < text.length && !(text[i] === "*" && text[i + 1] === "/")) i++;
            i = Math.min(text.length, i + 2);
            result.push({ kind: "comment", text: text.slice(start, i), start, end: i });
            continue;
        }
        if (ch === '"' || ch === "'") {
            const quote = ch;
            i++;
            let escaped = false;
            while (i < text.length) {
                const current = text[i++];
                if (escaped) escaped = false;
                else if (current === "\\") escaped = true;
                else if (current === quote) break;
            }
            result.push({ kind: "string", text: text.slice(start, i), start, end: i });
            continue;
        }
        if (/[A-Za-z_$]/.test(ch)) {
            i++;
            while (i < text.length && /[A-Za-z0-9_$]/.test(text[i])) i++;
            result.push({ kind: "identifier", text: text.slice(start, i), start, end: i });
            continue;
        }
        if (/\d/.test(ch)) {
            i++;
            while (i < text.length && /[A-Za-z0-9_.]/.test(text[i])) i++;
            result.push({ kind: "number", text: text.slice(start, i), start, end: i });
            continue;
        }
        result.push({ kind: "punctuation", text: ch, start, end: ++i });
    }
    return result;
}

function structuralBraces(line: string, initialBlockComment: boolean): { braces: Array<{ char: string; index: number }>; inBlockComment: boolean } {
    const braces: Array<{ char: string; index: number }> = [];
    let inBlockComment = initialBlockComment;
    let state: "normal" | "string" | "char" | "lineComment" = "normal";
    let escaped = false;

    for (let i = 0; i < line.length; i++) {
        const ch = line[i], next = line[i + 1];
        if (inBlockComment) {
            if (ch === "*" && next === "/") { inBlockComment = false; i++; }
            continue;
        }
        if (state === "lineComment") break;
        if (state === "string" || state === "char") {
            if (escaped) escaped = false;
            else if (ch === "\\") escaped = true;
            else if ((state === "string" && ch === '"') || (state === "char" && ch === "'")) state = "normal";
            continue;
        }
        if (ch === "/" && next === "/") { state = "lineComment"; i++; continue; }
        if (ch === "/" && next === "*") { inBlockComment = true; i++; continue; }
        if (ch === '"') { state = "string"; continue; }
        if (ch === "'") { state = "char"; continue; }
        if (ch === "{" || ch === "}") braces.push({ char: ch, index: i });
    }
    return { braces, inBlockComment };
}
