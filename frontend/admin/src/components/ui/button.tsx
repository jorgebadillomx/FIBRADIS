import { ButtonHTMLAttributes, forwardRef } from "react";
import { cn } from "../../lib/utils";

export type ButtonVariant = "default" | "outline" | "ghost" | "destructive";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

const variantStyles: Record<ButtonVariant, string> = {
  default: "bg-emerald-500 hover:bg-emerald-400 text-white",
  outline: "border border-slate-700 bg-transparent hover:bg-slate-800",
  ghost: "text-slate-200 hover:bg-slate-800",
  destructive: "bg-rose-600 hover:bg-rose-500 text-white"
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = "default", type = "button", ...props }, ref) => (
    <button
      ref={ref}
      type={type}
      className={cn(
        "inline-flex items-center justify-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-emerald-400 disabled:pointer-events-none disabled:opacity-50",
        variantStyles[variant],
        className
      )}
      {...props}
    />
  )
);

Button.displayName = "Button";
