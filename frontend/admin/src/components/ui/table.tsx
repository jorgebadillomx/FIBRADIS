import { HTMLAttributes, TableHTMLAttributes } from "react";
import { cn } from "../../lib/utils";

export const Table = ({ className, ...props }: TableHTMLAttributes<HTMLTableElement>) => (
  <table className={cn("min-w-full text-sm text-slate-200", className)} {...props} />
);

export const THead = ({ className, ...props }: HTMLAttributes<HTMLTableSectionElement>) => (
  <thead className={cn("bg-slate-900/80 text-left text-xs uppercase text-slate-400", className)} {...props} />
);

export const TBody = ({ className, ...props }: HTMLAttributes<HTMLTableSectionElement>) => (
  <tbody className={cn("divide-y divide-slate-800", className)} {...props} />
);

export const TR = ({ className, ...props }: HTMLAttributes<HTMLTableRowElement>) => (
  <tr className={cn("hover:bg-slate-800/70 transition-colors", className)} {...props} />
);

export const TH = ({ className, ...props }: HTMLAttributes<HTMLTableCellElement>) => (
  <th className={cn("px-4 py-3 font-medium", className)} {...props} />
);

export const TD = ({ className, ...props }: HTMLAttributes<HTMLTableCellElement>) => (
  <td className={cn("px-4 py-3", className)} {...props} />
);
