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
- Added Lua runtime integration tests for hook execution, early returns, native delegation, cross-module ordering, and one-time finalization.
- Added automatic GitHub Release notes extraction from the matching version section in `CHANGELOG.md`.

### Fixed
- Headless smoke coverage now proves that a project builds without `main`, `config`, or `OnInit`.
