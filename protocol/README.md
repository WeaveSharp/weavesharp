# WeaveSharp protocol v0

This directory defines the first wire format and a deterministic model of its
request lifecycle. It addresses ARC-03, ARC-04, ARC-06 and ARC-07 in section 13.
The implementation operates on decoded records. Hosting, transport parsing,
editor transactions and graph construction are separate work.

`v0/envelope.schema.json` selects one of ten family schemas. Each file uses JSON
Schema draft 2020-12; its HTTPS identifier is a name, and local checks resolve
references from the adjacent files without fetching anything.

Every message has these six fields:

| Field | Meaning |
| --- | --- |
| `version` | A major/minor pair from the peers' negotiated intersection. |
| `family` | The discriminator identifying a payload contract. |
| `requestId` | A session-scoped identity, also used by replies and cancellation. |
| `kind` | `query`, `mutation`, `response` or `notification`. |
| `payload` | One of the shapes listed below. |
| `extensions` | A bounded dictionary of named extension entries; empty is valid. |

Request identities remain reserved for the lifetime of the session, including
after completion. A reply repeats the request's identity. A notification gets
its own identity but creates no pending request. A caller assigns distinct
identities across reconnects and creates a new session after negotiating again.

The initial implementation advertises version `0.0`. The negotiator chooses
the greatest pair present in both offers; a minor version is not implicitly
supported merely because it is numerically smaller. An empty intersection
prevents session creation. Initialization precedes editing. Component versions
are descriptive and do not substitute for a protocol agreement.

Each extension entry contains `mandatory` and a string `value`. Known names are
returned as enabled. An unknown name with `mandatory: false` is reported as
ignored; a required unknown name prevents agreement. All regular objects are
closed. Adding fields directly to a payload is not the extension mechanism.
Initialization's `trustState` is descriptive data; this library assigns it no
authority or access effect.

| Family / schema stem | Kinds | Payload purpose |
| --- | --- | --- |
| `initialize` | query, response | Version offers, component releases, host/workspace identities, capabilities and a declared trust label. |
| `set-compilation-context` | notification | Project target, language/compiler settings, reference revisions and generated-source generation. |
| `publish-source-snapshot` | notification | Host-owned document identity with complete text or changes against a named prior snapshot. |
| `request-graph` | query | A body span and requested detail; `requireComplete` asks to replace cached graph data. |
| `publish-graph` | response, notification | Revision, typed nodes, connections, regions, source mappings, diagnostics and coverage. |
| `prepare-edit` | query | Command, source anchors, choices and the documents intended to receive edits. |
| `edit-result` | response, mutation | Proposal, requested host transaction, applied result or rejection with a reason. |
| `publish-execution-state` | notification | Execution session, source generation, evidence category and current capabilities. |
| `read-or-save-layout` | query, mutation, response | Layout version, source hints, view ownership, positions and reported conflicts. |
| `cancel-or-dispose` | notification | Cancellation of a request or disposal of a view, context, session or workspace. |

The edit sequence starts with a `prepare-edit` query. Its `edit-result` response
has stage `proposal`. Accepting that proposal is a separate `edit-result`
mutation with stage `commit` and outcome `requested`; its reply is `applied` or
`rejected`. The host checks document versions and owns the transaction. A
proposal or acknowledgement does not advance the active source snapshot.
Only observing the host's published source changes that context. A refusal
requires a new preparation if the user still wants the operation.

Layout reads are queries and saves are mutations. A layout response states
which operation it answers. A stopped execution payload includes a pause and
frame identity; the running form has neither. These are transient identifiers,
not serialized runtime objects. Consumers discard frame data when the pause or
source generation changes. Layout conflicts do not imply source changes.

A `SnapshotIdentity` combines host identity, a document URI, the host's version
and a snapshot token. Context additionally names the workspace, project and
compilation generation. Equal document version strings from different hosts
are unrelated. URI notation is hierarchical (`scheme://...`); host adapters
map it to their document model. Source spans use zero-based UTF-16 offsets and
a length in UTF-16 code units, with the ending offset excluded. Each span names
its snapshot. For source deltas, change spans refer to the base snapshot and
the enclosing context describes the resulting snapshot. Changes in a batch
share that base; they are not offsets into successively edited text.

Contract sizes are explicit. Identifiers generally allow 128 Unicode scalars,
document URIs 2,048, text fragments 1,048,576, and extension values 4,096. A
message permits 32 extensions, each with a name of at most 64 scalars. Graphs
allow 16,384 nodes, 32,768 edges and 4,096 regions. Other collections and nested
objects also have limits in their schemas. Schema string lengths count Unicode
scalars; source offsets count UTF-16 units. These are different measurements.

`ProtocolSession` represents one view. Call it serially on the host's event
loop. Its transport is supplied by the caller and its clock is a caller-provided
`TimeSpan`; it starts no workers or timers. `Begin` takes ownership of a request's
resource lease once registration succeeds. Completion, cancellation, timeout,
replacement or disposal releases that lease once. Argument rejection leaves
ownership with the caller. Leases should have nonthrowing `Dispose` methods.

Responses must match the request identity, expected response family, original
context and current context. Wrong-family and mismatched-context replies leave
the pending request available for a correct reply. Changing context retires
old pending work. Queries in the same response-family/slot replace older
queries, including their retry option. Use distinct slots for independent
bodies or views. Terminal dispositions are returned explicitly, so callers can
record ignored replies without making them visible again.

`Expire` marks replies missing at the supplied deadline; callers can also use
`LoseResponse` when transport failure is known. Retrying a query requires a
fresh identity, a fresh lease and the same still-active context. A mutation
cannot enter that retry path. Missing mutation replies instead produce an
`InspectHostDocuments` callback carrying the original request identity and
starting document versions. A transport exception is also an uncertain outcome,
since sending may already have happened.

The host inspection must describe a settled original transaction before it can
decide an outcome. Missing, duplicate or wrong-host document observations keep
reconciliation pending. Changed versions tagged by the host with that original
mutation identity establish application; settled unchanged snapshots establish
non-application. Other combinations, including partial application, are
reported as divergence. Host formatting may produce text different from the
proposal. Every outcome is based on host observations, never assumed text.
No reconciliation result resends a mutation, including `NotApplied`.

Cancelling local work cannot undo a host transaction. Pending mutations still
request inspection when cancelled, disposed or superseded by context. That
inspection can finish after view disposal, while ordinary late replies stay
ignored. Hosts must retain the original identity until their transaction status
is settled. Terminal request metadata remains in the reference session until
the session itself becomes unreachable; production adapters should bound
session lifetime and concurrent work according to their own integration.

`GraphReceiver` tracks revisions by context and graph identity. Feed it a reply
only after `ProtocolSession.Receive` returns `Accepted`; notifications still
need the current context. An incremental graph names `baseRevision` and lists
removed identifiers alongside replacement/addition records. The other lists
describe the new mappings, diagnostics and coverage. If that base is absent,
the receiver keeps its revision and calls the supplied complete-graph request
handler. A later complete snapshot reestablishes the base for following deltas.
Old revisions and graphs for another active context cannot advance the view.
The receiver tracks revisions; callers own actual graph materialization.

`WeaveSharp.Protocol.Tests` checks both accepted and rejected JSON examples
for every family, round-trips the accepted examples through the C# contracts,
and exercises bounds and lifecycle transitions. Its fixture checker supports
only the schema vocabulary used here and refuses unfamiliar keywords, including
inside unused definitions. It stays in the test assembly. The session tests
use recorded transport calls, controlled time and counted resource leases.

Both projects are in the solution and have package lock files. Normal CI uses
the repository's locked restore, Release build and test steps. Offline sandbox
runs can build with a local feed and execute the built test assemblies directly;
that is separate evidence from running `dotnet test` on each CI operating system.
