import { LabelHTMLAttributes } from "react";
import { cn } from "../../lib/utils";

export const Label = ({ className, ...props }: LabelHTMLAttributes<HTMLLabelElement>) => (
  <label className={cn("block text-xs font-medium uppercase tracking-wide text-slate-400", className)} {...props} />
);
