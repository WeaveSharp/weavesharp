package com.weavesharp.rider

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.command.WriteCommandAction
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.wm.StatusBar

class InsertStatementAction : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        val editor = event.getData(CommonDataKeys.EDITOR)
        val file = editor?.document?.let { FileDocumentManager.getInstance().getFile(it) }
        event.presentation.isEnabledAndVisible = event.project != null && file?.extension == "cs" && file.isWritable &&
            editor?.document?.isWritable == true
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = event.project ?: return
        val editor = event.getData(CommonDataKeys.EDITOR) ?: return
        val document = editor.document
        val file = FileDocumentManager.getInstance().getFile(document) ?: return
        if (file.extension != "cs" || !file.isWritable || !document.isWritable) return
        var inserted = false
        WriteCommandAction.runWriteCommandAction(project, "Insert sample statement", null, Runnable {
            val snapshot = Snapshot("rider", file.url, document.modificationStamp.toString(), "native-action")
            val context = Context("native-action", "stand-in", "no-compiler", snapshot)
            val projection = StandIn.project(document.text, context, 0)
            val offset = StandIn.insertionOffset(projection, editor.caretModel.offset)
            if (offset != null) { document.insertString(offset, "; "); inserted = true }
        })
        if (!inserted) StatusBar.Info.set("Place the caret inside a statement covered by the sample grammar.", project)
    }
}
