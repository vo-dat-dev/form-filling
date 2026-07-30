const API_BASE = process.env.API_BASE_URL ?? "http://127.0.0.1:8000/api";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function request<T>(
  path: string,
  options?: RequestInit,
): Promise<T> {
  const url = `${API_BASE}${path}`;
  const res = await fetch(url, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options?.headers,
    },
  });

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new ApiError(res.status, text || res.statusText);
  }

  if (res.status === 204) return undefined as T;
  return res.json();
}

// Threads
export const threadsApi = {
  list: (agentId?: string) =>
    request<Array<{ id: string; agentId: string; title: string; createdAt: string; updatedAt: string }>>(
      `/threads${agentId ? `?agentId=${encodeURIComponent(agentId)}` : ""}`,
    ),
  create: (data: { agentId?: string; title?: string }) =>
    request<{ id: string; agentId: string; title: string; createdAt: string; updatedAt: string }>(
      "/threads",
      { method: "POST", body: JSON.stringify(data) },
    ),
  update: (id: string, data: { title?: string; metadata?: string }) =>
    request<{ id: string; agentId: string; title: string; createdAt: string; updatedAt: string }>(
      `/threads/${id}`,
      { method: "PATCH", body: JSON.stringify(data) },
    ),
  delete: (id: string) =>
    request<{ success: boolean }>(`/threads/${id}`, { method: "DELETE" }),
};

// Forms
export const formsApi = {
  list: (q?: string) => {
    const params = q ? `?q=${encodeURIComponent(q)}` : "";
    return request<Array<Record<string, unknown>>>(`/forms${params}`);
  },
  get: (id: string) =>
    request<Record<string, unknown>>(`/forms/${id}`),
  create: (data: { title: string; description?: string; fields: string; embedding?: string }) =>
    request<Record<string, unknown>>("/forms", {
      method: "POST",
      body: JSON.stringify(data),
    }),
  update: (id: string, data: { title: string; description?: string; fields: string; embedding?: string; descriptionChanged?: boolean }) =>
    request<Record<string, unknown>>(`/forms/${id}`, {
      method: "PUT",
      body: JSON.stringify(data),
    }),
  delete: (id: string) =>
    request<{ success: boolean }>(`/forms/${id}`, { method: "DELETE" }),
};

// Submissions
export const submissionsApi = {
  list: (formId: string) =>
    request<Array<Record<string, unknown>>>(`/forms/${formId}/submissions`),
  create: (formId: string, data: Record<string, unknown>) =>
    request<Record<string, unknown>>(`/forms/${formId}/submissions`, {
      method: "POST",
      body: JSON.stringify(data),
    }),
  get: (id: string) =>
    request<Record<string, unknown>>(`/submissions/${id}`),
};
