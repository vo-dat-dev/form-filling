"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Plus, FileText, Trash2, Eye, Clock } from "lucide-react";

interface FormListItem {
  id: string;
  title: string;
  description: string | null;
  createdAt: string;
  updatedAt: string;
  _count: { submissions: number };
}

export default function FormsPage() {
  const [forms, setForms] = useState<FormListItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchForms();
  }, []);

  async function fetchForms() {
    try {
      const res = await fetch("/api/forms");
      if (res.ok) setForms(await res.json());
    } catch (e) {
      console.error("Failed to fetch forms", e);
    } finally {
      setLoading(false);
    }
  }

  async function deleteForm(id: string) {
    if (!confirm("Delete this form and all its submissions?")) return;
    try {
      const res = await fetch(`/api/forms/${id}`, { method: "DELETE" });
      if (res.ok) setForms(forms.filter((f) => f.id !== id));
    } catch (e) {
      console.error("Failed to delete form", e);
    }
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="bg-white border-b border-slate-200 px-6 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Forms</h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Create and manage dynamic forms
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Link
            href="/"
            className="text-sm text-slate-500 hover:text-slate-800 transition-colors"
          >
            ← Home
          </Link>
          <Link
            href="/forms/create"
            className="inline-flex items-center gap-1.5 px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-lg hover:bg-indigo-700 transition-colors"
          >
            <Plus className="w-4 h-4" />
            New Form
          </Link>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-6 py-8">
        {loading ? (
          <div className="text-center text-slate-400 py-16">Loading...</div>
        ) : forms.length === 0 ? (
          <div className="text-center py-16">
            <FileText className="w-12 h-12 text-slate-300 mx-auto mb-4" />
            <p className="text-slate-500 font-medium">No forms yet</p>
            <p className="text-sm text-slate-400 mt-1">
              Create your first dynamic form to get started.
            </p>
            <Link
              href="/forms/create"
              className="inline-flex items-center gap-1.5 mt-4 px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-lg hover:bg-indigo-700 transition-colors"
            >
              <Plus className="w-4 h-4" />
              Create Form
            </Link>
          </div>
        ) : (
          <div className="space-y-3">
            {forms.map((form) => (
              <div
                key={form.id}
                className="bg-white rounded-lg border border-slate-200 p-5 flex items-center justify-between hover:shadow-sm transition-shadow"
              >
                <div className="flex-1 min-w-0">
                  <Link
                    href={`/forms/${form.id}/edit`}
                    className="text-base font-medium text-slate-800 hover:text-indigo-600 transition-colors"
                  >
                    {form.title}
                  </Link>
                  {form.description && (
                    <p className="text-sm text-slate-500 mt-0.5 truncate">
                      {form.description}
                    </p>
                  )}
                  <div className="flex items-center gap-4 mt-2 text-xs text-slate-400">
                    <span className="flex items-center gap-1">
                      <FileText className="w-3.5 h-3.5" />
                      {form._count.submissions} submissions
                    </span>
                    <span className="flex items-center gap-1">
                      <Clock className="w-3.5 h-3.5" />
                      {new Date(form.updatedAt).toLocaleDateString()}
                    </span>
                  </div>
                </div>
                <div className="flex items-center gap-2 ml-4">
                  <Link
                    href={`/forms/${form.id}/fill`}
                    className="p-2 text-slate-400 hover:text-indigo-600 hover:bg-indigo-50 rounded-lg transition-colors"
                    title="Fill form"
                  >
                    <Eye className="w-4 h-4" />
                  </Link>
                  <Link
                    href={`/forms/${form.id}/submissions`}
                    className="p-2 text-slate-400 hover:text-slate-600 hover:bg-slate-100 rounded-lg transition-colors text-xs"
                    title="View submissions"
                  >
                    Data
                  </Link>
                  <button
                    onClick={() => deleteForm(form.id)}
                    className="p-2 text-slate-400 hover:text-red-500 hover:bg-red-50 rounded-lg transition-colors"
                    title="Delete form"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
