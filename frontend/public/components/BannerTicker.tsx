import { AnimatePresence, motion } from "framer-motion";
import { Clock, ExternalLink } from "lucide-react";
import { useCallback, useEffect, useRef, useState, type FC } from "react";
import BannerError from "./BannerError";
import BannerSkeleton from "./BannerSkeleton";
import useSecuritiesFeed, { type TickerData } from "./useSecuritiesFeed";

export interface BannerTickerProps {
  tickers: TickerData[];
  lastUpdated: string | null;
  isStale: boolean;
  onTickerSelect?: (ticker: TickerData) => void;
}

interface PriceState {
  direction: "up" | "down" | "same";
  previousPrice?: number;
}

const formatCurrency = (value: number): string =>
  new Intl.NumberFormat("es-MX", {
    style: "currency",
    currency: "MXN",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);

const formatPercent = (value?: number): string => {
  if (value === undefined || Number.isNaN(value)) return "--";
  return `${(value * 100).toFixed(2)} %`;
};

const formatTimestamp = (iso?: string | null): string => {
  if (!iso) return "--";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "--";
  return new Intl.DateTimeFormat("es-MX", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  }).format(date);
};

const useSystemTheme = () => {
  const [isDark, setIsDark] = useState<boolean>(() =>
    typeof window !== "undefined"
      ? window.matchMedia("(prefers-color-scheme: dark)").matches
      : false
  );

  useEffect(() => {
    if (typeof window === "undefined") return;
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = (event: MediaQueryListEvent) => setIsDark(event.matches);
    media.addEventListener("change", handler);
    setIsDark(media.matches);
    return () => media.removeEventListener("change", handler);
  }, []);

  return isDark;
};

const TickerCard: FC<{
  ticker: TickerData;
  priceState: PriceState;
  onSelect?: (ticker: TickerData) => void;
}> = ({ ticker, priceState, onSelect }) => {
  const colorMap = {
    up: "text-emerald-600 dark:text-emerald-400",
    down: "text-rose-600 dark:text-rose-400",
    same: "text-slate-600 dark:text-slate-300",
  } as const;

  return (
    <motion.button
      layout
      onClick={() => onSelect?.(ticker)}
      className="group flex min-w-[12rem] flex-col items-start rounded-2xl bg-white/80 p-3 text-left font-medium shadow-sm transition hover:bg-white dark:bg-slate-800/80 dark:hover:bg-slate-800"
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -8 }}
    >
      <span className="flex items-center gap-2 text-xs uppercase tracking-wide text-slate-500 dark:text-slate-400">
        <span aria-hidden className="h-2 w-2 rounded-full bg-emerald-400 group-hover:bg-emerald-500" />
        {ticker.ticker}
      </span>
      <span className={`mt-1 text-lg ${colorMap[priceState.direction]}`}>
        {formatCurrency(ticker.lastPrice)}
      </span>
      <span className="mt-1 text-xs text-slate-500 dark:text-slate-400">
        Yield TTM · {formatPercent(ticker.yieldTTM)}
      </span>
      {priceState.previousPrice !== undefined && (
        <span className="sr-only">
          Precio anterior {formatCurrency(priceState.previousPrice)}
        </span>
      )}
    </motion.button>
  );
};

export const BannerTickerContent: FC<BannerTickerProps> = ({
  tickers,
  lastUpdated,
  isStale,
  onTickerSelect,
}) => {
  const prevPrices = useRef<Map<string, number>>(new Map());

  const states = tickers.map((ticker) => {
    const prev = prevPrices.current.get(ticker.ticker);
    let direction: PriceState["direction"] = "same";
    if (prev !== undefined) {
      if (ticker.lastPrice > prev) direction = "up";
      else if (ticker.lastPrice < prev) direction = "down";
    }
    return { ticker, state: { direction, previousPrice: prev } satisfies PriceState };
  });

  useEffect(() => {
    const map = new Map<string, number>();
    tickers.forEach((ticker) => {
      map.set(ticker.ticker, ticker.lastPrice);
    });
    prevPrices.current = map;
  }, [tickers]);

  return (
    <div
      role="region"
      aria-live="polite"
      aria-label="Precios más recientes de FIBRAs"
      className="pointer-events-auto fixed inset-x-0 bottom-0 z-40 flex flex-col gap-2 bg-gradient-to-t from-white/95 via-white/90 to-transparent p-3 text-slate-900 shadow-[0_-8px_24px_rgba(15,23,42,0.1)] backdrop-blur dark:from-slate-900/95 dark:via-slate-900/80 dark:text-slate-100"
    >
      <div className="flex items-center justify-between gap-2 text-xs font-medium uppercase tracking-wide text-slate-500 dark:text-slate-400">
        <span className="flex items-center gap-2">
          <Clock aria-hidden className="h-3.5 w-3.5" />
          Última actualización · {formatTimestamp(lastUpdated)}
        </span>
        {isStale && (
          <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-amber-700 dark:bg-amber-500/20 dark:text-amber-200">
            🔸 Desactualizado
          </span>
        )}
      </div>
      <div className="flex w-full items-stretch gap-2 overflow-x-auto pb-2">
        <AnimatePresence initial={false}>
          {states.map(({ ticker, state }) => (
            <TickerCard key={ticker.ticker} ticker={ticker} priceState={state} onSelect={onTickerSelect} />
          ))}
        </AnimatePresence>
      </div>
    </div>
  );
};

export const BannerTicker: FC = () => {
  const { data, isLoading, isStale, lastUpdated, error, refresh } = useSecuritiesFeed();
  const [isExpanded, setIsExpanded] = useState<boolean>(false);
  const isDark = useSystemTheme();

  const handleTickerSelect = useCallback((ticker: TickerData) => {
    console.debug("[FIBRADIS][BannerTicker] ticker_select", ticker.ticker);
    if (ticker && ticker.ticker) {
      window.dispatchEvent(
        new CustomEvent("fibradis:ticker:selected", { detail: ticker.ticker })
      );
    }
  }, []);

  if (isLoading && data.length === 0) {
    return <BannerSkeleton />;
  }

  if (error && data.length === 0) {
    return <BannerError message={error} retry={() => void refresh()} />;
  }

  return (
    <div data-theme={isDark ? "dark" : "light"} className="pointer-events-none">
      <AnimatePresence>
        {data.length > 0 && (
          <motion.div
            layout
            transition={{ type: "spring", stiffness: 260, damping: 30 }}
            tabIndex={0}
            aria-expanded={isExpanded}
            onFocus={() => setIsExpanded(true)}
            onBlur={() => setIsExpanded(false)}
            onMouseEnter={() => setIsExpanded(true)}
            onMouseLeave={() => setIsExpanded(false)}
            className={
              isExpanded
                ? "pointer-events-auto"
                : "pointer-events-auto sm:pointer-events-none"
            }
          >
            <BannerTickerContent
              tickers={data}
              lastUpdated={lastUpdated}
              isStale={isStale}
              onTickerSelect={handleTickerSelect}
            />
          </motion.div>
        )}
      </AnimatePresence>
      {error && data.length > 0 && (
        <div className="fixed right-4 bottom-24 z-40 flex max-w-xs items-start gap-2 rounded-2xl bg-slate-900/90 p-3 text-xs font-medium text-white shadow-lg backdrop-blur sm:right-8">
          <ExternalLink aria-hidden className="mt-0.5 h-3.5 w-3.5" />
          <div>
            <p className="text-white/90">{error}</p>
            <button
              type="button"
              onClick={() => void refresh()}
              className="mt-2 rounded-lg bg-white/10 px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-white transition hover:bg-white/20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
            >
              Reintentar
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default BannerTicker;
