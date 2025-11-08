import { NavLink } from "react-router-dom";
import { cn } from "../lib/utils";

const links = [
  { to: "/admin/users", label: "Usuarios" },
  { to: "/admin/audit", label: "Auditoría" },
  { to: "/admin/settings", label: "Settings" }
];

export function Navigation() {
  return (
    <nav className="flex gap-2">
      {links.map((link) => (
        <NavLink
          key={link.to}
          to={link.to}
          className={({ isActive }) =>
            cn(
              "rounded-md px-3 py-2 text-sm font-medium transition-colors",
              isActive ? "bg-emerald-500 text-white" : "text-slate-300 hover:bg-slate-800"
            )
          }
        >
          {link.label}
        </NavLink>
      ))}
    </nav>
  );
}
