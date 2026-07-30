"use client";

import { useCopilotChatConfiguration } from "@copilotkit/react-core/v2";
import { useEffect, useState, useCallback } from "react";
import { Plus, Trash2, Pencil, Check, X } from "lucide-react";

interface Thread {
  id: string;
  agentId: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

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
  const [threads, setThreads] = useState<Thread[]>([]);
  const [activeThreadId, setActiveThreadId] = useState<string | null>(null);
  const [editId, setEditId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");

  const fetchThreads = useCallback(async () => {
    try {
      const res = await fetch(`/api/threads?agentId=${agentId}`);
      if (res.ok) setThreads(await res.json());
    } catch { /* ignore */ }
  }, [agentId]);

  useEffect(() => {
    fetchThreads();
  }, [fetchThreads]);

  useEffect(() => {
    const threadFromUrl = getThreadParam();
    if (threadFromUrl && config) {
      config.setActiveThreadId(threadFromUrl);
      setActiveThreadId(threadFromUrl);
    }
  }, []);

  async function handleNew() {
    try {
      const res = await fetch("/api/threads", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ agentId }),
      });
      if (res.ok) {
        const thread: Thread = await res.json();
        setThreads((prev) => [thread, ...prev]);
        setThreadParam(thread.id);
        config?.setActiveThreadId(thread.id);
        setActiveThreadId(thread.id);
      }
    } catch { /* ignore */ }
  }

  async function handleDelete(id: string, e: React.MouseEvent) {
    e.stopPropagation();
    try {
      const res = await fetch(`/api/threads/${id}`, { method: "DELETE" });
      if (res.ok) {
        setThreads((prev) => prev.filter((t) => t.id !== id));
        if (activeThreadId === id) {
          setThreadParam(null);
          config?.startNewThread();
          setActiveThreadId(null);
        }
      }
    } catch { /* ignore */ }
  }

  async function handleRename(id: string) {
    if (!editTitle.trim()) return;
    try {
      const res = await fetch(`/api/threads/${id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title: editTitle.trim() }),
      });
      if (res.ok) {
        const updated: Thread = await res.json();
        setThreads((prev) => prev.map((t) => (t.id === id ? updated : t)));
      }
    } catch { /* ignore */ }
    setEditId(null);
  }

  function selectThread(id: string) {
    setThreadParam(id);
    config?.setActiveThreadId(id);
    setActiveThreadId(id);
  }

  return (
    <aside className="w-64 bg-white border-r border-slate-200 flex flex-col h-full">
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
        {threads.map((thread) => (
          <div
            key={thread.id}
            onClick={() => selectThread(thread.id)}
            className={`group flex items-center gap-2 px-3 py-2 rounded-lg text-sm cursor-pointer transition-colors ${
              activeThreadId === thread.id
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
                <span className="flex-1 truncate">{thread.title}</span>
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    setEditId(thread.id);
                    setEditTitle(thread.title);
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
