package com.weavesharp.rider

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.application.ReadAction
import com.intellij.openapi.editor.event.DocumentEvent
import com.intellij.openapi.editor.event.DocumentListener
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.fileEditor.FileEditor
import com.intellij.openapi.fileEditor.FileEditorPolicy
import com.intellij.openapi.fileEditor.FileEditorProvider
import com.intellij.openapi.fileEditor.FileEditorState
import com.intellij.openapi.fileEditor.FileEditorStateLevel
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Disposer
import com.intellij.openapi.util.UserDataHolderBase
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.ui.jcef.JBCefApp
import com.intellij.ui.jcef.JBCefBrowser
import org.cef.browser.CefBrowser
import org.cef.browser.CefFrame
import org.cef.handler.CefLoadHandlerAdapter
import java.awt.BorderLayout
import java.beans.PropertyChangeListener
import java.util.concurrent.atomic.AtomicLong
import javax.swing.JComponent
import javax.swing.JLabel
import javax.swing.JPanel

// DumbAware: the view needs no index, so Rider offers it at once instead of adding it again when
// indexing ends, which showed a second tab for the same editor (the Builder's hands-on run).
class GraphEditorProvider : FileEditorProvider, DumbAware {
    override fun accept(project: Project, file: VirtualFile): Boolean = !file.isDirectory && file.extension == "cs" &&
        file.url.codePointCount(0, file.url.length) <= 2048
    override fun createEditor(project: Project, file: VirtualFile): FileEditor = GraphEditor(project, file)
    override fun getEditorTypeId(): String = "weavesharp-rider-trial"
    override fun getPolicy(): FileEditorPolicy = FileEditorPolicy.PLACE_AFTER_DEFAULT_EDITOR
}

class GraphEditor(project: Project, private val sourceFile: VirtualFile) : UserDataHolderBase(), FileEditor {
    companion object { private val views = AtomicLong() }
    private val viewId = "rider-view-${views.incrementAndGet()}"
    private val sequence = AtomicLong()
    private val panel = JPanel(BorderLayout())
    private val document = FileDocumentManager.getInstance().getDocument(sourceFile)
    private var browser: JBCefBrowser? = null
    @Volatile private var disposed = false
    @Volatile private var ready = false

    init {
        if (document == null) {
            panel.add(JLabel("Rider has no text document for this file."))
        } else if (!JBCefApp.isSupported()) {
            panel.add(JLabel("The graph view needs Rider's bundled JCEF browser."))
        } else {
            val resources = SurfaceResources.bundled()
            val embedded = JBCefBrowser()
            browser = embedded
            Disposer.register(this, embedded)
            val host = SurfaceBrowser(embedded, resources)
            embedded.jbCefClient.addLoadHandler(object : CefLoadHandlerAdapter() {
                override fun onLoadStart(browser: CefBrowser, frame: CefFrame, transitionType: org.cef.network.CefRequest.TransitionType) {
                    if (frame.isMain) ready = false
                }
                override fun onLoadEnd(browser: CefBrowser, frame: CefFrame, httpStatusCode: Int) {
                    if (frame.isMain && frame.url == SurfaceResources.PAGE && httpStatusCode == 200) {
                        ApplicationManager.getApplication().invokeLater {
                            if (!disposed) { ready = true; publish() }
                        }
                    }
                }
            }, embedded.cefBrowser)
            document.addDocumentListener(object : DocumentListener {
                override fun documentChanged(event: DocumentEvent) { publish() }
            }, this)
            project.getService(DebugObserver::class.java)
            panel.add(embedded.component, BorderLayout.CENTER)
            host.open()
        }
    }

    private fun publish() {
        val current = document ?: return
        val json = ReadAction.compute<String, RuntimeException> {
            val revision = sequence.incrementAndGet()
            val snapshot = Snapshot("rider", sourceFile.url, current.modificationStamp.toString(), "$viewId-$revision")
            val context = Context(viewId, "stand-in", "no-compiler", snapshot)
            StandIn.publication(StandIn.project(current.text, context, revision), "publication-$revision")
        }
        ApplicationManager.getApplication().invokeLater {
            if (!disposed && ready) browser?.cefBrowser?.executeJavaScript(HostDelivery.script(json), SurfaceResources.PAGE, 0)
        }
    }

    override fun getComponent(): JComponent = panel
    override fun getPreferredFocusedComponent(): JComponent = browser?.component ?: panel
    override fun getName(): String = "WeaveSharp trial"
    override fun getFile(): VirtualFile = sourceFile
    override fun getState(level: FileEditorStateLevel): FileEditorState = FileEditorState.INSTANCE
    override fun setState(state: FileEditorState) {}
    override fun isModified(): Boolean = false
    override fun isValid(): Boolean = !disposed && sourceFile.isValid
    override fun addPropertyChangeListener(listener: PropertyChangeListener) {}
    override fun removePropertyChangeListener(listener: PropertyChangeListener) {}
    override fun dispose() { disposed = true; ready = false }
}
