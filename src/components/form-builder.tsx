"use client";

import { useState, useCallback } from "react";
import {
  GripVertical,
  Plus,
  Trash2,
  ChevronUp,
  ChevronDown,
  Save,
} from "lucide-react";
import type { FormField, FieldType, FieldOption, FormConfig } from "@/lib/types";

const FIELD_TYPES: { type: FieldType; label: string }[] = [
  { type: "text", label: "Text" },
  { type: "number", label: "Number" },
  { type: "email", label: "Email" },
  { type: "tel", label: "Phone" },
  { type: "textarea", label: "Textarea" },
  { type: "select", label: "Dropdown" },
  { type: "checkbox", label: "Checkbox" },
  { type: "radio", label: "Radio" },
  { type: "date", label: "Date" },
  { type: "file", label: "File Upload" },
];

interface FormBuilderProps {
  initial?: FormConfig;
  onSave: (config: FormConfig) => Promise<void>;
}

let fieldCounter = 0;

function createField(type: FieldType): FormField {
  fieldCounter++;
  const id = `field_${Date.now()}_${fieldCounter}`;
  const base: FormField = {
    id,
    type,
    label: "",
    placeholder: "",
    helpText: "",
    required: false,
    order: 0,
  };
  if (type === "select" || type === "radio" || type === "checkbox") {
    base.options = [
      { label: "Option 1", value: "option_1" },
      { label: "Option 2", value: "option_2" },
    ];
  }
  return base;
}

export function FormBuilder({ initial, onSave }: FormBuilderProps) {
  const [title, setTitle] = useState(initial?.title ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [fields, setFields] = useState<FormField[]>(
    initial?.fields ?? [createField("text")]
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const addField = useCallback((type: FieldType) => {
    setFields((prev) => [...prev, { ...createField(type), order: prev.length }]);
  }, []);

  const removeField = useCallback((id: string) => {
    setFields((prev) =>
      prev
        .filter((f) => f.id !== id)
        .map((f, i) => ({ ...f, order: i }))
    );
  }, []);

  const moveField = useCallback((id: string, direction: "up" | "down") => {
    setFields((prev) => {
      const idx = prev.findIndex((f) => f.id === id);
      if (idx === -1) return prev;
      const newIdx = direction === "up" ? idx - 1 : idx + 1;
      if (newIdx < 0 || newIdx >= prev.length) return prev;
      const arr = [...prev];
      [arr[idx], arr[newIdx]] = [arr[newIdx], arr[idx]];
      return arr.map((f, i) => ({ ...f, order: i }));
    });
  }, []);

  const updateField = useCallback(
    (id: string, updates: Partial<FormField>) => {
      setFields((prev) =>
        prev.map((f) => (f.id === id ? { ...f, ...updates } : f))
      );
    },
    []
  );

  function addOption(fieldId: string) {
    setFields((prev) =>
      prev.map((f) => {
        if (f.id !== fieldId) return f;
        const options = f.options ?? [];
        const n = options.length + 1;
        return {
          ...f,
          options: [...options, { label: `Option ${n}`, value: `option_${n}` }],
        };
      })
    );
  }

  function updateOption(
    fieldId: string,
    optIdx: number,
    updates: Partial<FieldOption>
  ) {
    setFields((prev) =>
      prev.map((f) => {
        if (f.id !== fieldId) return f;
        const options = [...(f.options ?? [])];
        options[optIdx] = { ...options[optIdx], ...updates };
        return { ...f, options };
      })
    );
  }

  function removeOption(fieldId: string, optIdx: number) {
    setFields((prev) =>
      prev.map((f) => {
        if (f.id !== fieldId) return f;
        return {
          ...f,
          options: (f.options ?? []).filter((_, i) => i !== optIdx),
        };
      })
    );
  }

  async function handleSave() {
    if (!title.trim()) {
      setError("Form title is required");
      return;
    }
    const validFields = fields.filter((f) => f.label.trim());
    if (validFields.length === 0) {
      setError("At least one field with a label is required");
      return;
    }
    setError(null);
    setSaving(true);
    try {
      await onSave({
        title: title.trim(),
        description: description.trim() || undefined,
        fields: validFields.map((f, i) => ({ ...f, order: i })),
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Save failed");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      {error && (
        <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="bg-white rounded-lg border border-slate-200 p-5 space-y-4">
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Form Title *
          </label>
          <input
            type="text"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="e.g. Contact Information"
            className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Description
          </label>
          <input
            type="text"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Brief description of this form"
            className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
          />
        </div>
      </div>

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold text-slate-700">Fields</h2>
          <div className="flex items-center gap-1">
            {FIELD_TYPES.map((ft) => (
              <button
                key={ft.type}
                onClick={() => addField(ft.type)}
                className="px-2 py-1 text-xs text-slate-600 bg-white border border-slate-200 rounded-md hover:bg-slate-50 hover:border-slate-300 transition-colors"
                title={`Add ${ft.label}`}
              >
                +{ft.label}
              </button>
            ))}
          </div>
        </div>

        {fields.map((field, index) => (
          <FieldEditor
            key={field.id}
            field={field}
            index={index}
            total={fields.length}
            onUpdate={(updates) => updateField(field.id, updates)}
            onRemove={() => removeField(field.id)}
            onMoveUp={() => moveField(field.id, "up")}
            onMoveDown={() => moveField(field.id, "down")}
            onAddOption={() => addOption(field.id)}
            onUpdateOption={(idx, updates) =>
              updateOption(field.id, idx, updates)
            }
            onRemoveOption={(idx) => removeOption(field.id, idx)}
          />
        ))}
      </div>

      <button
        onClick={handleSave}
        disabled={saving}
        className="w-full flex items-center justify-center gap-2 px-4 py-2.5 bg-indigo-600 text-white text-sm font-medium rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
      >
        <Save className="w-4 h-4" />
        {saving ? "Saving..." : "Save Form"}
      </button>
    </div>
  );
}

interface FieldEditorProps {
  field: FormField;
  index: number;
  total: number;
  onUpdate: (updates: Partial<FormField>) => void;
  onRemove: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
  onAddOption: () => void;
  onUpdateOption: (idx: number, updates: Partial<FieldOption>) => void;
  onRemoveOption: (idx: number) => void;
}

function FieldEditor({
  field,
  index,
  total,
  onUpdate,
  onRemove,
  onMoveUp,
  onMoveDown,
  onAddOption,
  onUpdateOption,
  onRemoveOption,
}: FieldEditorProps) {
  return (
    <div className="bg-white rounded-lg border border-slate-200 p-4">
      <div className="flex items-start gap-3">
        <div className="flex flex-col items-center gap-0.5 pt-1">
          <GripVertical className="w-4 h-4 text-slate-300 cursor-grab" />
          <button
            onClick={onMoveUp}
            disabled={index === 0}
            className="p-0.5 text-slate-300 hover:text-slate-600 disabled:opacity-30"
          >
            <ChevronUp className="w-3.5 h-3.5" />
          </button>
          <button
            onClick={onMoveDown}
            disabled={index === total - 1}
            className="p-0.5 text-slate-300 hover:text-slate-600 disabled:opacity-30"
          >
            <ChevronDown className="w-3.5 h-3.5" />
          </button>
        </div>

        <div className="flex-1 space-y-3 min-w-0">
          <div className="flex items-center gap-2">
            <span className="px-2 py-0.5 bg-indigo-100 text-indigo-700 text-xs font-medium rounded">
              {field.type}
            </span>
            <input
              type="text"
              value={field.label}
              onChange={(e) => onUpdate({ label: e.target.value })}
              placeholder="Field Label *"
              className="flex-1 px-2 py-1 border-b border-transparent hover:border-slate-300 focus:border-indigo-500 text-sm font-medium text-slate-800 bg-transparent focus:outline-none"
            />
            <label className="flex items-center gap-1.5 text-xs text-slate-500 cursor-pointer">
              <input
                type="checkbox"
                checked={field.required}
                onChange={(e) => onUpdate({ required: e.target.checked })}
                className="rounded border-slate-300 text-indigo-600 focus:ring-indigo-500"
              />
              Required
            </label>
            <button
              onClick={onRemove}
              className="p-1.5 text-slate-300 hover:text-red-500 hover:bg-red-50 rounded-lg transition-colors"
            >
              <Trash2 className="w-4 h-4" />
            </button>
          </div>

          {(field.type === "text" ||
            field.type === "email" ||
            field.type === "tel" ||
            field.type === "number") && (
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs text-slate-500 mb-0.5">
                  Placeholder
                </label>
                <input
                  type="text"
                  value={field.placeholder ?? ""}
                  onChange={(e) => onUpdate({ placeholder: e.target.value })}
                  placeholder="Placeholder text"
                  className="w-full px-2 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
              </div>
              <div>
                <label className="block text-xs text-slate-500 mb-0.5">
                  Help Text
                </label>
                <input
                  type="text"
                  value={field.helpText ?? ""}
                  onChange={(e) => onUpdate({ helpText: e.target.value })}
                  placeholder="Help text"
                  className="w-full px-2 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
              </div>
            </div>
          )}

          {field.type === "textarea" && (
            <div>
              <label className="block text-xs text-slate-500 mb-0.5">
                Placeholder
              </label>
              <input
                type="text"
                value={field.placeholder ?? ""}
                onChange={(e) => onUpdate({ placeholder: e.target.value })}
                placeholder="Placeholder text"
                className="w-full px-2 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          )}

          {(field.type === "select" ||
            field.type === "radio" ||
            field.type === "checkbox") && (
            <div className="space-y-2">
              <label className="block text-xs text-slate-500 mb-0.5">
                Options
              </label>
              {(field.options ?? []).map((opt, optIdx) => (
                <div key={optIdx} className="flex items-center gap-2">
                  <input
                    type="text"
                    value={opt.label}
                    onChange={(e) =>
                      onUpdateOption(optIdx, { label: e.target.value })
                    }
                    placeholder="Label"
                    className="flex-1 px-2 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                  <input
                    type="text"
                    value={opt.value}
                    onChange={(e) =>
                      onUpdateOption(optIdx, { value: e.target.value })
                    }
                    placeholder="Value"
                    className="flex-1 px-2 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                  <button
                    onClick={() => onRemoveOption(optIdx)}
                    className="p-1.5 text-slate-300 hover:text-red-500 rounded"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                  </button>
                </div>
              ))}
              <button
                onClick={onAddOption}
                className="text-xs text-indigo-600 hover:text-indigo-800 font-medium"
              >
                + Add Option
              </button>
            </div>
          )}

          {field.type === "date" && (
            <div>
              <label className="block text-xs text-slate-500 mb-0.5">
                Help Text
              </label>
              <input
                type="text"
                value={field.helpText ?? ""}
                onChange={(e) => onUpdate({ helpText: e.target.value })}
                placeholder="Help text"
                className="w-full px-2 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
