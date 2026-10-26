package com.weavesharp.rider

import com.google.gson.Gson

data class Snapshot(val hostId: String, val documentUri: String, val hostVersion: String, val snapshotId: String)
data class Context(val workspaceId: String, val projectId: String, val compilationContextId: String, val snapshot: Snapshot)
data class Span(val snapshot: Snapshot, val start: Int, val length: Int, val coordinates: String = "utf16-offset")
data class GraphNode(val id: String, val kind: String = "sample-step", val typeName: String = "")
data class GraphEdge(val id: String, val from: String, val to: String, val kind: String = "execution")
data class GraphRegion(val id: String, val kind: String, val nodeIds: List<String>)
data class Mapping(val nodeId: String, val source: Span)
data class Diagnostic(val code: String, val message: String, val source: Span, val severity: String = "info")
data class Graph(
    val context: Context, val revision: Long,
    val nodes: List<GraphNode>, val edges: List<GraphEdge>, val regions: List<GraphRegion>,
    val mappings: List<Mapping>, val diagnostics: List<Diagnostic>, val coverage: String,
    val graphId: String = "stand-in-body", val mode: String = "complete",
)
data class Statement(val source: Span, val text: String, val terminal: Boolean)
data class Projection(val graph: Graph, val statements: List<Statement>, val editable: Boolean)
data class ProtocolVersion(val major: Int = 0, val minor: Int = 0)
data class Publication(
    val requestId: String, val payload: Graph,
    val version: ProtocolVersion = ProtocolVersion(), val family: String = "publish-graph",
    val kind: String = "notification", val extensions: Map<String, String> = emptyMap(),
)

object StandIn {
    private val method = Regex("""^\s*(?://[^\r\n]*\r?\n\s*)*class\s+[A-Za-z_][A-Za-z_0-9]*\s*\{\s*(?:public\s+)?(?:static\s+)?void\s+[A-Za-z_][A-Za-z_0-9]*\s*\(\s*\)\s*\{([^{}]*)\}\s*\}\s*$""")
    private val statement = Regex("""^(?:;|(?:int\s+[A-Za-z_][A-Za-z_0-9]*\s*=\s*-?[0-9]+|[A-Za-z_][A-Za-z_0-9]*(?:\+\+|--)|[A-Za-z_][A-Za-z_0-9]*\s*=\s*-?[0-9]+|return)\s*;)$""")

    fun project(source: String, context: Context, revision: Long): Projection {
        fun span(start: Int, length: Int) = Span(context.snapshot, start, length)
        fun tooLarge() = Projection(Graph(context, revision, emptyList(), emptyList(), emptyList(), emptyList(),
            listOf(Diagnostic("stand-in-size", "This sample displays at most 512 statements in 65536 UTF-16 units.", span(0, source.length))),
            "unavailable"), emptyList(), false)
        if (source.length > 65536) return tooLarge()
        val body = method.matchEntire(source)?.groups?.get(1)
        if (body == null) return Projection(Graph(context, revision, emptyList(), emptyList(), emptyList(), emptyList(),
            listOf(Diagnostic("stand-in-shape", "The sample expects a class containing a single void method without parameters.", span(0, source.length))),
            "unavailable"), emptyList(), false)

        val statements = mutableListOf<Statement>()
        val nodes = mutableListOf<GraphNode>()
        val edges = mutableListOf<GraphEdge>()
        val diagnostics = mutableListOf<Diagnostic>()
        var predecessor: String? = null
        var consumed = 0
        for (piece in Regex("[^;]*;").findAll(body.value)) {
            if (nodes.size + diagnostics.size == 512) return tooLarge()
            val text = piece.value.trim()
            val start = body.range.first + piece.range.first + piece.value.indexOf(text)
            val location = span(start, text.length)
            consumed = piece.range.last + 1
            if (!statement.matches(text)) {
                diagnostics += Diagnostic("stand-in-statement", "The sample parser does not cover this statement.", location)
                predecessor = null
                continue
            }
            val id = "statement-$start"
            val terminal = Regex("""return\s*;""").matches(text)
            statements += Statement(location, text, terminal)
            nodes += GraphNode(id)
            predecessor?.let { edges += GraphEdge("sequence-$start", it, id) }
            predecessor = if (terminal) null else id
        }
        if (body.value.substring(consumed).isNotBlank()) diagnostics += Diagnostic("stand-in-tail",
            "The method ends with text that the sample cannot parse.", span(body.range.first + consumed, body.value.length - consumed))
        val complete = diagnostics.isEmpty()
        return Projection(Graph(context, revision, nodes, edges,
            listOf(GraphRegion("method-body", "body", nodes.map { it.id })),
            statements.mapIndexed { index, item -> Mapping(nodes[index].id, item.source) }, diagnostics,
            if (complete) "complete" else "partial"), statements, complete)
    }

    fun publication(projection: Projection, requestId: String): String = Gson().toJson(Publication(requestId, projection.graph))

    fun insertionOffset(projection: Projection, caret: Int): Int? = if (!projection.editable) null else
        projection.statements.firstOrNull { caret >= it.source.start && caret < it.source.start + it.source.length }?.source?.start
}
