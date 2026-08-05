"use client";

import { useThreads, useCopilotChatConfiguration } from "@copilotkit/react-core/v2";
import { useEffect, useState } from "react";
import { Plus, Trash2, Pencil, Check, X } from "lucide-react";



interface ThreadsDrawerProps {
  agentId?: string;
}

function getThreadParam(): string | null {
  if (typeof window === "undefined") return null;
  return new URLSearchParams(window.location.search).get("threadId");
}

function setThreadParam(threadId: string | null) {
  if (typeof window === "undefined") return;
  const params = new URLSearchParams(window.location.search);
  if (threadId) {
    params.set("threadId", threadId);
  } else {
    params.delete("threadId");
  }
  const qs = params.toString();
  const url = qs ? `${window.location.pathname}?${qs}` : window.location.pathname;
  window.history.replaceState(null, "", url);
}

export function ThreadsDrawer({ agentId = "minerU" }: ThreadsDrawerProps) {
  const config = useCopilotChatConfiguration();
  const {
    threads,
    isLoading,
    renameThread,
    archiveThread,
    deleteThread,
  } = useThreads({ agentId });
  
  const [activeThreadId, setActiveThreadId] = useState<string | null>(null);
  const [editId, setEditId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");

  useEffect(() => {
    const threadFromUrl = getThreadParam();
    if (threadFromUrl && config) {
      config.setActiveThreadId(threadFromUrl);
      setActiveThreadId(threadFromUrl);
    }
  }, [config]);

  function handleNew() {
    setThreadParam(null);
    config?.startNewThread();
    setActiveThreadId(null);
  }

  async function handleDelete(id: string, e: React.MouseEvent) {
    e.stopPropagation();
    await deleteThread(id);
    if (activeThreadId === id) {
      setThreadParam(null);
      config?.startNewThread();
      setActiveThreadId(null);
    }
  }

  async function handleRename(id: string) {
    if (!editTitle.trim()) return;
    await renameThread(id, editTitle.trim());
    setEditId(null);
  }

  function selectThread(id: string) {
    setThreadParam(id);
    config?.setActiveThreadId(id);
    setActiveThreadId(id);
  }

  return (
    <aside className="w-full bg-white border-r border-slate-200 flex flex-col h-full">
      <div className="p-3 border-b border-slate-200">
        <button
          onClick={handleNew}
          className="w-full flex items-center gap-2 px-3 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 transition-colors"
        >
          <Plus className="w-4 h-4" />
          New Conversation
        </button>
      </div>

      <nav className="flex-1 overflow-y-auto p-2 space-y-1">
        {isLoading && (
          <div className="text-center py-4 text-sm text-slate-400">Loading threads...</div>
        )}
        {threads.map((thread) => (
          <div
            key={thread.id}
            onClick={() => selectThread(thread.id)}
            className={`group flex items-center gap-2 px-3 py-2 rounded-lg text-sm cursor-pointer transition-colors ${activeThreadId === thread.id
                ? "bg-indigo-50 text-indigo-700 font-medium"
                : "text-slate-600 hover:bg-slate-100"
              }`}
          >
            {editId === thread.id ? (
              <form
                onSubmit={(e) => { e.preventDefault(); handleRename(thread.id); }}
                className="flex items-center gap-1 flex-1 min-w-0"
                onClick={(e) => e.stopPropagation()}
              >
                <input
                  value={editTitle}
                  onChange={(e) => setEditTitle(e.target.value)}
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
              <>
                <span className="flex-1 truncate">{thread.name ?? "Untitled"}</span>
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    setEditId(thread.id);
                    setEditTitle(thread.name ?? "Untitled");
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
