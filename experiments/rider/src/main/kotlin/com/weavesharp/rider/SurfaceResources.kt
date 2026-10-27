package com.weavesharp.rider

import java.security.SecureRandom
import java.util.Base64

data class SurfaceResponse(val bytes: ByteArray, val contentType: String, val policy: String? = null)

class SurfaceResources(private val files: Map<String, ByteArray>, private val policyTemplate: String) {
    companion object {
        const val ORIGIN = "https://surface.weavesharp.invalid"
        const val PAGE = "$ORIGIN/index.html"
        const val PLACEHOLDER = "AAAAAAAAAAAAAAAAAAAAAA=="

        fun bundled(): SurfaceResources {
            fun bytes(name: String): ByteArray = checkNotNull(SurfaceResources::class.java.getResourceAsStream("/surface/$name")) {
                "Missing generated surface resource: $name"
            }.use { it.readBytes() }
            val names = bytes("files.txt").toString(Charsets.UTF_8).lineSequence().filter { it.isNotEmpty() }.toList()
            return SurfaceResources(names.associate { "$ORIGIN/$it" to bytes(it) }, bytes("page-policy.txt").toString(Charsets.UTF_8))
        }
    }

    fun allows(url: String, method: String): Boolean = method == "GET" && files.containsKey(url)

    fun response(url: String, method: String): SurfaceResponse? {
        if (!allows(url, method)) return null
        val bytes = files.getValue(url)
        if (url == PAGE) {
            val random = ByteArray(16)
            SecureRandom().nextBytes(random)
            val nonce = Base64.getEncoder().encodeToString(random)
            return SurfaceResponse(bytes.toString(Charsets.UTF_8).replace(PLACEHOLDER, nonce).toByteArray(Charsets.UTF_8),
                "text/html; charset=utf-8", policyTemplate.replace(PLACEHOLDER, nonce))
        }
        return SurfaceResponse(bytes, if (url.endsWith(".css")) "text/css; charset=utf-8" else "text/javascript; charset=utf-8")
    }
}

object HostDelivery {
    fun script(message: String): String {
        val encoded = Base64.getEncoder().encodeToString(message.toByteArray(Charsets.UTF_8))
        return "window.weavesharpReceive(\"$encoded\")"
    }
}
