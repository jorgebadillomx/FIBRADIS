export interface AdminUser {
  userId: string;
  username: string;
  email: string;
  role: string;
  isActive: boolean;
  createdAtUtc: string;
  lastLoginUtc?: string | null;
}

export interface PaginatedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AuditLogEntry {
  id: string;
  userId: string;
  action: string;
  result: string;
  timestampUtc: string;
  ipAddress: string;
  metadata: Record<string, unknown>;
}

export interface SettingsResponse {
  marketHoursMx: string;
  llmMaxTokensPerUser: number;
  securityMaxUploadSize: number;
  systemMaintenanceMode: boolean;
}

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: {
      "Content-Type": "application/json",
      ...init?.headers
    },
    credentials: "include",
    ...init
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Error ${response.status}`);
  }

  return (await response.json()) as T;
}

export async function getUsers(params: { search?: string; page?: number; pageSize?: number }) {
  const searchParams = new URLSearchParams();
  if (params.search) searchParams.set("search", params.search);
  if (params.page) searchParams.set("page", String(params.page));
  if (params.pageSize) searchParams.set("pageSize", String(params.pageSize));
  const query = searchParams.toString();
  return request<PaginatedResponse<AdminUser>>(`/v1/admin/users${query ? `?${query}` : ""}`);
}

export async function createUser(payload: { email: string; role: string; password: string; isActive: boolean }) {
  return request<AdminUser>("/v1/admin/users", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function updateUser(id: string, payload: { email: string; role: string; isActive: boolean; password?: string }) {
  return request<AdminUser>(`/v1/admin/users/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload)
  });
}

export async function deactivateUser(id: string) {
  await request<void>(`/v1/admin/users/${id}`, {
    method: "DELETE"
  });
}

export async function getAuditLogs(params: {
  userId?: string;
  action?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}) {
  const searchParams = new URLSearchParams();
  if (params.userId) searchParams.set("userId", params.userId);
  if (params.action) searchParams.set("action", params.action);
  if (params.from) searchParams.set("from", params.from);
  if (params.to) searchParams.set("to", params.to);
  if (params.page) searchParams.set("page", String(params.page));
  if (params.pageSize) searchParams.set("pageSize", String(params.pageSize));
  const query = searchParams.toString();
  return request<PaginatedResponse<AuditLogEntry>>(`/v1/admin/audit${query ? `?${query}` : ""}`);
}

export async function getSettings() {
  return request<SettingsResponse>("/v1/admin/settings");
}

export async function updateSettings(payload: Partial<SettingsResponse>) {
  return request<SettingsResponse>("/v1/admin/settings", {
    method: "PUT",
    body: JSON.stringify(payload)
  });
}
