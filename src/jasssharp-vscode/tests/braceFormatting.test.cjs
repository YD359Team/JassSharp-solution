const test = require("node:test");
const assert = require("node:assert/strict");
const { getAllmanBraceEdits } = require("../out/braceFormatting.js");

function applyEdits(source, edits) {
    let result = source;
    for (const edit of edits) result = result.slice(0, edit.start) + edit.text + result.slice(edit.end);
    return result;
}

test("moves class and method opening braces to their own lines", () => {
    const source = "class Actor {\n    void Run() {\n    }\n}";
    assert.equal(applyEdits(source, getAllmanBraceEdits(source)), "class Actor\n{\n    void Run()\n    {\n    }\n}");
});

test("ignores braces inside strings and comments", () => {
    const source = ['string text = "not { a block"; // { comment', "/* { block comment } */", "class Actor {", "}"].join("\n");
    assert.equal(applyEdits(source, getAllmanBraceEdits(source)), ['string text = "not { a block"; // { comment', "/* { block comment } */", "class Actor", "{", "}"].join("\n"));
});

test("leaves already separated braces unchanged", () => {
    assert.deepEqual(getAllmanBraceEdits("class Actor\n{\n}"), []);
});

test("preserves indentation and CRLF line endings", () => {
    const source = "    if (ready) {\r\n    }";
    assert.equal(applyEdits(source, getAllmanBraceEdits(source, "\r\n")), "    if (ready)\r\n    {\r\n    }");
});
