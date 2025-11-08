import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AdminUser,
  PaginatedResponse,
  AuditLogEntry,
  SettingsResponse,
  createUser,
  deactivateUser,
  getAuditLogs,
  getSettings,
  getUsers,
  updateSettings,
  updateUser
} from "../lib/api";

export function useUsers(search: string, page: number, pageSize: number) {
  return useQuery<PaginatedResponse<AdminUser>>({
    queryKey: ["admin-users", { search, page, pageSize }],
    queryFn: () => getUsers({ search, page, pageSize })
  });
}

export function useAudit(query: { userId?: string; action?: string; from?: string; to?: string; page: number; pageSize: number }) {
  return useQuery<PaginatedResponse<AuditLogEntry>>({
    queryKey: ["admin-audit", query],
    queryFn: () => getAuditLogs(query)
  });
}

export function useSettings() {
  return useQuery<SettingsResponse>({
    queryKey: ["admin-settings"],
    queryFn: () => getSettings()
  });
}

export function useUserMutations() {
  const queryClient = useQueryClient();
  const create = useMutation({
    mutationFn: createUser,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin-users"] });
    }
  });
  const update = useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: Parameters<typeof updateUser>[1] }) => updateUser(id, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin-users"] });
      queryClient.invalidateQueries({ queryKey: ["admin-audit"] });
    }
  });
  const deactivate = useMutation({
    mutationFn: (id: string) => deactivateUser(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin-users"] });
      queryClient.invalidateQueries({ queryKey: ["admin-audit"] });
    }
  });
  return { create, update, deactivate };
}

export function useSettingsMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: updateSettings,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin-settings"] });
      queryClient.invalidateQueries({ queryKey: ["admin-audit"] });
    }
  });
}
