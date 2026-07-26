"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Plus, FileText, Eye, Clock, Inbox } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

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
            Home
          </Link>
          <Link href="/forms/create">
            <Button>
              <Plus className="w-4 h-4" />
              New Form
            </Button>
          </Link>
        </div>
      </header>

      <main className="px-6 py-8">
        {loading ? (
          <div className="flex gap-4 overflow-x-auto pb-4">
            {[1, 2, 3].map((i) => (
              <Card key={i} className="animate-pulse shrink-0 w-72">
                <CardContent className="p-5">
                  <div className="h-5 bg-slate-200 rounded w-2/3 mb-3" />
                  <div className="h-4 bg-slate-100 rounded w-full mb-2" />
                  <div className="h-4 bg-slate-100 rounded w-1/2" />
                </CardContent>
              </Card>
            ))}
          </div>
        ) : forms.length === 0 ? (
          <Card className="text-center py-16 max-w-md mx-auto">
            <CardContent>
              <FileText className="w-12 h-12 text-slate-300 mx-auto mb-4" />
              <p className="text-slate-500 font-medium">No forms yet</p>
              <p className="text-sm text-slate-400 mt-1">
                Create your first dynamic form to get started.
              </p>
              <Link href="/forms/create">
                <Button className="mt-4">
                  <Plus className="w-4 h-4" />
                  Create Form
                </Button>
              </Link>
            </CardContent>
          </Card>
        ) : (
          <div className="flex gap-3 overflow-x-auto pb-4">
            {forms.map((form) => (
              <Card key={form.id} className="shrink-0 w-64 hover:shadow-md transition-shadow">
                <CardContent className="p-4">
                  <div className="flex items-start justify-between mb-2">
                    <div className="w-8 h-8 rounded-lg bg-indigo-100 flex items-center justify-center">
                      <FileText className="w-4 h-4 text-indigo-600" />
                    </div>
                    <div className="flex items-center gap-0.5">
                      <Link href={`/forms/${form.id}/submissions`}>
                        <Button variant="ghost" size="icon" className="w-7 h-7 text-slate-400 hover:text-indigo-600" title="View submissions">
                          <Inbox className="w-3.5 h-3.5" />
                        </Button>
                      </Link>
                      <Link href={`/forms/${form.id}/fill`}>
                        <Button variant="ghost" size="icon" className="w-7 h-7 text-slate-400 hover:text-indigo-600" title="Fill form">
                          <Eye className="w-3.5 h-3.5" />
                        </Button>
                      </Link>
                    </div>
                  </div>
                  <Link
                    href={`/forms/${form.id}/edit`}
                    className="text-sm font-semibold text-slate-800 hover:text-indigo-600 transition-colors line-clamp-1"
                  >
                    {form.title}
                  </Link>
                  {form.description && (
                    <p className="text-xs text-slate-500 mt-0.5 line-clamp-2">
                      {form.description}
                    </p>
                  )}
                  <div className="flex items-center gap-3 mt-3 text-xs text-slate-400">
                    <Link
                      href={`/forms/${form.id}/submissions`}
                      className="flex items-center gap-1 hover:text-indigo-600 transition-colors"
                    >
                      <Inbox className="w-3 h-3" />
                      {form._count.submissions} submissions
                    </Link>
                    <span className="flex items-center gap-1">
                      <Clock className="w-3 h-3" />
                      {new Date(form.updatedAt).toLocaleDateString()}
                    </span>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
