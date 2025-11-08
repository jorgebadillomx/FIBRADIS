import { Navigate, Route, Routes } from "react-router-dom";
import { Navigation } from "./components/Navigation";
import { UsersPage } from "./pages/UsersPage";
import { AuditPage } from "./pages/AuditPage";
import { SettingsPage } from "./pages/SettingsPage";

export default function App() {
  return (
    <div className="min-h-screen bg-slate-950 text-slate-100">
      <header className="border-b border-slate-900 bg-slate-950/80">
        <div className="mx-auto flex max-w-6xl flex-col gap-4 px-6 py-6 md:flex-row md:items-center md:justify-between">
          <div>
            <h1 className="text-2xl font-semibold text-white">Panel Admin · FIBRADIS</h1>
            <p className="text-sm text-slate-400">Gestión centralizada de usuarios, auditoría y settings.</p>
          </div>
          <Navigation />
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-6 py-8">
        <Routes>
          <Route path="/admin" element={<Navigate to="/admin/users" replace />} />
          <Route path="/admin/users" element={<UsersPage />} />
          <Route path="/admin/audit" element={<AuditPage />} />
          <Route path="/admin/settings" element={<SettingsPage />} />
          <Route path="*" element={<Navigate to="/admin/users" replace />} />
        </Routes>
      </main>
    </div>
  );
}
