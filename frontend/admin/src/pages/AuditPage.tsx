import { useState } from "react";
import { AuditLogTable } from "../components/AuditLogTable";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { useAudit } from "../hooks/useAdminApi";

const PAGE_SIZE = 20;

export function AuditPage() {
  const [filters, setFilters] = useState({
    userId: "",
    action: "",
    from: "",
    to: "",
    page: 1
  });

  const { data, isFetching } = useAudit({
    userId: filters.userId || undefined,
    action: filters.action || undefined,
    from: filters.from || undefined,
    to: filters.to || undefined,
    page: filters.page,
    pageSize: PAGE_SIZE
  });

  const applyFilters = () => {
    setFilters((prev) => ({ ...prev, page: 1 }));
  };

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;

  return (
    <div className="space-y-6">
      <div className="grid gap-3 md:grid-cols-5">
        <Input
          placeholder="Usuario"
          value={filters.userId}
          onChange={(event) => setFilters((prev) => ({ ...prev, userId: event.target.value }))}
        />
        <Input
          placeholder="Acción"
          value={filters.action}
          onChange={(event) => setFilters((prev) => ({ ...prev, action: event.target.value }))}
        />
        <Input
          type="datetime-local"
          value={filters.from}
          onChange={(event) => setFilters((prev) => ({ ...prev, from: event.target.value }))}
        />
        <Input
          type="datetime-local"
          value={filters.to}
          onChange={(event) => setFilters((prev) => ({ ...prev, to: event.target.value }))}
        />
        <Button onClick={applyFilters}>Filtrar</Button>
      </div>

      <AuditLogTable logs={data?.items ?? []} />

      <div className="flex items-center justify-between text-sm text-slate-400">
        <span>
          Página {filters.page} de {totalPages} {isFetching ? "(cargando...)" : ""}
        </span>
        <div className="flex gap-2">
          <Button
            variant="outline"
            onClick={() => setFilters((prev) => ({ ...prev, page: Math.max(1, prev.page - 1) }))}
            disabled={filters.page === 1}
          >
            Anterior
          </Button>
          <Button
            variant="outline"
            onClick={() => setFilters((prev) => ({ ...prev, page: Math.min(totalPages, prev.page + 1) }))}
            disabled={filters.page >= totalPages}
          >
            Siguiente
          </Button>
        </div>
      </div>
    </div>
  );
}
