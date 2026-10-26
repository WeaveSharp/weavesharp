# Sequence-point mapping experiment

This spike compiles small C# examples in memory and compares their portable PDB
locations with WS-05 source anchors. It produces a separate graph for each
callable body and records the evidence behind every mapping grade.

The [study](../../docs/debugging/mapping-study.md) explains the observations,
classification precedence, and the debugger work still needed in the hosts.

```csharp
var study = SequencePointStudy.Analyze("Example.cs", "snapshot-3", source);
var release = study.Builds.Single(build => build.Mode == BuildMode.Release);
var location = SequencePointStudy.MapOffset(release, methodToken, ilOffset);
```

`Analyze` uses C# 14, metadata references from the loaded runtime, and the
centrally selected Roslyn package. Each invocation emits Debug and Release images into
memory. The fixture code is never loaded or invoked. The resulting records carry
method tokens, offsets, source positions, body associations, mapping rules, and
the PDB's method-level custom information. No debugger adapter is included.

`MapOffset` operates within one compilation's method-token space. A point at
the requested offset supplies its source mapping. An offset inside a visible
interval receives an approximate result. Hidden intervals stay unavailable.
This lookup neither proves that an offset is an instruction boundary nor that a
debugger can pause there. A caller must first select the matching build.

The test executable finds every source file in `Fixtures/`, compiles it twice,
compares a second PDB inventory, checks the recorded classifications, and writes
JSON and Markdown reports beneath its own `mapping-reports/` output directory.
`Expectations/` contains reviewed classifications for every projected element.
Tests read those files; they never update them. Fixture text is normalized to LF
before both compilation and projection so checkout newline settings do not move
the expectation offsets. A separate test checks CRLF and UTF-16 columns.

Both projects belong to the solution. After a locked restore and a Release build,
the test executable can be run directly:

```sh
dotnet experiments/debug-mapping/WeaveSharp.DebugMapping.Spike.Tests/bin/Release/net10.0/WeaveSharp.DebugMapping.Spike.Tests.dll
```

For this sandbox run, restore used cached archives through a configuration in
`.vericairn/scratch/`; package storage, temporary directories and logs also stayed
inside that directory. The command-line audit override was restricted to these
offline restores. Project and repository package settings were left intact.

The existing workflow will include these projects through the solution. The
Builder still needs to refresh the generated dependency register, review the
commit, and obtain the hosted checks. Passing the local executable does not
establish a result for the CI test runner or for a live debugger.
