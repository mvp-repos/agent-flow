## CRITICAL RULE – READ CAREFULLY:

### General rules:
1. Do a codebase analysis before create the PRD using the bmad.
2. Read all project documents first.

### Coding rules:
1. When modifying code, **update `<summary>`** (and `<param>` / `<returns>` / `<exception>`) so they describe **current** behavior. Add or extend **`<remarks>`** for:
   - **Technical** notes (contracts, logging, edge cases, lifecycle), and/or  
   - **Changelog** when behavior changes: what changed, why, ADO ticket; optional **Author** and **Last Updated** in a `<para>` for traceability.  
   Commits must still reference ADO; XML changelog is in addition to git history.

2. Example changelog block inside `<remarks>` (optional when you change a member):

```csharp
/// <remarks>
/// <para>
/// Change: [what changed and why]. ADO #[id].
/// Author: [name]<br/>
/// Last Updated: dd/MM/yyyy
/// </para>
/// </remarks>
```

3. Always include XML summary comments for classes, interfaces, and methods, including

E.g.
```csharp
 /// <summary></summary>
 /// <param></param>
 /// <returns></returns>
 /// <exception></exception>
 ```

4. Always include inline comments explaining non-obvious logic.

E.g.
```csharp
 /// The code explaining comment
 ```

### Markdown document rules:
1. When creating a Markdown (.md) file, add the following lines at the very top of the document:

E.g.

```markdown
  **Author:** Cursor  
  **Editor:** Darshana Wijesinghe  
  **Created Date:** dd/MM/yyyy  
```

