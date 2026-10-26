# Layout XML version 1

An adjacent file such as `TileCatalog.weavesharp.xml` holds presentation for
`TileCatalog.cs`. Deleting it loses saved presentation. It has no role in compiling
the C# document. This format addresses META-01 through META-10; coordinate policy
follows DESIGN-08 sections 6 and 7.

`visual-1.xsd` describes the known XML vocabulary in `urn:weavesharp:visual:1`.
The root is `visualDocument`, with `schemaVersion="1"` and a relative `source`.

| Element | Required data | Optional data and children |
| --- | --- | --- |
| `view` | `id` | `memberHint`, `contextHint`, `flow`; nodes, notes, groups, routes |
| `node` | `id`, `syntaxKind`; one `anchor` | `collapsed`, paired `x` and `y`; one `extent` |
| `anchor` | At least one nonempty hint | `syntaxPathHint`, `memberSignature`, `syntaxRole`, `fingerprint` |
| `note` | `id`; one `text` | Paired `x` and `y`; one `extent` |
| `extent` | `along`, `across` | None |
| `group` | `id`, nonblank `title`; one or more `member` children | `collapsed`, `note` |
| `member` | `node` | None |
| `edgeRoute` | `id`, `from`, `fromPort`, `to`, `toPort`; one or more `point` children | None |
| `point` | `x`, `y` | None |

Views have document-wide identifiers. Nodes, notes, groups and routes share an
identifier scope within each view. References may name missing nodes. Such groups
and routes stay in the document; the reader reports their unresolved references
in `Orphans`. That list is derived on reading and has no separate XML record.

Positions describe placements chosen by a user. Route points describe an edge the
user rerouted. Extents describe manual resizing. Callers must leave absent values
unset: the public model has no flag distinguishing automatic geometry from a user
choice. The writer neither calculates geometry nor fills missing positions or
sizes. Viewport, selection, zoom and debugger state have no fields here.

Coordinates are integers in logical units. `x` follows flow; `y` crosses it.
The reader's default coordinate range is -1,000,000 to 1,000,000, and each extent
dimension ranges from 1 to 100,000. `flow` accepts `left-to-right`, `top-to-bottom`
or `right-to-left`. A positioned view without a saved direction is written with
`left-to-right`. An unpositioned view keeps an absent direction absent. Saving its
first position therefore also changes the view's opening line to add `flow`.

## Writer behavior

`LayoutWriter.Write(VisualDocument)` returns a byte array. It does no filesystem
work. The caller owns revision checks, conflict handling and file replacement.
Before rewriting a file, check `LayoutReadResult.CanWrite`; a damaged document's
partial model cannot recover the records that the reader omitted.

Output uses UTF-8 without a BOM, an XML declaration, LF newlines, two spaces per
nesting level and a final newline. Both empty documents and empty views retain
separate opening and closing lines. This avoids changing container lines when the
first record arrives or the last one leaves.

Views sort by `id` using ordinal comparison. Within a view the order is nodes,
notes, groups, then routes; identifiers sort each category. Unknown children
follow those records, retaining their relative order. Group membership and route
point sequences retain their supplied order. A node writes its anchor before an
extent; a note writes its text before an extent.

Known attributes follow the order in the table, with these expansions: the root
writes `schemaVersion` then `source`; a view writes `id`, `memberHint`,
`contextHint`, `flow`; a node writes `id`, `syntaxKind`, `collapsed`, `x`, `y`;
a group writes `id`, `title`, `collapsed`, `note`. Extension attributes come last,
ordered by namespace URI and local name. Prefixes for those attributes are assigned
within each element, so inserting a sibling does not renumber existing prefixes.
Namespace declarations are formatting details and are not retained by the model.

A change to an already positioned node affects its opening line. Inserting or
removing a record affects that record's block, except when a first position also
requires adding the view's direction. Escaping preserves note and attribute text,
including control whitespace allowed by XML. Whitespace-only notes use
`xml:space="preserve"`. Comments are not kept by the reader.

Before returning bytes, the writer passes them through `LayoutReader` with default
limits. A reader refusal or a serialization error becomes an `ArgumentException`
whose message explains the problem. This includes unsupported versions, malformed
records, duplicate identifiers, out-of-range values and invalid source paths.
Reader validation stays in the existing reader; the writer introduces no parallel
input validator.

## Extensions and current compatibility limits

The model retains unknown attributes on the root, views, nodes, anchors, notes,
groups and routes. It retains unknown child elements on those containers except
anchors. The writer emits those retained values, including unresolved records,
without resolving their references or interpreting their contents.

The XSD permits foreign-namespace children at those container extension points
and unknown attributes. It enforces required fields, child counts, identifier
scopes and numeric bounds. Child order is flexible in the schema even though the
writer chooses a fixed order. Foreign content uses `processContents="skip"`, so
schema validation does not require another schema. Load it with a null resolver,
as the tests do.

The schema and reader are not interchangeable validators. XSD 1.0 cannot express
the paired optional coordinates or the requirement for at least one anchor hint.
Source-path rules and reader resource limits also require the reader. Numeric
schema types normalize whitespace; the reader's number syntax is stricter.

The reader accepts additional unknown elements in the visual namespace and in no
namespace. The writer preserves them, but they fall outside this XSD's extension
policy. XSD 1.0's wildcard rules cannot combine arbitrary unknown names in the
target namespace with distinct typed declarations for the known children. Thus
not every reader-valid extension document will validate against this schema.

There are also existing reader/model losses that this writer cannot repair:
unknown attributes on `extent`, `member`, `point` and `text` are accepted but not
represented; namespace-prefix spellings are discarded; mixed extension text is
collected after child elements. Those details may already have changed before
`Write` receives the model. Full preservation requires changes to the reader and
its public model, which are outside WS-07's permitted files. The tests expose the
leaf-attribute and schema-policy gaps rather than claiming complete preservation.

The fixtures in `tests/WeaveSharp.Layout.Tests/Fixtures` are independently written
examples. Tests check exact bytes, localized changes, model data, extension order,
orphan retention, schema conformance for its supported extension policy, and
writer refusals. They do not establish CI results or complete reader/schema parity.
