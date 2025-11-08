import { useEffect, useState } from "react";
import { SettingsResponse } from "../lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Button } from "./ui/button";
import { Input } from "./ui/input";
import { Label } from "./ui/label";

interface SettingsFormProps {
  settings?: SettingsResponse;
  loading?: boolean;
  onSubmit: (values: Partial<SettingsResponse>) => Promise<void>;
  statusMessage?: string | null;
}

export function SettingsForm({ settings, loading, onSubmit, statusMessage }: SettingsFormProps) {
  const [form, setForm] = useState<Partial<SettingsResponse>>({});
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (settings) {
      setForm(settings);
    }
  }, [settings]);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      setError(null);
      await onSubmit(form);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Configuración del sistema</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <Label htmlFor="marketHours">Horario Mercado MX</Label>
            <Input
              id="marketHours"
              value={form.marketHoursMx ?? ""}
              onChange={(event) => setForm((prev) => ({ ...prev, marketHoursMx: event.target.value }))}
            />
          </div>
          <div>
            <Label htmlFor="tokens">Max Tokens por usuario</Label>
            <Input
              id="tokens"
              type="number"
              value={form.llmMaxTokensPerUser ?? 0}
              onChange={(event) => setForm((prev) => ({ ...prev, llmMaxTokensPerUser: Number(event.target.value) }))}
            />
          </div>
          <div>
            <Label htmlFor="upload">Tamaño máximo upload (bytes)</Label>
            <Input
              id="upload"
              type="number"
              value={form.securityMaxUploadSize ?? 0}
              onChange={(event) => setForm((prev) => ({ ...prev, securityMaxUploadSize: Number(event.target.value) }))}
            />
          </div>
          <div className="flex items-center gap-2">
            <input
              id="maintenance"
              type="checkbox"
              checked={form.systemMaintenanceMode ?? false}
              onChange={(event) => setForm((prev) => ({ ...prev, systemMaintenanceMode: event.target.checked }))}
              className="h-4 w-4 rounded border-slate-800 bg-slate-900"
            />
            <Label htmlFor="maintenance" className="!text-sm normal-case">
              Activar modo mantenimiento
            </Label>
          </div>
          {error && <p className="text-sm text-rose-400">{error}</p>}
          {statusMessage && <p className="text-sm text-emerald-400">{statusMessage}</p>}
          <div className="flex justify-end">
            <Button type="submit" disabled={loading}>
              Guardar cambios
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
