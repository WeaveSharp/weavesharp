# WeaveSharp

Visual authoring and debugging for ordinary C# projects, in the editors you already use.

WeaveSharp is planned as extensions for Visual Studio, Visual Studio Code and JetBrains Rider that let you read, write and debug C# as executable graphs, while your C# source and project files stay the program. Open a method as a graph, connect calls to the libraries you already use, review each change as an ordinary C# diff, and follow execution in the debugger you already have.

## Status

Nothing is built or released yet. Development begins with its first stage, contracts and feasibility, which settles the source-editing contracts, the C# 14 construct inventory and what each editor's extension APIs make possible. This README will say when a release exists and what it can do; until then, treat everything below as design commitments, not features.

## What WeaveSharp commits to

- **Your source stays authoritative.** A graph is a view of your current C# documents, including unsaved edits. Opening, arranging or saving a graph never changes your source. Every semantic graph edit is a defined C# change you can preview, undo and review.
- **No new runtime.** Your project builds, runs, tests and debugs with its normal tools, with or without WeaveSharp. WeaveSharp adds no interpreter, scheduler, compiler or required package to your application.
- **Optional layout.** Node positions, groups and notes can be saved beside a source file as versioned XML. Deleting that file loses only presentation, never program meaning.
- **The debugger you already use.** Graph debugging maps paused locations, breakpoints and values from your editor's own debugger, and shows when a mapping is exact, approximate or unavailable instead of guessing.
- **Evidence over appearance.** Execution history, thread colors and operation views show only what was actually observed, with gaps and limits visible.
- **Your language version.** C# 14 on .NET 10 is the reference; every project keeps its own language version, target and settings.
- **Accessible authoring.** Every graph operation is available from the keyboard and through an accessible outline, not only by dragging.

## Editors

| Editor | Commitment |
| --- | --- |
| Visual Studio Code | Primary integration |
| JetBrains Rider | Primary integration |
| Visual Studio | Primary integration |
| Cursor, Windsurf, VSCodium, Google Antigravity | Tested compatible hosts of the VS Code extension |
| VS Code Remote SSH, Dev Containers, WSL, GitHub Codespaces | Remote scenarios |

Each editor ships only what has passed its own evidence checks; installing an extension does not by itself mean every feature works there.

## How delivery is gated

WeaveSharp is delivered in stages, each closed by evidence rather than a date:

1. **Contracts and feasibility:** the product and source-editing contracts, the language inventory and each editor's integration evidence.
2. **Reliable inspection:** load projects, show methods as graphs, navigate to source and keep optional layout, without ever changing your source.
3. **Defined visual authoring:** create declarations and a declared set of graph edits, each with a source preview and keyboard access.
4. **Graph debugging:** verified breakpoints, frames, current locations and values in each supported debugger configuration.
5. **Observed execution history:** opt-in capture of observed traversals, threads and operations, with visible gaps and measured cost.
6. **Qualified expansion:** the remaining C# authoring coverage and the further editors and remote scenarios, each qualified for its tier.

## Getting WeaveSharp

When a release exists, it will be published on the Visual Studio Code Marketplace and Open VSX, the JetBrains Marketplace and the Visual Studio Marketplace, and each release's extension files and checksums will also be on this repository's Releases page. Nothing is published yet.

## Questions and announcements

Ask questions in [Discussions](https://github.com/WeaveSharp/weavesharp/discussions) (Q&A). Releases are announced in Discussions (Announcements).

## Security

Please report vulnerabilities privately, as [SECURITY.md](SECURITY.md) describes, never in a public issue.

## License

WeaveSharp is licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE).

## Contact

weavesharp@jasonfurr.com
