"use client";

import {
  CopilotChatConfigurationProvider,
  CopilotSidebar,
  useRenderTool,
} from "@copilotkit/react-core/v2";
import { useCoAgent } from "@copilotkit/react-core";
import { ThreadsDrawer } from "@/components/threads-drawer";
import { useEffect, useState, useRef, useLayoutEffect, type ReactNode } from "react";
import { CheckCircle, FileSearch, ClipboardList, PenLine, Loader2, Plus, Trash2 } from "lucide-react";
import type { FormField } from "@/lib/types";
import styles from "./page.module.css";

interface FormFillState {
  formId?: string;
  formTitle?: string;
  filledValues?: Record<string, unknown>;
  fills?: Array<{
    formId: string;
    formTitle: string;
    filledValues: Record<string, unknown>;
  }>;
}

interface FormData {
  id: string;
  title: string;
  description: string | null;
  fields: FormField[];
}

export default function HomePage() {
  return (
    <CopilotChatConfigurationProvider agentId="formFill">
      <div className={`${styles.layout} threadsLayout`}>
        <ThreadsDrawer agentId="formFill" />
        <div className={styles.mainPanel}>
          <div className="h-screen flex flex-col bg-slate-50">
            <header className="flex items-center gap-4 px-6 py-3 bg-white border-b border-slate-200 shadow-sm">
              <h1 className="text-lg font-semibold text-slate-800">
                Document Assistant
              </h1>
              <span className="text-xs text-slate-400 ml-auto">
                Upload PDF, Word, Excel, PowerPoint, or images
              </span>
            </header>

            <main className="flex-1 flex relative overflow-hidden">
              <FormFillContent />
              <CopilotSidebar
                defaultOpen={true}
                labels={{
                  modalHeaderTitle: "Document Assistant",
                  welcomeMessageText:
                    "👋 Upload a document (PDF, Word, image…) and I'll extract its content, find the best matching form, and fill it in for you.",
                }}
                attachments={{ enabled: true, maxSize: 50 * 1024 * 1024 }}
              />
            </main>
          </div>
        </div>
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
  search_forms: {
    label: "Searching matching forms",
    icon: <ClipboardList className="w-4 h-4" />,
  },
  fill_form: {
    label: "Matching & filling form fields",
    icon: <PenLine className="w-4 h-4" />,
  },
};

function FormFillToolCallCard({
  name,
  status,
  args,
  result,
}: {
  name: string;
  status: ToolStatus;
  args?: Record<string, unknown>;
  result?: unknown;
}) {
  const meta = TOOL_META[name];
  const isRunning = status === "inProgress" || status === "executing";

  const ocrSummary =
    status === "complete" &&
      name === "parse_documents" &&
      typeof result === "string" &&
      result.trim().length > 0 &&
      !result.startsWith("No ")
      ? result.replace(/\s+/g, " ").trim().slice(0, 160) +
      (result.length > 160 ? "…" : "")
      : null;

  const formsResult =
    status === "complete" && name === "search_forms" && Array.isArray(result)
      ? (result as Array<Record<string, unknown>>)
      : null;

  const formTitle =
    name === "fill_form" && !!args?.formTitle
      ? String(args.formTitle)
      : null;

  return (
    <div className="rounded-lg bg-slate-100 text-sm text-slate-700 my-1 overflow-hidden">
      <div className="flex items-center gap-2.5 px-3 py-2">
        <span className={isRunning ? "text-indigo-500" : "text-emerald-500"}>
          {isRunning ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            meta?.icon ?? <CheckCircle className="w-4 h-4" />
          )}
        </span>
        <span className="flex-1">{meta?.label ?? name}</span>
        {isRunning && (
          <span className="text-xs text-indigo-400">running…</span>
        )}
      </div>
      {ocrSummary && (
        <div className="px-3 pb-2.5 border-t border-slate-200 pt-2">
          <p className="text-xs text-slate-500 leading-relaxed">{ocrSummary}</p>
        </div>
      )}
      {formsResult && formsResult.length > 0 && (
        <div className="px-3 pb-2.5 border-t border-slate-200 pt-2 space-y-1.5">
          <p className="text-xs font-medium text-slate-600 mb-1">Matching forms:</p>
          {formsResult.map((f, i) => {
            const title = f.title as string;
            const desc = f.description as string | undefined;
            return (
              <div key={i} className="flex items-center justify-between text-xs bg-white rounded px-2 py-1.5 border border-slate-200">
                <div className="min-w-0">
                  <span className="font-medium text-slate-700">{title}</span>
                  {desc && (
                    <span className="text-slate-400 ml-1">— {desc.slice(0, 50)}</span>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
      {status === "complete" && formTitle && (
        <div className="px-3 pb-2.5 border-t border-slate-200 pt-2">
          <p className="text-xs text-slate-500">
            Matched form:{" "}
            <span className="font-medium text-slate-700">{formTitle}</span>
          </p>
        </div>
      )}
    </div>
  );
}

function FormFillContent() {
  const { state } = useCoAgent<FormFillState>({ name: "formFill" });

  useRenderTool(
    {
      name: "*",
      agentId: "formFill",
      render: ({ name, args, status, result }) => (
        <FormFillToolCallCard
          name={name}
          status={status as ToolStatus}
          args={args as Record<string, unknown>}
          result={result}
        />
      ),
    },
    [],
  );
  const [formsList, setFormsList] = useState<FormData[]>([]);
  const [formsValues, setFormsValues] = useState<Record<string, Record<string, unknown>>>({});
  const [formsSubmitted, setFormsSubmitted] = useState<Record<string, boolean>>({});
  const [formsSubmitting, setFormsSubmitting] = useState<Record<string, boolean>>({});
  const [formsErrors, setFormsErrors] = useState<Record<string, Record<string, string>>>({});

  useEffect(() => {
    const fills = state?.fills;
    const singleFormId = state?.formId;

    // Use fills array if available, otherwise fall back to single formId
    const formEntries = fills?.length
      ? fills.map((f) => ({ formId: f.formId, filledValues: f.filledValues }))
      : singleFormId
        ? [{ formId: singleFormId, filledValues: state?.filledValues }]
        : [];

    if (formEntries.length === 0) {
      setFormsList([]);
      setFormsValues({});
      setFormsSubmitted({});
      setFormsErrors({});
      return;
    }

    // Fetch all forms in parallel
    Promise.all(
      formEntries.map((entry) =>
        fetch(`/api/forms/${entry.formId}`).then((r) => r.json()) as Promise<FormData>
      )
    ).then((fetchedForms) => {
      setFormsList(fetchedForms);
      setFormsSubmitted({});
      setFormsErrors({});

      setFormsValues((prev) => {
        const next = { ...prev };
        for (let i = 0; i < fetchedForms.length; i++) {
          const form = fetchedForms[i];
          if (next[form.id]) continue; // preserve existing values
          const filled = formEntries[i]?.filledValues;
          const init: Record<string, unknown> = {};
          for (const f of form.fields) {
            const pre = filled?.[f.id];
            init[f.id] = pre ?? (f.type === "checkbox" || f.type === "list" ? [] : "");
          }
          next[form.id] = init;
        }
        return next;
      });
    }).catch(console.error);
  }, [state?.formId, state?.filledValues, state?.fills]);

  function validate(formId: string): boolean {
    const form = formsList.find((f) => f.id === formId);
    if (!form) return false;
    const errs: Record<string, string> = {};
    const vals = formsValues[formId] ?? {};
    for (const field of form.fields) {
      if (field.required) {
        const val = vals[field.id];
        if (!val || (typeof val === "string" && !val.trim()) || (Array.isArray(val) && val.length === 0)) {
          errs[field.id] = `${field.label} is required`;
        }
      }
    }
    setFormsErrors((prev) => ({ ...prev, [formId]: errs }));
    return Object.keys(errs).length === 0;
  }

  async function handleSubmit(formId: string, e: React.FormEvent) {
    e.preventDefault();
    if (!validate(formId)) return;
    setFormsSubmitting((prev) => ({ ...prev, [formId]: true }));
    try {
      const res = await fetch(`/api/forms/${formId}/submissions`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(formsValues[formId] ?? {}),
      });
      if (res.ok) setFormsSubmitted((prev) => ({ ...prev, [formId]: true }));
      else alert("Submission failed");
    } catch {
      alert("Submission failed");
    } finally {
      setFormsSubmitting((prev) => ({ ...prev, [formId]: false }));
    }
  }

  function setValue(formId: string, fieldId: string, val: unknown) {
    setFormsValues((prev) => ({
      ...prev,
      [formId]: { ...(prev[formId] ?? {}), [fieldId]: val },
    }));
    if (formsErrors[formId]?.[fieldId]) {
      setFormsErrors((prev) => {
        const next = { ...prev };
        if (next[formId]) {
          const errs = { ...next[formId] };
          delete errs[fieldId];
          next[formId] = errs;
        }
        return next;
      });
    }
  }

  if (formsList.length === 0) {
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

  return (
    <div className="flex-1 overflow-y-auto">
      <div className="columns-1 sm:columns-2 gap-4 px-6 py-8 [&>*]:break-inside-avoid [&>*]:mb-4">
        {formsList.map((form) => {
          const formId = form.id;
          const values = formsValues[formId] ?? {};
          const submitted = formsSubmitted[formId];
          const submitting = formsSubmitting[formId];
          const errors = formsErrors[formId] ?? {};

          if (submitted) {
            return (
              <div key={formId} className="rounded-lg border border-green-200 bg-green-50 p-6 text-center">
                <CheckCircle className="w-10 h-10 text-green-500 mx-auto mb-3" />
                <h3 className="text-base font-semibold text-green-800">{form.title}</h3>
                <p className="text-sm text-green-600 mt-1">Đã gửi thành công!</p>
              </div>
            );
          }

          return (
            <div key={formId} className="bg-white rounded-lg border border-slate-200 overflow-hidden">
              <div className="px-5 py-4 border-b border-slate-100">
                <h2 className="text-base font-semibold text-slate-800">{form.title}</h2>
                {form.description && <FormDescription text={form.description} />}
              </div>

              <form onSubmit={(e) => handleSubmit(formId, e)} className="px-5 py-4 space-y-4">
                {[...form.fields]
                  .sort((a, b) => a.order - b.order)
                  .map((field) => (
                    <FormFieldInput
                      key={field.id}
                      field={field}
                      value={values[field.id]}
                      error={errors[field.id]}
                      onChange={(val) => setValue(formId, field.id, val)}
                    />
                  ))}

                <button
                  type="submit"
                  disabled={submitting}
                  className="w-full py-2.5 bg-indigo-600 text-white text-sm font-medium rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  {submitting ? "Submitting..." : `Submit ${form.title}`}
                </button>
              </form>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function FormDescription({ text }: { text: string }) {
  const [expanded, setExpanded] = useState(false);
  const [isClamped, setIsClamped] = useState(false);
  const ref = useRef<HTMLParagraphElement>(null);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    setIsClamped(el.scrollHeight > el.clientHeight);
  }, [text]);

  return (
    <div className="mt-0.5">
      <p
        ref={ref}
        className={`text-xs text-slate-500 leading-relaxed ${expanded ? "" : "line-clamp-3"}`}
      >
        {text}
      </p>
      {isClamped && (
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="text-xs text-indigo-600 hover:text-indigo-800 mt-1 font-medium"
        >
          {expanded ? "Thu gọn" : "Xem thêm"}
        </button>
      )}
    </div>
  );
}

function RepeatingListInput({
  subFields,
  value,
  onChange,
}: {
  subFields: FormField[];
  value: Record<string, unknown>[];
  onChange: (val: unknown) => void;
}) {
  function addItem() {
    const item: Record<string, unknown> = {};
    subFields.forEach((sf) => {
      if (sf.type === "checkbox") item[sf.id] = [];
      else item[sf.id] = "";
    });
    onChange([...value, item]);
  }

  function removeItem(idx: number) {
    onChange(value.filter((_, i) => i !== idx));
  }

  function updateItem(itemIdx: number, fieldId: string, val: unknown) {
    onChange(
      value.map((item, i) =>
        i === itemIdx ? { ...item, [fieldId]: val } : item
      )
    );
  }

  return (
    <div className="space-y-3">
      {value.map((item, itemIdx) => (
        <div
          key={itemIdx}
          className="border border-slate-200 rounded-lg p-4 space-y-3"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-slate-500">
              Item {itemIdx + 1}
            </span>
            <button
              type="button"
              onClick={() => removeItem(itemIdx)}
              className="p-1 text-slate-300 hover:text-red-500"
            >
              <Trash2 className="w-4 h-4" />
            </button>
          </div>
          {subFields.map((sf) => (
            <FormFieldInput
              key={sf.id}
              field={sf}
              value={item[sf.id]}
              onChange={(val) => updateItem(itemIdx, sf.id, val)}
            />
          ))}
        </div>
      ))}
      <button
        type="button"
        onClick={addItem}
        className="flex items-center gap-2 px-3 py-2 text-sm text-indigo-600 border border-dashed border-indigo-300 rounded-lg hover:bg-indigo-50 transition-colors w-full justify-center"
      >
        <Plus className="w-4 h-4" />
        Add Item
      </button>
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
  const inputClass = `w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${error ? "border-red-300 bg-red-50" : "border-slate-300"
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

      case "list":
        return (
          <RepeatingListInput
            subFields={field.subFields ?? []}
            value={Array.isArray(value) ? (value as Record<string, unknown>[]) : []}
            onChange={onChange}
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
