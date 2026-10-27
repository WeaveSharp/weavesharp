package com.weavesharp.rider

import com.intellij.openapi.Disposable
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Disposer
import com.intellij.xdebugger.XDebugProcess
import com.intellij.xdebugger.XDebugSession
import com.intellij.xdebugger.XDebugSessionListener
import com.intellij.xdebugger.XDebuggerManager
import com.intellij.xdebugger.XDebuggerManagerListener
import java.util.IdentityHashMap

data class DebugLocation(val file: String, val line: Int)
data class DebugObservation(val session: Long, val event: String, val position: DebugLocation?)

@Service(Service.Level.PROJECT)
class DebugObserver(project: Project) : Disposable {
    private data class Attachment(val id: Long, val lifetime: Disposable)
    private val logger = Logger.getInstance(DebugObserver::class.java)
    private val sessions = IdentityHashMap<XDebugSession, Attachment>()
    private val recent = ArrayDeque<DebugObservation>()
    private var serial = 0L

    init {
        val manager = XDebuggerManager.getInstance(project)
        project.messageBus.connect(this).subscribe(XDebuggerManager.TOPIC, object : XDebuggerManagerListener {
            override fun processStarted(debugProcess: XDebugProcess) { attach(debugProcess.session, "started") }
            override fun processStopped(debugProcess: XDebugProcess) { stop(debugProcess.session) }
        })
        manager.debugSessions.forEach { attach(it, "attached") }
    }

    @Synchronized private fun attach(session: XDebugSession, event: String) {
        if (sessions.containsKey(session)) return
        val lifetime = Disposer.newDisposable("WeaveSharp debugger observation")
        Disposer.register(this, lifetime)
        sessions[session] = Attachment(++serial, lifetime)
        session.addSessionListener(object : XDebugSessionListener {
            override fun sessionPaused() { record(session, "paused", true) }
            override fun stackFrameChanged() { record(session, "frame-changed", session.isPaused) }
            override fun sessionResumed() { record(session, "resumed", false) }
            override fun sessionStopped() { stop(session) }
        }, lifetime)
        record(session, event, false)
        if (session.isPaused) record(session, "paused", true)
    }

    @Synchronized private fun record(session: XDebugSession, event: String, locate: Boolean) {
        val attachment = sessions[session] ?: return
        val position = if (locate) session.currentPosition?.let { DebugLocation(it.file.url, it.line) } else null
        val observation = DebugObservation(attachment.id, event, position)
        if (recent.size == 256) recent.removeFirst()
        recent.addLast(observation)
        logger.info("Rider graph trial: $observation")
    }

    @Synchronized private fun stop(session: XDebugSession) {
        record(session, "stopped", false)
        val attachment = sessions.remove(session) ?: return
        Disposer.dispose(attachment.lifetime)
    }

    @Synchronized fun observations(): List<DebugObservation> = recent.toList()
    @Synchronized override fun dispose() { sessions.clear(); recent.clear() }
}
