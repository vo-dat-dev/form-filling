"use client";

import {
  CopilotChatConfigurationProvider,
  CopilotSidebar,
  useRenderTool,
} from "@copilotkit/react-core/v2";
import { useCoAgent } from "@copilotkit/react-core";
import Link from "next/link";
import { useEffect, useState, type ReactNode } from "react";
import { CheckCircle, FileSearch, ClipboardList, PenLine, Loader2 } from "lucide-react";
import type { FormField } from "@/lib/types";

interface MinerUState {
  formId?: string;
  formTitle?: string;
  filledValues?: Record<string, unknown>;
}

interface FormData {
  id: string;
  title: string;
  description: string | null;
  fields: FormField[];
}

export default function MinerUPage() {
  return (
    <CopilotChatConfigurationProvider agentId="minerU">
      <div className="h-screen flex flex-col bg-slate-50">
        <header className="flex items-center gap-4 px-6 py-3 bg-white border-b border-slate-200 shadow-sm">
          <Link
            href="/"
            className="text-slate-500 hover:text-slate-800 text-sm transition-colors"
          >
            ← Back
          </Link>
          <h1 className="text-lg font-semibold text-slate-800">
            MinerU Document Assistant
          </h1>
          <span className="text-xs text-slate-400 ml-auto">
            Upload PDF, Word, Excel, PowerPoint, or images
          </span>
        </header>

        <main className="flex-1 flex relative overflow-hidden">
          <MinerUContent />
          <CopilotSidebar
            defaultOpen={true}
            labels={{
              modalHeaderTitle: "MinerU Assistant",
              welcomeMessageText:
                "👋 Upload a document (PDF, Word, image…) and I'll extract its content, find the best matching form, and fill it in for you.",
            }}
            attachments={{ enabled: true, maxSize: 50 * 1024 * 1024 }}
          />
        </main>
      </div>
    </CopilotChatConfigurationProvider>
  );
}

type ToolStatus = "inProgress" | "executing" | "complete";

const TOOL_META: Record<string, { label: string; icon: ReactNode }> = {
  parse_documents: {
    label: "Extracting document content",
    icon: <FileSearch className="w-4 h-4" />,
  },
  get_forms: {
    label: "Fetching available forms",
    icon: <ClipboardList className="w-4 h-4" />,
  },
  fill_form: {
    label: "Matching & filling form fields",
    icon: <PenLine className="w-4 h-4" />,
  },
};

function MinerUToolCallCard({
  name,
  status,
  args,
}: {
  name: string;
  status: ToolStatus;
  args?: Record<string, unknown>;
}) {
  const meta = TOOL_META[name];
  const isRunning = status === "inProgress" || status === "executing";

  return (
    <div className="flex items-center gap-2.5 px-3 py-2 rounded-lg bg-slate-100 text-sm text-slate-700 my-1">
      <span className={isRunning ? "text-indigo-500" : "text-emerald-500"}>
        {isRunning ? (
          <Loader2 className="w-4 h-4 animate-spin" />
        ) : (
          meta?.icon ?? <CheckCircle className="w-4 h-4" />
        )}
      </span>
      <span className="flex-1">{meta?.label ?? name}</span>
      {status === "complete" && name === "fill_form" && !!args?.formTitle && (
        <span className="text-xs text-slate-400 truncate max-w-[120px]">
          {String(args.formTitle)}
        </span>
      )}
      {isRunning && (
        <span className="text-xs text-indigo-400">running…</span>
      )}
    </div>
  );
}

function MinerUContent() {
  const { state } = useCoAgent<MinerUState>({ name: "minerU" });

  useRenderTool(
    {
      name: "*",
      agentId: "minerU",
      render: ({ name, args, status }) => (
        <MinerUToolCallCard
          name={name}
          status={status as ToolStatus}
          args={args as Record<string, unknown>}
        />
      ),
    },
    [],
  );
  const [form, setForm] = useState<FormData | null>(null);
  const [values, setValues] = useState<Record<string, unknown>>({});
  const [submitted, setSubmitted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!state?.formId) {
      setForm(null);
      return;
    }

    fetch(`/api/forms/${state.formId}`)
      .then((r) => r.json())
      .then((data: FormData) => {
        setForm(data);
        const initial: Record<string, unknown> = {};
        for (const f of data.fields) {
          const pre = state.filledValues?.[f.id];
          initial[f.id] = pre ?? (f.type === "checkbox" ? [] : "");
        }
        setValues(initial);
        setSubmitted(false);
        setErrors({});
      })
      .catch(console.error);
  }, [state?.formId, state?.filledValues]);

  function validate(): boolean {
    const errs: Record<string, string> = {};
    for (const field of form?.fields ?? []) {
      if (field.required) {
        const val = values[field.id];
        if (!val || (typeof val === "string" && !val.trim())) {
          errs[field.id] = `${field.label} is required`;
        }
      }
    }
    setErrors(errs);
    return Object.keys(errs).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!form || !validate()) return;
    setSubmitting(true);
    try {
      const res = await fetch(`/api/forms/${form.id}/submissions`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(values),
      });
      if (res.ok) setSubmitted(true);
      else alert("Submission failed");
    } catch {
      alert("Submission failed");
    } finally {
      setSubmitting(false);
    }
  }

  if (!form) {
    return (
      <div className="flex-1 flex items-center justify-center text-center text-slate-400 select-none">
        <div>
          <p className="text-5xl mb-4">📄</p>
          <p className="text-base font-medium text-slate-500">
            Upload a document and I'll find the right form for you
          </p>
          <p className="text-sm mt-1">
            Supported: PDF · DOCX · PPTX · XLSX · PNG · JPG · WEBP
          </p>
        </div>
      </div>
    );
  }

  if (submitted) {
    return (
      <div className="flex-1 flex items-center justify-center">
        <div className="text-center">
          <CheckCircle className="w-16 h-16 text-green-500 mx-auto mb-4" />
          <h2 className="text-xl font-semibold text-slate-800 mb-1">
            Form Submitted!
          </h2>
          <p className="text-sm text-slate-500">
            Successfully submitted <span className="font-medium">{form.title}</span>.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto">
      <div className="max-w-2xl mx-auto px-6 py-8">
        <div className="mb-6">
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 bg-green-50 text-green-700 text-xs font-medium rounded-full mb-3">
            ✓ Form matched from document
          </span>
          <h2 className="text-xl font-semibold text-slate-800">{form.title}</h2>
          {form.description && (
            <p className="text-sm text-slate-500 mt-1">{form.description}</p>
          )}
        </div>

        <form onSubmit={handleSubmit} className="space-y-5">
          {form.fields
            .sort((a, b) => a.order - b.order)
            .map((field) => (
              <FormFieldInput
                key={field.id}
                field={field}
                value={values[field.id]}
                error={errors[field.id]}
                onChange={(val) =>
                  setValues((prev) => ({ ...prev, [field.id]: val }))
                }
              />
            ))}

          <button
            type="submit"
            disabled={submitting}
            className="w-full py-2.5 bg-indigo-600 text-white text-sm font-medium rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {submitting ? "Submitting..." : "Submit Form"}
          </button>
        </form>
      </div>
    </div>
  );
}

function FormFieldInput({
  field,
  value,
  error,
  onChange,
}: {
  field: FormField;
  value: unknown;
  error?: string;
  onChange: (val: unknown) => void;
}) {
  const inputClass = `w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${
    error ? "border-red-300 bg-red-50" : "border-slate-300"
  }`;

  const strVal = (value as string) ?? "";

  function renderInput() {
    switch (field.type) {
      case "textarea":
        return (
          <textarea
            value={strVal}
            onChange={(e) => onChange(e.target.value)}
            placeholder={field.placeholder}
            rows={4}
            className={inputClass}
          />
        );

      case "select":
        return (
          <select
            value={strVal}
            onChange={(e) => onChange(e.target.value)}
            className={inputClass}
          >
            <option value="">Select...</option>
            {(field.options ?? []).map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        );

      case "checkbox":
        return (
          <div className="space-y-2">
            {(field.options ?? []).map((opt) => (
              <label
                key={opt.value}
                className="flex items-center gap-2 cursor-pointer"
              >
                <input
                  type="checkbox"
                  value={opt.value}
                  checked={((value as string[]) ?? []).includes(opt.value)}
                  onChange={(e) => {
                    const current = (value as string[]) ?? [];
                    onChange(
                      e.target.checked
                        ? [...current, opt.value]
                        : current.filter((v) => v !== opt.value)
                    );
                  }}
                  className="rounded border-slate-300 text-indigo-600 focus:ring-indigo-500"
                />
                <span className="text-sm text-slate-700">{opt.label}</span>
              </label>
            ))}
          </div>
        );

      case "radio":
        return (
          <div className="space-y-2">
            {(field.options ?? []).map((opt) => (
              <label
                key={opt.value}
                className="flex items-center gap-2 cursor-pointer"
              >
                <input
                  type="radio"
                  name={field.id}
                  value={opt.value}
                  checked={strVal === opt.value}
                  onChange={(e) => onChange(e.target.value)}
                  className="border-slate-300 text-indigo-600 focus:ring-indigo-500"
                />
                <span className="text-sm text-slate-700">{opt.label}</span>
              </label>
            ))}
          </div>
        );

      case "date":
        return (
          <input
            type="date"
            value={strVal}
            onChange={(e) => onChange(e.target.value)}
            className={inputClass}
          />
        );

      case "number":
        return (
          <input
            type="number"
            value={strVal}
            onChange={(e) => onChange(e.target.value)}
            placeholder={field.placeholder}
            className={inputClass}
          />
        );

      default:
        return (
          <input
            type={field.type}
            value={strVal}
            onChange={(e) => onChange(e.target.value)}
            placeholder={field.placeholder}
            className={inputClass}
          />
        );
    }
  }

  return (
    <div>
      <label className="block text-sm font-medium text-slate-700 mb-1">
        {field.label}
        {field.required && <span className="text-red-500 ml-0.5">*</span>}
      </label>
      {renderInput()}
      {field.helpText && !error && (
        <p className="mt-1 text-xs text-slate-400">{field.helpText}</p>
      )}
      {error && <p className="mt-1 text-xs text-red-500">{error}</p>}
    </div>
  );
}
