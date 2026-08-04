import { request } from "./http";

export interface Thread {
  id: string;
  agentId: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

export const threadsService = {
  list: (agentId?: string) =>
    request<Thread[]>(
      `/threads${agentId ? `?agentId=${encodeURIComponent(agentId)}` : ""}`,
    ),
  create: (data: { agentId?: string; title?: string }) =>
    request<Thread>("/threads", {
      method: "POST",
      body: JSON.stringify(data),
    }),
  update: (id: string, data: { title?: string; metadata?: string }) =>
    request<Thread>(`/threads/${id}`, {
      method: "PATCH",
      body: JSON.stringify(data),
    }),
  delete: (id: string) =>
    request<{ success: boolean }>(`/threads/${id}`, { method: "DELETE" }),
};