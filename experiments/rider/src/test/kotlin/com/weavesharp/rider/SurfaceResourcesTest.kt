package com.weavesharp.rider

import java.util.Base64
import org.junit.jupiter.api.Assertions.*
import org.junit.jupiter.api.Test

class SurfaceResourcesTest {
    private fun resource(name: String): String = checkNotNull(javaClass.getResourceAsStream("/surface/$name"))
        .use { it.readBytes().toString(Charsets.UTF_8) }

    @Test fun `every page response changes only the placeholder in the generated page and policy`() {
        val resources = SurfaceResources.bundled()
        val nonces = mutableSetOf<String>()
        repeat(12) {
            val response = resources.response(SurfaceResources.PAGE, "GET")!!
            val policy = response.policy!!
            val nonce = Regex("'nonce-([^']+)'").find(policy)!!.groupValues[1]
            assertEquals(24, nonce.length)
            assertEquals(16, Base64.getDecoder().decode(nonce).size)
            assertTrue(nonces.add(nonce))
            assertEquals("text/html; charset=utf-8", response.contentType)
            assertEquals(resource("index.html").replace(SurfaceResources.PLACEHOLDER, nonce), response.bytes.toString(Charsets.UTF_8))
            assertEquals(resource("page-policy.txt").replace(SurfaceResources.PLACEHOLDER, nonce), policy)
            assertFalse(policy.contains(SurfaceResources.PLACEHOLDER))
            assertFalse(response.bytes.toString(Charsets.UTF_8).contains(SurfaceResources.PLACEHOLDER))
            assertTrue(policy.endsWith("; frame-ancestors 'none'"))
        }
    }

    @Test fun `only exact generated file URLs with GET have responses`() {
        val resources = SurfaceResources.bundled()
        val names = resource("files.txt").lineSequence().filter { it.isNotEmpty() }.toList()
        for (name in names) {
            val url = "${SurfaceResources.ORIGIN}/$name"
            assertTrue(resources.allows(url, "GET"))
            assertNotNull(resources.response(url, "GET"))
            for (method in listOf("POST", "HEAD", "PUT", "get")) assertNull(resources.response(url, method))
        }
        for (url in listOf("about:blank", "file:///index.html", "https://example.invalid/index.html",
            "${SurfaceResources.ORIGIN}/", "${SurfaceResources.PAGE}?refresh", "${SurfaceResources.PAGE}#anchor",
            "${SurfaceResources.ORIGIN}/../index.html", "${SurfaceResources.ORIGIN}/%69ndex.html",
            "${SurfaceResources.ORIGIN}/page-policy.txt", "${SurfaceResources.ORIGIN}/files.txt",
            "${SurfaceResources.ORIGIN}/experiments/rider/webview/generate.js", "${SurfaceResources.ORIGIN}/missing.js")) {
            assertFalse(resources.allows(url, "GET"), url)
            assertNull(resources.response(url, "GET"), url)
        }
    }

    @Test fun `scripts and styles retain their packaged bytes without a page nonce rewrite`() {
        val resources = SurfaceResources.bundled()
        for (name in listOf("experiments/rider/webview/entry.js", "experiments/canvas/src/canvas.css")) {
            val response = resources.response("${SurfaceResources.ORIGIN}/$name", "GET")!!
            assertEquals(resource(name), response.bytes.toString(Charsets.UTF_8))
            assertNull(response.policy)
        }
    }

    @Test fun `delivery contains only the fixed receiver call and standard Base64`() {
        for (message in listOf("", "{\"text\":\"測定 😀 שלום\"}", "\"\\\n</script>\u2028\u2029")) {
            val script = HostDelivery.script(message)
            val match = Regex("""window\.weavesharpReceive\("([A-Za-z0-9+/]*={0,2})"\)""").matchEntire(script)
            assertNotNull(match)
            val encoded = match!!.groupValues[1]
            assertEquals(Base64.getEncoder().encodeToString(message.toByteArray(Charsets.UTF_8)), encoded)
            assertEquals(message, Base64.getDecoder().decode(encoded).toString(Charsets.UTF_8))
        }
    }
}
