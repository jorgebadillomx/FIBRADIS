import type { FC } from "react";
import { AlertTriangle } from "lucide-react";

interface BannerErrorProps {
  message: string;
  retry?: () => void;
}

export const BannerError: FC<BannerErrorProps> = ({ message, retry }) => {
  return (
    <div
      role="alert"
      className="fixed inset-x-4 bottom-4 z-40 flex items-center justify-between gap-4 rounded-2xl border border-red-200 bg-red-50/90 p-4 text-sm font-medium text-red-700 shadow-lg backdrop-blur dark:border-red-900/40 dark:bg-red-900/70 dark:text-red-100 sm:inset-x-8"
    >
      <span className="flex items-center gap-2">
        <AlertTriangle aria-hidden className="h-4 w-4" />
        {message}
      </span>
      {retry && (
        <button
          type="button"
          onClick={retry}
          className="rounded-lg bg-red-600 px-3 py-1 text-xs font-semibold text-white transition hover:bg-red-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-red-600"
        >
          Reintentar
        </button>
      )}
    </div>
  );
};

export default BannerError;
