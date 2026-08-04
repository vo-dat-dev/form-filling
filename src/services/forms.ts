import { request } from "./http";

export interface Form {
  id: string;
  title: string;
  description: string | null;
  fields: string;
  embedding?: string | null;
  createdAt: string;
  updatedAt: string;
  count?: number;
  similarity?: number | null;
}

export interface CreateFormParams {
  title: string;
  description?: string;
  fields: string;
  embedding?: string;
}

export interface UpdateFormParams {
  title: string;
  description?: string;
  fields: string;
  embedding?: string;
  descriptionChanged?: boolean;
}

export const formsService = {
  list: () => request<Form[]>("/forms"),
  search: (query: string) =>
    request<Form[]>("/forms/search", {
      method: "POST",
      body: JSON.stringify({ query }),
    }),
  get: (id: string) => request<Form>(`/forms/${id}`),
  create: (data: CreateFormParams) =>
    request<Form>("/forms", {
      method: "POST",
      body: JSON.stringify(data),
    }),
  update: (id: string, data: UpdateFormParams) =>
    request<Form>(`/forms/${id}`, {
      method: "PUT",
      body: JSON.stringify(data),
    }),
  delete: (id: string) =>
    request<{ success: boolean }>(`/forms/${id}`, { method: "DELETE" }),
};