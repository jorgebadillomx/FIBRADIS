import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useSecuritiesFeed } from "../useSecuritiesFeed";

const mockResponse = (data: unknown, ok = true) => {
  return Promise.resolve({
    ok,
    json: () => Promise.resolve(data),
  }) as unknown as Promise<Response>;
};

describe("useSecuritiesFeed", () => {
  const now = new Date();
  const sample = [
    {
      ticker: "FUNO11",
      lastPrice: 25.18,
      yieldTTM: 0.0673,
      updatedAt: now.toISOString(),
    },
  ];

  beforeEach(() => {
    vi.restoreAllMocks();
    vi.useFakeTimers();
    vi.setSystemTime(now);
    window.localStorage.clear();
  });

  afterEach(() => {
    vi.useRealTimers();
    Object.defineProperty(navigator, "onLine", {
      configurable: true,
      value: true,
    });
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "visible",
    });
  });

  it("fetches and returns data", async () => {
    vi.spyOn(global, "fetch").mockImplementation(() => mockResponse(sample));

    const { result } = renderHook(() => useSecuritiesFeed());

    await waitFor(() => expect(result.current.data).toHaveLength(1));
    expect(result.current.isLoading).toBe(false);
    expect(result.current.lastUpdated).toBe(sample[0].updatedAt);
  });

  it("marks data as stale if last update older than 5 minutes", async () => {
    const staleDate = new Date(now.getTime() - 6 * 60_000).toISOString();
    vi.spyOn(global, "fetch").mockImplementation(() =>
      mockResponse([
        {
          ...sample[0],
          updatedAt: staleDate,
        },
      ])
    );

    const { result } = renderHook(() => useSecuritiesFeed());

    await waitFor(() => expect(result.current.isStale).toBe(true));
  });

  it("polls every 60 seconds", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockImplementation(() => mockResponse(sample));

    renderHook(() => useSecuritiesFeed());
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());

    act(() => {
      vi.advanceTimersByTime(60_000);
    });

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("stops polling when tab is hidden", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockImplementation(() => mockResponse(sample));

    renderHook(() => useSecuritiesFeed());
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());

    act(() => {
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "hidden",
      });
      document.dispatchEvent(new Event("visibilitychange"));
      vi.advanceTimersByTime(120_000);
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("uses cached data when fetch fails", async () => {
    window.localStorage.setItem(
      "fibradis:securities",
      JSON.stringify({ timestamp: Date.now(), data: sample })
    );
    vi.spyOn(global, "fetch").mockImplementation(() => Promise.reject(new Error("Network error")));

    const { result } = renderHook(() => useSecuritiesFeed());

    await waitFor(() => expect(result.current.data).toHaveLength(1));
    expect(result.current.error).toBe("No fue posible actualizar los precios.");
  });

  it("displays offline message when navigator is offline", async () => {
    window.localStorage.setItem(
      "fibradis:securities",
      JSON.stringify({ timestamp: Date.now(), data: sample })
    );
    vi.spyOn(global, "fetch").mockImplementation(() => Promise.reject(new Error("Network error")));
    Object.defineProperty(navigator, "onLine", {
      configurable: true,
      value: false,
    });

    const { result } = renderHook(() => useSecuritiesFeed());

    await waitFor(() => expect(result.current.error).toContain("Sin conexión"));
  });
});
