package com.weavesharp.rider

import com.intellij.ui.jcef.JBCefBrowser
import org.cef.browser.CefBrowser
import org.cef.browser.CefFrame
import org.cef.callback.CefBeforeDownloadCallback
import org.cef.callback.CefCallback
import org.cef.callback.CefContextMenuParams
import org.cef.callback.CefDownloadItem
import org.cef.callback.CefDownloadItemCallback
import org.cef.callback.CefMenuModel
import org.cef.handler.CefContextMenuHandlerAdapter
import org.cef.handler.CefDownloadHandlerAdapter
import org.cef.handler.CefLifeSpanHandlerAdapter
import org.cef.handler.CefRequestHandlerAdapter
import org.cef.handler.CefResourceHandlerAdapter
import org.cef.handler.CefResourceRequestHandler
import org.cef.handler.CefResourceRequestHandlerAdapter
import org.cef.misc.BoolRef
import org.cef.misc.IntRef
import org.cef.misc.StringRef
import org.cef.network.CefRequest
import org.cef.network.CefResponse
import java.util.concurrent.atomic.AtomicBoolean

class SurfaceBrowser(private val browser: JBCefBrowser, private val resources: SurfaceResources) {
    private val opening = AtomicBoolean(false)

    init {
        val client = browser.jbCefClient
        val cef = browser.cefBrowser
        client.addRequestHandler(object : CefRequestHandlerAdapter() {
            override fun onBeforeBrowse(browser: CefBrowser, frame: CefFrame, request: CefRequest,
                                        userGesture: Boolean, isRedirect: Boolean): Boolean {
                val initial = frame.isMain && request.url == SurfaceResources.PAGE && request.method == "GET" && !isRedirect
                return !(initial && opening.compareAndSet(true, false))
            }

            override fun getResourceRequestHandler(browser: CefBrowser, frame: CefFrame?, request: CefRequest,
                                                   isNavigation: Boolean, isDownload: Boolean, requestInitiator: String?,
                                                   disableDefaultHandling: BoolRef): CefResourceRequestHandler {
                disableDefaultHandling.set(true)
                return object : CefResourceRequestHandlerAdapter() {
                    override fun getResourceHandler(browser: CefBrowser, frame: CefFrame?, request: CefRequest): CefResourceHandlerAdapter =
                        PackagedResponse(if (isDownload) null else resources.response(request.url, request.method))
                }
            }
        }, cef)
        client.addLifeSpanHandler(object : CefLifeSpanHandlerAdapter() {
            override fun onBeforePopup(browser: CefBrowser, frame: CefFrame, targetUrl: String, targetFrameName: String): Boolean = true
        }, cef)
        client.addContextMenuHandler(object : CefContextMenuHandlerAdapter() {
            override fun onBeforeContextMenu(browser: CefBrowser, frame: CefFrame, params: CefContextMenuParams, model: CefMenuModel) {
                model.clear()
            }
        }, cef)
        client.addDownloadHandler(object : CefDownloadHandlerAdapter() {
            // Returning false leaves the download to JCEF's default handling, which cancels it;
            // the continuation is never called, and onDownloadUpdated cancels it as well.
            override fun onBeforeDownload(browser: CefBrowser, item: CefDownloadItem, suggestedName: String, callback: CefBeforeDownloadCallback): Boolean = false
            override fun onDownloadUpdated(browser: CefBrowser, item: CefDownloadItem, callback: CefDownloadItemCallback) {
                callback.cancel()
            }
        }, cef)
    }

    fun open() {
        opening.set(true)
        browser.loadURL(SurfaceResources.PAGE)
    }
}

private class PackagedResponse(private val content: SurfaceResponse?) : CefResourceHandlerAdapter() {
    private val bytes = content?.bytes ?: byteArrayOf()
    private var offset = 0

    override fun processRequest(request: CefRequest, callback: CefCallback): Boolean {
        callback.Continue()
        return true
    }

    override fun getResponseHeaders(response: CefResponse, responseLength: IntRef, redirectUrl: StringRef) {
        response.status = if (content == null) 403 else 200
        response.statusText = if (content == null) "Forbidden" else "OK"
        val type = content?.contentType ?: "text/plain; charset=utf-8"
        response.mimeType = type.substringBefore(';')
        val headers = mutableMapOf("Content-Type" to type)
        content?.policy?.let { headers["Content-Security-Policy"] = it }
        response.setHeaderMap(headers)
        responseLength.set(bytes.size)
        redirectUrl.set("")
    }

    override fun readResponse(dataOut: ByteArray, bytesToRead: Int, bytesRead: IntRef, callback: CefCallback): Boolean {
        val count = minOf(bytesToRead, bytes.size - offset)
        bytesRead.set(count)
        if (count == 0) return false
        bytes.copyInto(dataOut, 0, offset, offset + count)
        offset += count
        return true
    }
}
