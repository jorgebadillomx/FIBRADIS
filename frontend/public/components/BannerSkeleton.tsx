import type { FC } from "react";

const shimmer =
  "animate-pulse bg-gradient-to-r from-slate-200 via-slate-100 to-slate-200 dark:from-slate-700 dark:via-slate-600 dark:to-slate-700";

export const BannerSkeleton: FC = () => {
  return (
    <div
      role="status"
      aria-live="polite"
      className="pointer-events-none fixed inset-x-4 bottom-4 z-40 flex h-20 items-center justify-between gap-4 rounded-2xl bg-white/80 px-4 shadow-lg backdrop-blur dark:bg-slate-900/80 sm:inset-x-8"
    >
      <div className={`h-10 w-24 rounded-xl ${shimmer}`} />
      <div className="hidden flex-1 items-center justify-evenly gap-4 sm:flex">
        <div className={`h-6 w-32 rounded-lg ${shimmer}`} />
        <div className={`h-6 w-24 rounded-lg ${shimmer}`} />
        <div className={`h-6 w-28 rounded-lg ${shimmer}`} />
      </div>
      <div className={`h-6 w-20 rounded-lg ${shimmer}`} />
      <span className="sr-only">Cargando precios de FIBRAs…</span>
    </div>
  );
};

export default BannerSkeleton;
