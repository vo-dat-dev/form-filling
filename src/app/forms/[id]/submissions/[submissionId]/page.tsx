"use client";

import { use, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, CheckCircle, XCircle, FileText } from "lucide-react";

interface FieldDef {
  id: string;
  label: string;
  type: string;
  options?: { label: string; value: string }[];
  required?: boolean;
}

interface SubmissionDetail {
  id: string;
  formId: string;
  data: Record<string, unknown>;
  createdAt: string;
  form: {
    id: string;
    title: string;
    fields: FieldDef[];
  };
}

const TYPE_LABELS: Record<string, string> = {
  text: "Text",
  number: "Number",
  email: "Email",
  tel: "Phone",
  textarea: "Textarea",
  select: "Dropdown",
  checkbox: "Checkbox",
  radio: "Radio",
  date: "Date",
  file: "File Upload",
};

export default function SubmissionDetailPage({
  params,
}: {
  params: Promise<{ id: string; submissionId: string }>;
}) {
  const { id, submissionId } = use(params);
  const [submission, setSubmission] = useState<SubmissionDetail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch(`/api/submissions/${submissionId}`)
      .then((r) => r.json())
      .then((data) => setSubmission(data))
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [submissionId]);

  if (loading) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center text-slate-400">
        Loading...
      </div>
    );
  }

  if (!submission) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center text-red-500">
        Submission not found
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="bg-white border-b border-slate-200 px-6 py-4">
        <div className="max-w-3xl mx-auto">
          <Link
            href={`/forms/${id}/submissions`}
            className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-800 mb-2 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to Submissions
          </Link>
          <h1 className="text-xl font-semibold text-slate-800">
            {submission.form.title}
          </h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Submitted {new Date(submission.createdAt).toLocaleString()}
          </p>
        </div>
      </header>

      <main className="max-w-3xl mx-auto px-6 py-8">
        <div className="bg-white rounded-lg border border-slate-200 divide-y divide-slate-100">
          {submission.form.fields
            .sort((a, b) => {
              const fa = submission.form.fields.indexOf(a);
              const fb = submission.form.fields.indexOf(b);
              return fa - fb;
            })
            .map((field) => {
              const value = submission.data[field.id];
              return (
                <div key={field.id} className="px-6 py-4">
                  <div className="flex items-start justify-between">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 mb-1">
                        <span className="text-sm font-medium text-slate-700">
                          {field.label}
                        </span>
                        {field.required && (
                          <span className="text-red-400 text-xs">*</span>
                        )}
                        <span className="text-xs text-slate-400 bg-slate-100 px-1.5 py-0.5 rounded">
                          {TYPE_LABELS[field.type] ?? field.type}
                        </span>
                      </div>
                      <div className="text-sm text-slate-900 mt-1">
                        {renderValue(field, value)}
                      </div>
                    </div>
                  </div>
                </div>
              );
            })}
        </div>
      </main>
    </div>
  );
}

function renderValue(field: FieldDef, value: unknown) {
  if (value === null || value === undefined) {
    return <span className="text-slate-300 italic">—</span>;
  }

  switch (field.type) {
    case "checkbox": {
      const checked = Array.isArray(value)
        ? value
        : typeof value === "boolean"
          ? value
            ? ["true"]
            : []
          : [String(value)];
      if (field.options && field.options.length > 0) {
        return (
          <div className="space-y-1">
            {field.options.map((opt) => {
              const isChecked = checked.includes(opt.value);
              return (
                <div key={opt.value} className="flex items-center gap-2">
                  {isChecked ? (
                    <CheckCircle className="w-4 h-4 text-green-500" />
                  ) : (
                    <XCircle className="w-4 h-4 text-slate-300" />
                  )}
                  <span
                    className={
                      isChecked ? "text-slate-900" : "text-slate-400"
                    }
                  >
                    {opt.label}
                  </span>
                </div>
              );
            })}
          </div>
        );
      }
      return (
        <span
          className={
            value ? "text-green-600 font-medium" : "text-slate-400"
          }
        >
          {String(value)}
        </span>
      );
    }

    case "radio":
    case "select": {
      const option = field.options?.find((o) => o.value === value);
      return (
        <span className="px-2 py-0.5 bg-indigo-50 text-indigo-700 rounded text-sm">
          {option?.label ?? String(value)}
        </span>
      );
    }

    case "file": {
      const file = value as { name?: string; type?: string; size?: number };
      return (
        <div className="flex items-center gap-2 text-indigo-600">
          <FileText className="w-4 h-4" />
          <span>{file.name ?? "Uploaded file"}</span>
          {file.size && (
            <span className="text-xs text-slate-400">
              ({formatSize(file.size)})
            </span>
          )}
        </div>
      );
    }

    case "date":
      return (
        <span className="text-slate-900">{String(value)}</span>
      );

    default:
      return (
        <span className="text-slate-900 whitespace-pre-wrap">
          {Array.isArray(value) ? value.join(", ") : String(value)}
        </span>
      );
  }
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
