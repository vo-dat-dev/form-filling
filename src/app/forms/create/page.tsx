"use client";

import { useRouter } from "next/navigation";
import { FormBuilder } from "@/components/form-builder";
import type { FormConfig } from "@/lib/types";

export default function CreateFormPage() {
  const router = useRouter();

  async function handleSave(config: FormConfig) {
    const res = await fetch("/api/forms", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(config),
    });
    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || "Failed to create form");
    }
    router.push("/forms");
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="bg-white border-b border-slate-200 px-6 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Create Form</h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Build your dynamic form by adding fields
          </p>
        </div>
      </header>

      <main className="max-w-3xl mx-auto px-6 py-8">
        <FormBuilder onSave={handleSave} />
      </main>
    </div>
  );
}
