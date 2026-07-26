"use client";

import { use, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Download } from "lucide-react";

interface Submission {
  id: string;
  data: Record<string, unknown>;
  createdAt: string;
}

interface FormInfo {
  id: string;
  title: string;
  fields: { id: string; label: string; type: string }[];
}

export default function SubmissionsPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);
  const [form, setForm] = useState<FormInfo | null>(null);
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      fetch(`/api/forms/${id}`).then((r) => r.json()),
      fetch(`/api/forms/${id}/submissions`).then((r) => r.json()),
    ])
      .then(([formData, submissionsData]) => {
        setForm(formData);
        setSubmissions(submissionsData);
      })
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [id]);

  function exportCsv() {
    if (!form || submissions.length === 0) return;
    const headers = ["Submitted At", ...form.fields.map((f) => f.label)];
    const rows = submissions.map((s) => {
      const date = new Date(s.createdAt).toLocaleString();
      const vals = form.fields.map((f) => {
        const v = s.data[f.id];
        return v !== undefined && v !== null ? `"${String(v)}"` : '""';
      });
      return [date, ...vals].join(",");
    });
    const csv = [headers.join(","), ...rows].join("\n");
    const blob = new Blob([csv], { type: "text/csv" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${form.title.replace(/\s+/g, "_")}_submissions.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  if (loading) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center text-slate-400">
        Loading...
      </div>
    );
  }

  if (!form) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center text-red-500">
        Form not found
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="bg-white border-b border-slate-200 px-6 py-4">
        <div className="max-w-5xl mx-auto">
          <Link
            href="/forms"
            className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-800 mb-2 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to Forms
          </Link>
          <div className="flex items-center justify-between">
            <div>
              <h1 className="text-xl font-semibold text-slate-800">
                {form.title}
              </h1>
              <p className="text-sm text-slate-500 mt-0.5">
                {submissions.length} submission
                {submissions.length !== 1 ? "s" : ""}
              </p>
            </div>
            {submissions.length > 0 && (
              <button
                onClick={exportCsv}
                className="inline-flex items-center gap-1.5 px-3 py-2 bg-white border border-slate-200 text-slate-700 text-sm font-medium rounded-lg hover:bg-slate-50 transition-colors"
              >
                <Download className="w-4 h-4" />
                Export CSV
              </button>
            )}
          </div>
        </div>
      </header>

      <main className="max-w-5xl mx-auto px-6 py-8">
        {submissions.length === 0 ? (
          <div className="text-center py-16 text-slate-400">
            <p className="font-medium">No submissions yet</p>
            <p className="text-sm mt-1">
              Submissions will appear here once users fill out the form.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200">
                  <th className="text-left py-2 px-3 text-slate-500 font-medium whitespace-nowrap">
                    #
                  </th>
                  {form.fields.map((f) => (
                    <th
                      key={f.id}
                      className="text-left py-2 px-3 text-slate-500 font-medium whitespace-nowrap"
                    >
                      {f.label}
                    </th>
                  ))}
                  <th className="text-left py-2 px-3 text-slate-500 font-medium whitespace-nowrap">
                    Submitted At
                  </th>
                </tr>
              </thead>
              <tbody>
                {submissions.map((s, idx) => (
                  <tr
                    key={s.id}
                    className="border-b border-slate-100 hover:bg-slate-50 cursor-pointer"
                    onClick={() =>
                      (window.location.href = `/forms/${form.id}/submissions/${s.id}`)
                    }
                  >
                    <td className="py-2 px-3 text-slate-400">{idx + 1}</td>
                    {form.fields.map((f) => (
                      <td
                        key={f.id}
                        className="py-2 px-3 text-slate-700 max-w-[200px] truncate"
                      >
                        {formatValue(s.data[f.id])}
                      </td>
                    ))}
                    <td className="py-2 px-3 text-slate-400 whitespace-nowrap">
                      {new Date(s.createdAt).toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </main>
    </div>
  );
}

function formatValue(v: unknown): string {
  if (v === null || v === undefined) return "-";
  if (typeof v === "boolean") return v ? "Yes" : "No";
  if (Array.isArray(v)) return v.join(", ");
  return String(v);
}
