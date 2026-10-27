package com.weavesharp.rider

import com.google.gson.JsonParser
import org.junit.jupiter.api.Assertions.*
import org.junit.jupiter.api.Test

class StandInTest {
    private val snapshot = Snapshot("rider", "file:///project/Meter.cs", "9007199254740999", "view-4")
    private val context = Context("view", "stand-in", "no-compiler", snapshot)
    private val sample = "class Meter { static void Tick() { int count = 4; count++; return; count--; } }"

    @Test fun `statements use UTF16 spans and a return ends its execution chain`() {
        val source = "// 測定 😀\r\n$sample"
        val result = StandIn.project(source, context, 4)
        assertTrue(result.editable)
        assertEquals("complete", result.graph.coverage)
        assertEquals(4, result.graph.nodes.size)
        assertEquals(2, result.graph.edges.size)
        result.statements.forEach { statement ->
            assertEquals(statement.text, source.substring(statement.source.start, statement.source.start + statement.source.length))
            assertEquals(snapshot, statement.source.snapshot)
        }
        assertEquals(source.indexOf("int count"), result.statements.first().source.start)
        assertTrue(result.statements[2].terminal)
    }

    @Test fun `unknown statements interrupt the sequence and disable insertion`() {
        val result = StandIn.project(sample.replace("count++;", "Work();"), context, 5)
        assertFalse(result.editable)
        assertEquals("partial", result.graph.coverage)
        assertEquals(0, result.graph.edges.size)
        assertEquals("stand-in-statement", result.graph.diagnostics.single().code)
        assertNull(StandIn.insertionOffset(result, result.statements.first().source.start))
    }

    @Test fun `unsupported shapes and unfinished text retain an explicit gap`() {
        for (source in listOf("class Meter { int Read() { return 4; } }", "class Meter { void Tick() { if (true) {} } }")) {
            val result = StandIn.project(source, context, 6)
            assertFalse(result.editable)
            assertEquals("unavailable", result.graph.coverage)
        }
        val tail = StandIn.project("class Meter { void Tick() { count++ } }", context, 7)
        assertEquals("partial", tail.graph.coverage)
        assertEquals("stand-in-tail", tail.graph.diagnostics.single().code)
    }

    @Test fun `insertion is before the caret statement and leaves other source intact`() {
        val result = StandIn.project(sample, context, 8)
        val target = sample.indexOf("count++")
        val offset = StandIn.insertionOffset(result, target + 3)
        assertEquals(target, offset)
        assertNull(StandIn.insertionOffset(result, 0))
        val edited = sample.substring(0, offset!!) + "; " + sample.substring(offset)
        val next = StandIn.project(edited, context.copy(snapshot = snapshot.copy(hostVersion = "22", snapshotId = "view-9")), 9)
        assertEquals(result.graph.nodes.size + 1, next.graph.nodes.size)
        assertEquals(";", next.statements[1].text)
        assertEquals(sample, edited.removeRange(offset, offset + 2))
    }

    @Test fun `large samples produce a small unavailable publication`() {
        for (source in listOf("class Meter { void Tick() { ${";".repeat(513)} } }", " ".repeat(65537))) {
            val result = StandIn.project(source, context, 10)
            assertEquals("unavailable", result.graph.coverage)
            assertTrue(result.graph.nodes.isEmpty())
            assertEquals("stand-in-size", result.graph.diagnostics.single().code)
        }
    }

    @Test fun `the publication keeps the stamp as a string and identifies the Rider snapshot`() {
        val json = JsonParser.parseString(StandIn.publication(StandIn.project(sample, context, 11), "publication-11")).asJsonObject
        assertEquals(setOf("version", "family", "kind", "requestId", "payload", "extensions"), json.keySet())
        assertEquals("publish-graph", json["family"].asString)
        assertEquals("notification", json["kind"].asString)
        assertEquals("complete", json["payload"].asJsonObject["mode"].asString)
        val identity = json["payload"].asJsonObject["context"].asJsonObject["snapshot"].asJsonObject
        assertTrue(identity["hostVersion"].asJsonPrimitive.isString)
        assertEquals(snapshot.hostVersion, identity["hostVersion"].asString)
        assertEquals("rider", identity["hostId"].asString)
        assertEquals(snapshot.documentUri, identity["documentUri"].asString)
    }
}
