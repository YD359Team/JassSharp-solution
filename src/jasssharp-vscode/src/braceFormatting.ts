export interface BraceFormattingEdit { start: number; end: number; text: string; }

/** Moves structural opening braces to their own line (Allman / C# style). */
export function getAllmanBraceEdits(source: string, eol = "\n"): BraceFormattingEdit[] {
    const edits: BraceFormattingEdit[] = [];
    let state: "code" | "line-comment" | "block-comment" | "string" | "raw-string" | "raw-literal" = "code";
    let escaped = false;
    for (let index = 0; index < source.length; index++) {
        const current = source[index], next = source[index + 1] ?? "", third = source[index + 2] ?? "";
        if (state === "line-comment") { if (current === "\n") state = "code"; continue; }
        if (state === "block-comment") { if (current === "*" && next === "/") { state = "code"; index++; } continue; }
        if (state === "string") {
            if (escaped) escaped = false;
            else if (current === "\\") escaped = true;
            else if (current === '"') state = "code";
            continue;
        }
        if (state === "raw-string") { if (current === '"' && next === '"' && third === '"') { state = "code"; index += 2; } continue; }
        if (state === "raw-literal") { if (current === "'") state = "code"; continue; }
        if (current === "/" && next === "/") { state = "line-comment"; index++; continue; }
        if (current === "/" && next === "*") { state = "block-comment"; index++; continue; }
        if (current === '"' && next === '"' && third === '"') { state = "raw-string"; index += 2; continue; }
        if (current === '"') { state = "string"; escaped = false; continue; }
        if (current === "'") { state = "raw-literal"; continue; }
        if (current !== "{") continue;
        const lineStart = source.lastIndexOf("\n", index - 1) + 1;
        const prefix = source.slice(lineStart, index);
        if (!prefix.trim()) continue;
        const trailing = prefix.match(/[ \t]*$/)?.[0] ?? "";
        const indent = prefix.match(/^[ \t]*/)?.[0] ?? "";
        edits.push({ start: index - trailing.length, end: index, text: eol + indent });
    }
    return edits.sort((a,b)=>b.start-a.start);
}
