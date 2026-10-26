# Requirement links

[register.json](register.json) contains 143 requirements, 33 acceptance cases,
and eight gates. Its labels summarize the obligations in our own words.
Entries describe planned evidence; their presence does not establish product
implementation, successful acceptance testing, or release approval.

[register.schema.json](register.schema.json) describes the JSON structure.
The root has three arrays:

- `gates`: `id` and a brief `name`.
- `acceptanceCases`: `id` and a brief `title`.
- `requirements`: the fields below.

| Field | Meaning |
| --- | --- |
| `id` | Stable requirement identifier. |
| `family` | The letters preceding the identifier's hyphen. |
| `title` | An original summary of up to twelve whitespace-separated words. |
| `section` | Defining section in the master design; `B` means Appendix B. |
| `acceptanceCases` | Applicable `AC-` identifiers, sorted numerically. |
| `gates` | Applicable `G-` identifiers, sorted numerically. |
| `assessment` | The primary method for establishing evidence. |

An `executable` assessment needs at least one acceptance case. The other
methods are `design-review`, `dependency-review`, `accessibility-study`, and
`measurement`. They describe document inspection, dependency evaluation,
accessible interaction studies, and workload measurements respectively.
These entries keep applicable case links even when their primary evidence
method is not execution. QUAL-04 has no case link because its evidence comes
from workload measurements. Every entry still needs a gate.

The inventory follows the master design's section 17 family order. Its links
start from that section's map; section 16 supplies narrower acceptance links
for UX, OPS, and QUAL. Those links replace the family defaults. QUAL-01,
QUAL-05, and QUAL-08 reference all 33 cases. QUAL-01 is defined in section 15
prose. GRF-14 through GRF-17 are defined in section 6; GRF-18 is in section 5.
The TRC definitions are in Appendix B. All defined identifiers fit the
section 17 ranges in this baseline.

To change an entry, consult its definition and both mapping sections. Write
new wording and preserve the established identifiers. When the design
changes its inventory or mappings, update the expectations in
`tests/WeaveSharp.Requirements.Tests/DesignBaseline.cs` as part of the same
review. Compare the resulting identifiers with the private design before
committing; the public tests do not load that document. Run the private
content check over the complete proposed diff.

The test project reads the register and schema by walking upward from its
assembly directory until it finds `WeaveSharp.slnx`. It therefore works with
normal build output and workspace-local artifact directories. No copies of
the register or private design are embedded in the assembly.

Validation covers the schema keywords used here, the exact inventory,
definition sections, section 16/17 mappings, reference resolution, and case
coverage. Unknown schema keywords fail validation instead of being ignored.
The schema checker is deliberately limited to this schema's vocabulary;
it is not a general JSON Schema implementation. Cross-record rules live in
the C# validator. Mutation tests remove evidence and gate links and require
diagnostics that identify the affected requirement.

The existing solution workflow restores with `--locked-mode`, builds Release,
and runs tests. In an offline sandbox, use the prepared local package feed
and keep restore caches and build artifacts within the workspace. Where the
test command's local transport is unavailable, run the built test assemblies
directly and report that distinction. Local results do not certify the
Linux, Windows, and macOS CI jobs.
