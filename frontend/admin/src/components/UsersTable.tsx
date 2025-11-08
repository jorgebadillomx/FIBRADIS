import { AdminUser } from "../lib/api";
import { Button } from "./ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Table, TBody, TD, TH, THead, TR } from "./ui/table";
import { formatDate } from "../lib/utils";

interface UsersTableProps {
  users: AdminUser[];
  onEdit: (user: AdminUser) => void;
  onDeactivate: (user: AdminUser) => void;
  loading?: boolean;
}

export function UsersTable({ users, onEdit, onDeactivate, loading }: UsersTableProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Usuarios ({users.length})</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="overflow-x-auto">
          <Table>
            <THead>
              <TR>
                <TH>Correo</TH>
                <TH>Usuario</TH>
                <TH>Rol</TH>
                <TH>Último acceso</TH>
                <TH>Estado</TH>
                <TH className="text-right">Acciones</TH>
              </TR>
            </THead>
            <TBody>
              {users.map((user) => (
                <TR key={user.userId}>
                  <TD>{user.email}</TD>
                  <TD className="text-slate-400">{user.username}</TD>
                  <TD className="uppercase">{user.role}</TD>
                  <TD>{formatDate(user.lastLoginUtc)}</TD>
                  <TD>
                    <span className={user.isActive ? "text-emerald-400" : "text-rose-400"}>
                      {user.isActive ? "Activo" : "Inactivo"}
                    </span>
                  </TD>
                  <TD className="flex justify-end gap-2">
                    <Button variant="outline" onClick={() => onEdit(user)} disabled={loading}>
                      Editar
                    </Button>
                    <Button
                      variant="destructive"
                      onClick={() => onDeactivate(user)}
                      disabled={loading || !user.isActive}
                    >
                      Desactivar
                    </Button>
                  </TD>
                </TR>
              ))}
              {users.length === 0 && (
                <TR>
                  <TD colSpan={6} className="py-6 text-center text-slate-500">
                    No se encontraron usuarios.
                  </TD>
                </TR>
              )}
            </TBody>
          </Table>
        </div>
      </CardContent>
    </Card>
  );
}
