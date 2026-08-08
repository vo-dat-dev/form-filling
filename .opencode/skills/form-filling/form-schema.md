# Form Schema & API Flow

## Field schema

A form has `title`, optional `description`, and `fields` (array of `FormField`).

### FormConfig (frontend type, `src/lib/types.ts`)

```ts
export type FieldType =
  | "text" | "number" | "email" | "tel" | "textarea"
  | "select" | "checkbox" | "radio" | "date"
  | "file" | "list";

export interface FormField {
  id: string;
  type: FieldType;
  label: string;
  placeholder?: string;
  helpText?: string;
  required: boolean;
  options?: FieldOption[];   // select / radio / checkbox
  validation?: FieldValidation;
  subFields?: FormField[];   // list (repeating group)
  order: number;
}
```

### Field types and value shapes

| type | value stored |
| --- | --- |
| text / email / tel / number / date / textarea | string |
| select / radio | string (the option value) |
| checkbox | `string[]` (array of selected option values) |
| list | array of objects mapping subField id -> value |
| file | `{ name, type, size }` or null |

## API flow

1. **Frontend page** sends `FormConfig` to the Next.js route:
   `POST /api/forms` or `PUT /api/forms/{id}` with
   `{ title, description, fields: FormField[] }`.
2. **Next.js route** (`src/app/api/forms/route.ts`, `[id]/route.ts`)
   stringifies fields: `fields: JSON.stringify(body.fields)` and forwards to
   the agent.
3. **Agent** (`agent/Program.cs`) `POST /api/forms` expects the body:
   ```json
   { "title": "...", "description": "...", "fields": "[{\"id\":\"f_1\",...}]" }
   ```
   `fields` MUST be a JSON **string**, not an array — the C# DTO binds
   `CreateFormRequest(string Title, string? Description, string Fields)`.
4. The agent calls `EmbeddingService.EmbedAsync(description)` (Ollama
   `/api/embed`, model `bge-m3`, 1024 dims) and stores the `Vector` in the
   `embedding` column (`vector(1024)`).
5. On **GET**, the agent returns `fields` as a JSON string; the Next.js route
   parses it back into an array (`parseFields`) before responding.

## Creating a form body

Build the body with stable `id`s (e.g. `f_1`, `f_2`, ...), ordered `order`
starting at 1, and sensible `required` flags. Keep `description` descriptive
in Vietnamese + English keywords, because the semantic search embedding is
generated from it.

### Example (land use certificate)

```json
{
  "title": "Giấy chứng nhận quyền sử dụng đất",
  "description": "Giấy chứng nhận quyền sử dụng đất (sổ đỏ) — land use certificate. Chứng nhận quyền sử dụng đất: tên chủ sở hữu, năm sinh, số CCCD, vợ/chồng đồng sở hữu, địa chỉ thường trú, số thửa đất, số tờ bản đồ, diện tích đất, mục đích sử dụng, số giấy chứng nhận, cơ quan cấp, ngày cấp.",
  "fields": "[{\"id\":\"f_1\",\"type\":\"text\",\"label\":\"Tên người sử dụng đất\",\"order\":1,\"required\":true},{\"id\":\"f_2\",\"type\":\"number\",\"label\":\"Năm sinh\",\"order\":2,\"required\":true},{\"id\":\"f_3\",\"type\":\"text\",\"label\":\"Số CCCD\",\"order\":3,\"required\":true},{\"id\":\"f_4\",\"type\":\"text\",\"label\":\"Địa chỉ thường trú\",\"order\":4,\"required\":true},{\"id\":\"f_5\",\"type\":\"number\",\"label\":\"Số thửa đất\",\"order\":5,\"required\":true},{\"id\":\"f_6\",\"type\":\"number\",\"label\":\"Số tờ bản đồ\",\"order\":6,\"required\":true},{\"id\":\"f_7\",\"type\":\"text\",\"label\":\"Diện tích đất\",\"order\":7,\"required\":true},{\"id\":\"f_8\",\"type\":\"text\",\"label\":\"Mục đích sử dụng\",\"order\":8,\"required\":true},{\"id\":\"f_9\",\"type\":\"date\",\"label\":\"Ngày cấp\",\"order\":9,\"required\":true}]"
}
```

### POST via curl (agent is on :8000)

```sh
curl -X POST http://localhost:8000/api/forms \
  -H "Content-Type: application/json" \
  -d @form_body.json
```

## Gotchas

- `fields` in the **agent request body** is a JSON string; in the **frontend
  API response** it is an array.
- Embedding dimension must be 1024 (the `bge-m3` model) — do not use a model
  with a different output size, or Postgres fails with "different vector
  dimensions".
- A `list` field is a repeating group; give it `subFields`. Nested lists are
  not supported.
- `checkbox` without `options` renders as a single boolean; with `options` it
  stores an array of values.