# Project Rules

## General
- Follow project documents.

## .NET Guidelines

## Logging

## Error Handling
- Handle general Exceptions.

## Code Style
- Follow existing project styles.
- **`<summary>` / `<param>` / `<returns>` / `<exception>`:** Describe **current** behavior only (what the API does now).
- **`<remarks>`:** Use for (1) **technical** notes (lifecycle, logging, error-handling contracts, edge cases), and/or (2) **changelogs** when you modify existing members: what changed, why, related ADO work item; optional **Author** and **Last Updated** inside a `<para>` for in-source traceability. Commits must still reference ADO—XML changelog complements git history, it does not replace it.