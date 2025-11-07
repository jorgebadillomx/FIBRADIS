import { useCallback, useEffect, useRef, useState } from "react";

export interface TickerData {
  ticker: string;
  lastPrice: number;
  yieldTTM?: number;
  updatedAt: string;
}

export interface SecuritiesFeedState {
  data: TickerData[];
  isLoading: boolean;
  lastUpdated: string | null;
  isStale: boolean;
  error?: string;
  refresh: () => Promise<void>;
}

const CACHE_KEY = "fibradis:securities";
const CACHE_TTL = 60_000;
const POLL_INTERVAL = 60_000;
const STALE_THRESHOLD = 5 * 60_000;
const API_URL = "https://api.fibradis.mx/v1/securities";

interface CachedPayload {
  timestamp: number;
  data: TickerData[];
}

const readCache = (): CachedPayload | null => {
  try {
    const raw = window.localStorage.getItem(CACHE_KEY);
    if (!raw) return null;
    const payload = JSON.parse(raw) as CachedPayload;
    if (!Array.isArray(payload.data)) {
      return null;
    }
    return payload;
  } catch (error) {
    console.warn("[FIBRADIS][BannerTicker] cache_read_failed", error);
    return null;
  }
};

const writeCache = (payload: CachedPayload) => {
  try {
    window.localStorage.setItem(CACHE_KEY, JSON.stringify(payload));
  } catch (error) {
    console.warn("[FIBRADIS][BannerTicker] cache_write_failed", error);
  }
};

const maxUpdatedAt = (data: TickerData[]): string | null => {
  if (!data.length) return null;
  const maxDate = data
    .map((item) => new Date(item.updatedAt).getTime())
    .filter((time) => Number.isFinite(time))
    .reduce((acc, curr) => Math.max(acc, curr), 0);
  return Number.isFinite(maxDate) ? new Date(maxDate).toISOString() : null;
};

export const useSecuritiesFeed = (): SecuritiesFeedState => {
  const [data, setData] = useState<TickerData[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [lastUpdated, setLastUpdated] = useState<string | null>(null);
  const [error, setError] = useState<string | undefined>();
  const [isStale, setIsStale] = useState<boolean>(false);
  const intervalRef = useRef<number | null>(null);
  const failureCountRef = useRef<number>(0);
  const pollingDisabledRef = useRef<boolean>(false);

  const evaluateStaleness = useCallback(
    (updatedAt: string | null) => {
      if (!updatedAt) {
        setIsStale(false);
        return;
      }
      const diff = Date.now() - new Date(updatedAt).getTime();
      setIsStale(diff > STALE_THRESHOLD);
    },
    []
  );

  const applyData = useCallback((payload: TickerData[], source: "network" | "cache") => {
    setData(payload);
    const updatedAt = maxUpdatedAt(payload);
    setLastUpdated(updatedAt);
    evaluateStaleness(updatedAt);
    if (source === "network") {
      writeCache({ data: payload, timestamp: Date.now() });
    }
  }, [evaluateStaleness]);

  const clearPolling = useCallback(() => {
    if (intervalRef.current) {
      window.clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
  }, []);

  const fetchFromNetwork = useCallback(async () => {
    if (pollingDisabledRef.current) {
      return;
    }
    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), 10_000);
    const fetchStartedAt = performance.now();
    try {
      const response = await fetch(API_URL, {
        method: "GET",
        cache: "no-cache",
        signal: controller.signal,
        headers: {
          Accept: "application/json",
        },
      });
      window.clearTimeout(timeoutId);

      if (!response.ok) {
        throw new Error(`API responded with ${response.status}`);
      }
      const payload = (await response.json()) as TickerData[];
      applyData(payload, "network");
      setIsLoading(false);
      setError(undefined);
      failureCountRef.current = 0;
      console.info(
        "[FIBRADIS][BannerTicker]",
        JSON.stringify({ fetch_time_ms: Math.round(performance.now() - fetchStartedAt), cache_hit: false })
      );
    } catch (err) {
      window.clearTimeout(timeoutId);
      failureCountRef.current += 1;
      const cache = readCache();
      if (cache) {
        applyData(cache.data, "cache");
        console.info(
          "[FIBRADIS][BannerTicker]",
          JSON.stringify({ fetch_time_ms: null, cache_hit: true })
        );
      }
      const offline = typeof navigator !== "undefined" && navigator.onLine === false;
      if (!cache) {
        setError(offline ? "Sin conexión. Mostrando últimos datos conocidos." : "Datos no disponibles");
      } else if (offline) {
        setError("Sin conexión. Mostrando últimos datos cacheados.");
      } else {
        setError("No fue posible actualizar los precios.");
      }
      setIsLoading(false);
      if (failureCountRef.current >= 3) {
        pollingDisabledRef.current = true;
        clearPolling();
        console.warn("[FIBRADIS][BannerTicker] polling deshabilitado por errores consecutivos");
      }
    }
  }, [applyData, clearPolling]);

  const refresh = useCallback(async () => {
    setIsLoading(true);
    await fetchFromNetwork();
  }, [fetchFromNetwork]);

  const startPolling = useCallback(() => {
    if (pollingDisabledRef.current || document.visibilityState !== "visible") {
      return;
    }
    clearPolling();
    intervalRef.current = window.setInterval(() => {
      void fetchFromNetwork();
    }, POLL_INTERVAL);
  }, [clearPolling, fetchFromNetwork]);

  useEffect(() => {
    const cached = readCache();
    if (cached) {
      const isFresh = Date.now() - cached.timestamp < CACHE_TTL;
      if (isFresh) {
        applyData(cached.data, "cache");
        setIsLoading(false);
        console.info(
          "[FIBRADIS][BannerTicker]",
          JSON.stringify({ fetch_time_ms: null, cache_hit: true })
        );
      }
    }
    void fetchFromNetwork();

    const handleVisibilityChange = () => {
      if (document.visibilityState === "visible") {
        pollingDisabledRef.current = false;
        void fetchFromNetwork();
        startPolling();
      } else {
        clearPolling();
      }
    };

    document.addEventListener("visibilitychange", handleVisibilityChange);
    startPolling();

    return () => {
      clearPolling();
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, [applyData, clearPolling, fetchFromNetwork, startPolling]);

  useEffect(() => {
    if (!lastUpdated) return;
    const interval = window.setInterval(() => {
      evaluateStaleness(lastUpdated);
    }, 30_000);
    return () => window.clearInterval(interval);
  }, [evaluateStaleness, lastUpdated]);

  return {
    data,
    isLoading,
    lastUpdated,
    isStale,
    error,
    refresh,
  };
};

export default useSecuritiesFeed;
