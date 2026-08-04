import { request } from "./http";

export interface Submission {
  id: string;
  formId: string;
  data: Record<string, unknown>;
  createdAt: string;
}

export const submissionsService = {
  list: (formId: string) =>
    request<Submission[]>(`/forms/${formId}/submissions`),
  create: (formId: string, data: Record<string, unknown>) =>
    request<Submission>(`/forms/${formId}/submissions`, {
      method: "POST",
      body: JSON.stringify(data),
    }),
  get: (id: string) => request<Submission>(`/submissions/${id}`),
};