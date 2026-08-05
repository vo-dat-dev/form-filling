"use client";

import { useThreads, useCopilotChatConfiguration } from "@copilotkit/react-core/v2";
import { Plus, Trash2, Pencil, Check, X } from "lucide-react";
import { useState } from "react";

interface ThreadsDrawerProps {
  agentId?: string;
}

export function ThreadsDrawer({ agentId = "minerU" }: ThreadsDrawerProps) {
  const config = useCopilotChatConfiguration();
  const { threads, isLoading, renameThread, deleteThread } = useThreads({ agentId });
  
  const [editId, setEditId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");

  function handleNew() {
    config?.startNewThread();
  }

  async function handleRename(id: string) {
    if (!editName.trim()) return;
    try {
      await renameThread(id, editName.trim());
      setEditId(null);
    } catch (error) {
      console.error('Failed to rename thread:', error);
      // Fallback: call backend API directly
      try {
        const response = await fetch(`/api/copilotkit/threads/${id}`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ name: editName.trim() }),
        });
        if (response.ok) {
          setEditId(null);
          window.location.reload(); // Reload to refresh thread list
        }
      } catch (fallbackError) {
        console.error('Fallback rename also failed:', fallbackError);
      }
    }
  }

  async function handleDelete(id: string, e: React.MouseEvent) {
    e.stopPropagation();
    try {
      await deleteThread(id);
    } catch (error) {
      console.error('Failed to delete thread:', error);
      // Fallback: call backend API directly
      try {
        const response = await fetch(`/api/copilotkit/threads/${id}`, {
          method: "DELETE",
        });
        if (response.ok) {
          window.location.reload(); // Reload to refresh thread list
        }
      } catch (fallbackError) {
        console.error('Fallback delete also failed:', fallbackError);
      }
    }
  }

  function selectThread(id: string) {
    config?.setActiveThreadId(id, { explicit: true });
  }

  if (isLoading) {
    return (
      <aside className="w-full bg-white border-r border-slate-200 flex items-center justify-center h-full">
        <div className="text-sm text-slate-400">Loading threads...</div>
      </aside>
    );
  }

  return (
    <aside className="w-full bg-white border-r border-slate-200 flex flex-col h-full">
      {/* Header */}
      <div className="p-3 border-b border-slate-200">
        <button
          onClick={handleNew}
          className="w-full flex items-center gap-2 px-3 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 transition-colors"
        >
          <Plus className="w-4 h-4" />
          New Conversation
        </button>
      </div>

      {/* Thread List */}
      <nav className="flex-1 overflow-y-auto p-2 space-y-1">
        {threads.length === 0 && (
          <div className="text-center py-8 text-sm text-slate-400">
            No conversations yet
          </div>
        )}
        
        {threads.map((thread) => (
          <div
            key={thread.id}
            onClick={() => selectThread(thread.id)}
            className="group flex items-center gap-2 px-3 py-2 rounded-lg text-sm cursor-pointer transition-colors text-slate-600 hover:bg-slate-100"
          >
            {editId === thread.id ? (
              // Edit mode
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  handleRename(thread.id);
                }}
                className="flex items-center gap-1 flex-1 min-w-0"
                onClick={(e) => e.stopPropagation()}
              >
                <input
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                  className="flex-1 px-1.5 py-0.5 text-sm border border-indigo-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  autoFocus
                />
                <button type="submit" className="p-0.5 text-indigo-600 hover:text-indigo-800">
                  <Check className="w-3.5 h-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => setEditId(null)}
                  className="p-0.5 text-slate-400 hover:text-slate-600"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              </form>
            ) : (
              // View mode
              <>
                <span className="flex-1 truncate">{thread.name ?? "Untitled"}</span>
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    setEditId(thread.id);
                    setEditName(thread.name ?? "Untitled");
                  }}
                  className="p-0.5 opacity-0 group-hover:opacity-100 text-slate-400 hover:text-slate-600 transition-opacity"
                >
                  <Pencil className="w-3.5 h-3.5" />
                </button>
                <button
                  onClick={(e) => handleDelete(thread.id, e)}
                  className="p-0.5 opacity-0 group-hover:opacity-100 text-slate-400 hover:text-red-500 transition-opacity"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                </button>
              </>
            )}
          </div>
        ))}
      </nav>
    </aside>
  );
}
