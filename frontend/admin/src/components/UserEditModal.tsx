import { useEffect, useState } from "react";
import { AdminUser } from "../lib/api";
import { Dialog, DialogContent, DialogFooter, DialogHeader } from "./ui/dialog";
import { Button } from "./ui/button";
import { Input } from "./ui/input";
import { Label } from "./ui/label";
import { Select } from "./ui/select";

export interface UserFormValues {
  email: string;
  role: string;
  isActive: boolean;
  password?: string;
}

interface UserEditModalProps {
  open: boolean;
  user?: AdminUser | null;
  onClose: () => void;
  onSubmit: (values: UserFormValues) => Promise<void>;
  loading?: boolean;
}

const roleOptions = [
  { value: "viewer", label: "Viewer" },
  { value: "user", label: "User" },
  { value: "admin", label: "Admin" }
];

export function UserEditModal({ open, user, onClose, onSubmit, loading }: UserEditModalProps) {
  const [form, setForm] = useState<UserFormValues>({
    email: "",
    role: "viewer",
    isActive: true,
    password: ""
  });
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (user) {
      setForm({
        email: user.email,
        role: user.role,
        isActive: user.isActive,
        password: ""
      });
    } else {
      setForm({ email: "", role: "viewer", isActive: true, password: "" });
    }
  }, [user, open]);

  const handleSubmit = async () => {
    try {
      setError(null);
      if (!form.email.includes("@")) {
        setError("El correo es obligatorio");
        return;
      }
      if (!form.role) {
        setError("Selecciona un rol válido");
        return;
      }
      await onSubmit(form);
      onClose();
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <Dialog open={open} onOpenChange={(value) => (value ? null : onClose())}>
      <DialogContent>
        <DialogHeader
          title={user ? "Editar usuario" : "Nuevo usuario"}
          description="Gestiona roles y credenciales de acceso."
        />
        <div className="space-y-4">
          <div>
            <Label htmlFor="email">Correo electrónico</Label>
            <Input
              id="email"
              type="email"
              value={form.email}
              onChange={(event) => setForm((prev) => ({ ...prev, email: event.target.value }))}
            />
          </div>
          <div>
            <Label htmlFor="role">Rol</Label>
            <Select
              id="role"
              value={form.role}
              onChange={(event) => setForm((prev) => ({ ...prev, role: event.target.value }))}
            >
              {roleOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="flex items-center gap-2">
            <input
              id="isActive"
              type="checkbox"
              checked={form.isActive}
              onChange={(event) => setForm((prev) => ({ ...prev, isActive: event.target.checked }))}
              className="h-4 w-4 rounded border-slate-800 bg-slate-900"
            />
            <Label htmlFor="isActive" className="!text-sm normal-case">
              Usuario activo
            </Label>
          </div>
          <div>
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              placeholder={user ? "Opcional" : "Temporal"}
              value={form.password ?? ""}
              onChange={(event) => setForm((prev) => ({ ...prev, password: event.target.value }))}
            />
          </div>
          {error && <p className="text-sm text-rose-400">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose} disabled={loading}>
            Cancelar
          </Button>
          <Button onClick={handleSubmit} disabled={loading}>
            {user ? "Guardar cambios" : "Crear usuario"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
