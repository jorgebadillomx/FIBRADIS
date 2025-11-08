import { AuditLogEntry } from "../lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Table, TBody, TD, TH, THead, TR } from "./ui/table";
import { formatDate } from "../lib/utils";

interface AuditLogTableProps {
  logs: AuditLogEntry[];
}

export function AuditLogTable({ logs }: AuditLogTableProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Eventos registrados ({logs.length})</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="overflow-x-auto">
          <Table>
            <THead>
              <TR>
                <TH>Fecha</TH>
                <TH>Usuario</TH>
                <TH>Acción</TH>
                <TH>Resultado</TH>
                <TH>IP</TH>
                <TH>Metadata</TH>
              </TR>
            </THead>
            <TBody>
              {logs.map((log) => (
                <TR key={log.id}>
                  <TD>{formatDate(log.timestampUtc)}</TD>
                  <TD className="text-slate-400">{log.userId}</TD>
                  <TD>{log.action}</TD>
                  <TD className={log.result === "success" ? "text-emerald-400" : "text-rose-400"}>{log.result}</TD>
                  <TD>{log.ipAddress}</TD>
                  <TD>
                    <pre className="whitespace-pre-wrap text-xs text-slate-400">
                      {JSON.stringify(log.metadata, null, 2)}
                    </pre>
                  </TD>
                </TR>
              ))}
              {logs.length === 0 && (
                <TR>
                  <TD colSpan={6} className="py-6 text-center text-slate-500">
                    Sin registros para los filtros actuales.
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
