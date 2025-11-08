import { SelectHTMLAttributes } from "react";
import { cn } from "../../lib/utils";

export const Select = ({ className, children, ...props }: SelectHTMLAttributes<HTMLSelectElement>) => (
  <select
    className={cn(
      "w-full rounded-md border border-slate-800 bg-slate-900 px-3 py-2 text-sm text-white focus:border-emerald-400 focus:outline-none focus:ring-2 focus:ring-emerald-400",
      className
    )}
    {...props}
  >
    {children}
  </select>
);
