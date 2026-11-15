# Changelog

All notable changes to JASS# are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions follow Semantic Versioning.

## [Unreleased]

### Documentation
- Added project scaffolding instructions and directory layouts for Warcraft III and headless projects.
- Documented the current restriction on parameterized base constructors and added a regression check.

### Added
- Centralized the compiler and VS Code extension version in `Version.props`.
- Added expression-bodied methods, operators, and namespace-level functions.
- Added C#-style Allman brace formatting on type and document formatting.
- Added an always-available Lua build status-bar command, including in headless projects.
- Added explicit command activation and editor toolbar/context-menu entries.
- Added Lua runtime integration tests for hook execution, early returns, native delegation, cross-module ordering, one-time finalization, and Unicode string indexing.
- Added tag-driven GitHub Releases: CI validates the version, extracts release notes from `CHANGELOG.md`, and publishes assets only after all test jobs pass.
- Added a Windows compiler archive alongside the VS Code VSIX in GitHub Releases.

### Fixed
- Corrected `Strings.LastIndexOf` for multibyte UTF-8 text and overlapping matches.
- Headless smoke coverage now proves that a project builds without `main`, `config`, or `OnInit`.
