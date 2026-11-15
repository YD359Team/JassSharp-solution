const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const extensionRoot = path.resolve(__dirname, "..");
const repositoryRoot = path.resolve(extensionRoot, "../..");
const manifest = JSON.parse(fs.readFileSync(path.join(extensionRoot, "package.json"), "utf8"));
const lock = JSON.parse(fs.readFileSync(path.join(extensionRoot, "package-lock.json"), "utf8"));
const versionProps = fs.readFileSync(path.join(repositoryRoot, "Version.props"), "utf8");
const version = versionProps.match(/<Version>\s*([^<]+?)\s*<\/Version>/)?.[1]?.trim();

test("compiler and extension package versions share Version.props", () => {
    assert.ok(version, "Version.props must define a Version property");
    assert.equal(manifest.version, version);
    assert.equal(lock.version, version);
    assert.equal(lock.packages[""].version, version);
});

test("all contributed commands activate and build remains available in headless mode", () => {
    const commands = manifest.contributes.commands.map(command => command.command);
    for (const command of ["jasssharp.build", "jasssharp.buildLua", "jasssharp.buildMap", "jasssharp.runMap", "jasssharp.openEditor", "jasssharp.packMap"]) {
        assert.ok(commands.includes(command), `missing contributed command ${command}`);
        assert.ok(manifest.activationEvents.includes(`onCommand:${command}`), `missing activation event for ${command}`);
    }
    assert.ok(manifest.contributes.menus["editor/title"].some(item =>
        item.command === "jasssharp.buildLua" && item.when === "editorLangId == jasssharp"));
});
