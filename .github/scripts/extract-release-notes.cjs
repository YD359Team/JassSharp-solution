const fs = require("node:fs");

function extractReleaseNotes(changelog, version) {
    if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) {
        throw new Error("Invalid release version: " + version);
    }

    // A validated SemVer contains only digits, dots and hyphens.
    const escapedVersion = version.replace(/\./g, "\\.");
    const heading = new RegExp("^## \\[" + escapedVersion + "\\](?:\\s+-\\s+.*)?\\s*$");
    const lines = changelog.replace(/^\uFEFF/, "").split(/\r?\n/);
    const start = lines.findIndex(line => heading.test(line));
    if (start < 0) {
        throw new Error("CHANGELOG.md has no section for version " + version + ".");
    }

    let end = lines.length;
    for (let index = start + 1; index < lines.length; index++) {
        if (/^##\s+/.test(lines[index])) {
            end = index;
            break;
        }
    }

    const notes = lines.slice(start + 1, end).join("\n").trim();
    if (!notes) {
        throw new Error("CHANGELOG.md section for version " + version + " is empty.");
    }
    return notes + "\n";
}

if (require.main === module) {
    const [version, changelogPath, outputPath] = process.argv.slice(2);
    if (!version || !changelogPath || !outputPath) {
        console.error("Usage: node extract-release-notes.cjs <version> <CHANGELOG.md> <output.md>");
        process.exitCode = 2;
    } else {
        try {
            const changelog = fs.readFileSync(changelogPath, "utf8");
            const notes = extractReleaseNotes(changelog, version);
            fs.writeFileSync(outputPath, notes, "utf8");
            console.log("Extracted CHANGELOG.md notes for " + version + ".");
        } catch (error) {
            console.error(error.message);
            process.exitCode = 1;
        }
    }
}

module.exports = { extractReleaseNotes };
