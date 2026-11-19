const test = require("node:test");
const assert = require("node:assert/strict");
const { extractReleaseNotes } = require("./extract-release-notes.cjs");

test("extracts only the requested version section", () => {
    const changelog = [
        "# Changelog",
        "## [Unreleased]",
        "### Added",
        "- Work in progress",
        "",
        "## [0.1.0] - 2026-11-15",
        "### Added",
        "- Compiler feature",
        "",
        "### Fixed",
        "- Regression",
        "",
        "## [0.0.1] - 2026-11-01",
        "- Previous feature",
    ].join("\n");

    assert.equal(
        extractReleaseNotes(changelog, "0.1.0"),
        "### Added\n- Compiler feature\n\n### Fixed\n- Regression\n",
    );
});

test("supports CRLF changelogs", () => {
    assert.equal(
        extractReleaseNotes("## [0.2.0]\r\n- Feature\r\n## [0.1.0]\r\n- Older\r\n", "0.2.0"),
        "- Feature\n",
    );
});

test("fails when the requested version is absent", () => {
    assert.throws(
        () => extractReleaseNotes("## [Unreleased]\n- Work\n", "0.1.0"),
        /no section for version 0.1.0/,
    );
});

test("fails when the requested section is empty", () => {
    assert.throws(
        () => extractReleaseNotes("## [0.1.0]\n## [0.0.1]\n- Older\n", "0.1.0"),
        /section for version 0.1.0 is empty/,
    );
});

test("rejects invalid versions", () => {
    assert.throws(
        () => extractReleaseNotes("## [latest]\n- Notes\n", "latest"),
        /Invalid release version/,
    );
});
