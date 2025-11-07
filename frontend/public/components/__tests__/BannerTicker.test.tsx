import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import BannerTicker, { BannerTickerContent } from "../BannerTicker";
import type { TickerData } from "../useSecuritiesFeed";
import useSecuritiesFeed from "../useSecuritiesFeed";

vi.mock("../useSecuritiesFeed", () => ({
  __esModule: true,
  default: vi.fn(),
}));

const mockUseSecuritiesFeed = vi.mocked(useSecuritiesFeed);

describe("BannerTicker component", () => {
  beforeEach(() => {
    mockUseSecuritiesFeed.mockReset();
  });

  const baseTickers: TickerData[] = [
    {
      ticker: "FUNO11",
      lastPrice: 25.18,
      yieldTTM: 0.0673,
      updatedAt: new Date().toISOString(),
    },
    {
      ticker: "TERRA13",
      lastPrice: 18.1,
      yieldTTM: 0.058,
      updatedAt: new Date().toISOString(),
    },
  ];

  it("renders a list of tickers", () => {
    render(
      <BannerTickerContent tickers={baseTickers} lastUpdated={baseTickers[0].updatedAt} isStale={false} />
    );

    expect(screen.getByText("FUNO11")).toBeInTheDocument();
    expect(screen.getByText("TERRA13")).toBeInTheDocument();
  });

  it("shows desactualizado label when data is stale", () => {
    render(
      <BannerTickerContent tickers={baseTickers} lastUpdated={baseTickers[0].updatedAt} isStale />
    );

    expect(screen.getByText("🔸 Desactualizado")).toBeInTheDocument();
  });

  it("applies correct color when price increases", () => {
    const { rerender } = render(
      <BannerTickerContent tickers={[{ ...baseTickers[0], lastPrice: 20 }]} lastUpdated={baseTickers[0].updatedAt} isStale={false} />
    );

    rerender(
      <BannerTickerContent tickers={[{ ...baseTickers[0], lastPrice: 22 }]} lastUpdated={baseTickers[0].updatedAt} isStale={false} />
    );

    const price = screen.getByText(/\$22.00/);
    expect(price.className).toContain("text-emerald");
  });

  it("renders error banner when hook returns an error and no data", async () => {
    mockUseSecuritiesFeed.mockReturnValueOnce({
      data: [],
      isLoading: false,
      isStale: false,
      lastUpdated: null,
      error: "Datos no disponibles",
      refresh: vi.fn(),
    });

    render(<BannerTicker />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Datos no disponibles");
  });

  it("expands on hover to allow interactions", async () => {
    const refresh = vi.fn();
    mockUseSecuritiesFeed.mockReturnValueOnce({
      data: baseTickers,
      isLoading: false,
      isStale: false,
      lastUpdated: baseTickers[0].updatedAt,
      error: undefined,
      refresh,
    });

    render(<BannerTicker />);
    const region = screen.getByRole("region");
    fireEvent.mouseEnter(region.parentElement!);
    expect(region).toBeVisible();
  });
});
