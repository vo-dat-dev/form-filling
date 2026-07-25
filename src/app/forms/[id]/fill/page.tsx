"use client";

import { use, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { CheckCircle, ArrowLeft } from "lucide-react";
import type { FormField } from "@/lib/types";

interface FormData {
  id: string;
  title: string;
  description: string | null;
  fields: FormField[];
}

export default function FillFormPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);
  const router = useRouter();
  const [form, setForm] = useState<FormData | null>(null);
  const [values, setValues] = useState<Record<string, unknown>>({});
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    fetch(`/api/forms/${id}`)
      .then((r) => r.json())
      .then((data) => {
        setForm(data);
        const initial: Record<string, unknown> = {};
        for (const f of data.fields) {
          if (f.type === "checkbox") initial[f.id] = [];
          else initial[f.id] = "";
        }
        setValues(initial);
      })
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [id]);

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
    if (!validate()) return;
    setSubmitting(true);
    try {
      const res = await fetch(`/api/forms/${id}/submissions`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(values),
      });
      if (res.ok) {
        setSubmitted(true);
      } else {
        const err = await res.json();
        alert(err.error || "Submission failed");
      }
    } catch {
      alert("Submission failed");
    } finally {
      setSubmitting(false);
    }
  }

  function setValue(fieldId: string, value: unknown) {
    setValues((prev) => ({ ...prev, [fieldId]: value }));
    if (errors[fieldId]) {
      setErrors((prev) => {
        const next = { ...prev };
        delete next[fieldId];
        return next;
      });
    }
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

  if (submitted) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center">
        <div className="text-center">
          <CheckCircle className="w-16 h-16 text-green-500 mx-auto mb-4" />
          <h2 className="text-xl font-semibold text-slate-800 mb-1">
            Form Submitted!
          </h2>
          <p className="text-sm text-slate-500 mb-6">
            Thank you for submitting {form.title}.
          </p>
          <button
            onClick={() => router.push("/forms")}
            className="inline-flex items-center gap-1.5 px-4 py-2 bg-slate-800 text-white text-sm font-medium rounded-lg hover:bg-slate-700 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to Forms
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="bg-white border-b border-slate-200 px-6 py-4">
        <div className="max-w-2xl mx-auto">
          <button
            onClick={() => router.push("/forms")}
            className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-800 mb-2 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to Forms
          </button>
          <h1 className="text-xl font-semibold text-slate-800">
            {form.title}
          </h1>
          {form.description && (
            <p className="text-sm text-slate-500 mt-0.5">
              {form.description}
            </p>
          )}
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-6 py-8">
        <form onSubmit={handleSubmit} className="space-y-5">
          {form.fields
            .sort((a, b) => a.order - b.order)
            .map((field) => (
              <FormFieldInput
                key={field.id}
                field={field}
                value={values[field.id]}
                error={errors[field.id]}
                onChange={(val) => setValue(field.id, val)}
              />
            ))}

          <button
            type="submit"
            disabled={submitting}
            className="w-full py-2.5 bg-indigo-600 text-white text-sm font-medium rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {submitting ? "Submitting..." : "Submit"}
          </button>
        </form>
      </main>
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
    error
      ? "border-red-300 bg-red-50"
      : "border-slate-300"
  }`;

  const strVal = (value as string) ?? "";
  const boolVal = value as boolean;

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
            {(field.options ?? []).length > 0
              ? field.options!.map((opt) => (
                  <label
                    key={opt.value}
                    className="flex items-center gap-2 cursor-pointer"
                  >
                    <input
                      type="checkbox"
                      value={opt.value}
                      checked={(value as string[] ?? []).includes(opt.value)}
                      onChange={(e) => {
                        const current = (value as string[]) ?? [];
                        if (e.target.checked) {
                          onChange([...current, opt.value]);
                        } else {
                          onChange(current.filter((v) => v !== opt.value));
                        }
                      }}
                      className="rounded border-slate-300 text-indigo-600 focus:ring-indigo-500"
                    />
                    <span className="text-sm text-slate-700">
                      {opt.label}
                    </span>
                  </label>
                ))
              : (
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={boolVal}
                    onChange={(e) => onChange(e.target.checked)}
                    className="rounded border-slate-300 text-indigo-600 focus:ring-indigo-500"
                  />
                  <span className="text-sm text-slate-700">
                    {field.label}
                  </span>
                </label>
              )}
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

      case "file":
        return (
          <input
            type="file"
            onChange={(e) => {
              const file = e.target.files?.[0];
              onChange(file ? { name: file.name, type: file.type, size: file.size } : null);
            }}
            className="w-full text-sm text-slate-600 file:mr-4 file:py-2 file:px-4 file:rounded-lg file:border-0 file:text-sm file:font-medium file:bg-indigo-50 file:text-indigo-700 hover:file:bg-indigo-100"
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
