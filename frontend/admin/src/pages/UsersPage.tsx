import { useState } from "react";
import { UsersTable } from "../components/UsersTable";
import { UserEditModal, UserFormValues } from "../components/UserEditModal";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { useUserMutations, useUsers } from "../hooks/useAdminApi";
import { AdminUser } from "../lib/api";

const PAGE_SIZE = 10;

export function UsersPage() {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);

  const { data, isLoading, isFetching } = useUsers(search, page, PAGE_SIZE);
  const { create, update, deactivate } = useUserMutations();

  const openCreateModal = () => {
    setEditingUser(null);
    setModalOpen(true);
  };

  const openEditModal = (user: AdminUser) => {
    setEditingUser(user);
    setModalOpen(true);
  };

  const handleSubmit = async (values: UserFormValues) => {
    if (editingUser) {
      await update.mutateAsync({
        id: editingUser.userId,
        payload: {
          email: values.email,
          role: values.role,
          isActive: values.isActive,
          password: values.password || undefined
        }
      });
      setFeedback("Guardado ✔");
    } else {
      await create.mutateAsync({
        email: values.email,
        role: values.role,
        password: values.password || "Temp123!",
        isActive: values.isActive
      });
      setFeedback("Usuario creado ✔");
    }
  };

  const handleDeactivate = async (user: AdminUser) => {
    if (!window.confirm(`¿Desactivar a ${user.email}?`)) {
      return;
    }
    await deactivate.mutateAsync(user.userId);
    setFeedback("Usuario desactivado ✔");
  };

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
        <div className="flex gap-3">
          <Input
            placeholder="Buscar por correo"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
          />
          <Button variant="outline" onClick={() => setSearch("")}>Limpiar</Button>
        </div>
        <Button onClick={openCreateModal}>Nuevo usuario</Button>
      </div>

      {feedback && <p className="text-sm text-emerald-400">{feedback}</p>}

      <UsersTable
        users={data?.items ?? []}
        onEdit={openEditModal}
        onDeactivate={handleDeactivate}
        loading={isLoading || isFetching}
      />

      <div className="flex items-center justify-between text-sm text-slate-400">
        <span>
          Página {page} de {totalPages}
        </span>
        <div className="flex gap-2">
          <Button
            variant="outline"
            onClick={() => setPage((prev) => Math.max(1, prev - 1))}
            disabled={page === 1}
          >
            Anterior
          </Button>
          <Button
            variant="outline"
            onClick={() => setPage((prev) => Math.min(totalPages, prev + 1))}
            disabled={page >= totalPages}
          >
            Siguiente
          </Button>
        </div>
      </div>

      <UserEditModal
        open={modalOpen}
        user={editingUser}
        onClose={() => setModalOpen(false)}
        onSubmit={handleSubmit}
        loading={create.isLoading || update.isLoading}
      />
    </div>
  );
}
