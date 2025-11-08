import * as DialogPrimitive from "@radix-ui/react-dialog";
import { ReactNode } from "react";
import { Button } from "./button";

export const Dialog = DialogPrimitive.Root;
export const DialogTrigger = DialogPrimitive.Trigger;
export const DialogClose = DialogPrimitive.Close;

export const DialogContent = ({ children }: { children: ReactNode }) => (
  <DialogPrimitive.Portal>
    <DialogPrimitive.Overlay className="fixed inset-0 bg-black/70 backdrop-blur-sm" />
    <DialogPrimitive.Content className="fixed left-1/2 top-1/2 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-slate-800 bg-slate-900 p-6 shadow-2xl">
      {children}
    </DialogPrimitive.Content>
  </DialogPrimitive.Portal>
);

export const DialogHeader = ({ title, description }: { title: string; description?: string }) => (
  <header className="mb-4 space-y-1">
    <h2 className="text-xl font-semibold text-white">{title}</h2>
    {description ? <p className="text-sm text-slate-400">{description}</p> : null}
  </header>
);

export const DialogFooter = ({ children }: { children: ReactNode }) => (
  <div className="mt-6 flex justify-end gap-2">{children}</div>
);

export const DialogAction = ({ label, variant = "default", onClick }: { label: string; variant?: Parameters<typeof Button>[0]["variant"]; onClick?: () => void }) => (
  <Button variant={variant} onClick={onClick}>
    {label}
  </Button>
);
