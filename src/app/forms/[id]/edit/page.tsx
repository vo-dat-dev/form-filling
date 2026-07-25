"use client";

import { use, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { FormBuilder } from "@/components/form-builder";
import type { FormConfig } from "@/lib/types";

export default function EditFormPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);
  const router = useRouter();
  const [initial, setInitial] = useState<FormConfig | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch(`/api/forms/${id}`)
      .then((r) => r.json())
      .then((data) => {
        setInitial({
          title: data.title,
          description: data.description ?? undefined,
          fields: data.fields,
        });
      })
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [id]);

  async function handleSave(config: FormConfig) {
    const res = await fetch(`/api/forms/${id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(config),
    });
    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || "Failed to update form");
    }
    router.push("/forms");
  }

  if (loading) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center text-slate-400">
        Loading...
      </div>
    );
  }

  if (!initial) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center text-red-500">
        Form not found
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="bg-white border-b border-slate-200 px-6 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Edit Form</h1>
          <p className="text-sm text-slate-500 mt-0.5">{initial.title}</p>
        </div>
      </header>

      <main className="max-w-3xl mx-auto px-6 py-8">
        <FormBuilder initial={initial} onSave={handleSave} />
      </main>
    </div>
  );
}
