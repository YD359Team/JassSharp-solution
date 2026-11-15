# Changelog

All notable changes to JASS# are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions follow Semantic Versioning.

## [Unreleased]

### Added
- Centralized the compiler and VS Code extension version in `Version.props`.
- Added expression-bodied methods, operators, and namespace-level functions.
- Added C#-style Allman brace formatting on type and document formatting.
- Added an always-available Lua build status-bar command, including in headless projects.
- Added explicit command activation and editor toolbar/context-menu entries.

### Fixed
- Headless smoke coverage now proves that a project builds without `main`, `config`, or `OnInit`.
