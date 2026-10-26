# C# meaning of graph edits

[contracts.json](contracts.json) defines the ten command families traced to
GRF-18 in design section 5. [contracts.schema.json](contracts.schema.json)
describes their format. These contracts specify intended editing behavior;
they do not claim that an editor implementation exists.

Each entry describes its source operation, prerequisites and validation work.
`requirement` contains traceability identifiers. `behaviorChange.mayChange`
distinguishes edits that can intentionally change execution from operations
that must preserve it. `previewRequirement` describes what the user must see.
All semantic edits use current document and compilation revisions, validate
the candidate, and apply through the host's undo transaction (SRC-05, SRC-06).
An unevaluated project context remains an explicit limitation (SRC-03).

The independently authored examples in
[WeaveSharp.Transformations.Tests](../../tests/WeaveSharp.Transformations.Tests)
exercise the five transformations in Appendix A, in its original order:

| Fixture directory | Observation |
| --- | --- |
| `receiver-arguments` | Receiver, operands and invocation retain their order, including failure paths. |
| `conditional-effects` | The right operand runs only when the left operand permits it. |
| `return-cleanup` | Return evaluation stays inside the protected body and cleanup precedes transfer to the caller. |
| `storage-alias` | A reference local still targets the array cell, with the original read and check ordering. |
| `start-before-await` | Starting both operations changes the trace and allows the second start after a first-task failure. |

The first four pairs require equal traces. The async pair is an intentional
behavior change under GRF-10 and GRF-11: each side has its own expected trace.
Its tasks are already completed or faulted, so no timing, thread scheduling,
network service or actual suspension is exercised. Results remain in argument
order. A synchronous failure while creating the first task still prevents the
second call on either side.

Tests compile each source file separately with Roslyn using C# 14 and the
running runtime's assemblies. Warnings and errors fail the test. Each run uses
a collectible load context and returns a recorded sequence of effects. Tests
also assert explicit expected traces, so equal but empty logs cannot pass.
The separate negative control eagerly evaluates a conditional operand; the
same sequence comparison rejects that variant.

From the repository root, normal validation is `dotnet restore --locked-mode`,
`dotnet build --no-restore --configuration Release`, then
`dotnet test --no-build --configuration Release`. The solution includes both
test projects in the existing CI matrix. In a sandbox that disallows the test
runner socket, invoke each built test DLL with `dotnet` instead. Such a run
does not establish that `dotnet test` or other operating systems passed.

These examples exercise fixed contexts and inputs. They are not a proof of
arbitrary source equivalence, a graph rewrite engine, or host integration.
