import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";

// Mock the axios client so the store's probe() never hits the network during these tests.
vi.mock("@/lib/apiClient", () => ({
  api: { get: vi.fn().mockResolvedValue({ data: { status: "Healthy", checks: {} } }) },
}));

import HealthStatusIndicator from "./HealthStatusIndicator";
import { useHealthCheck } from "@/lib/healthCheck";

describe("HealthStatusIndicator", () => {
  beforeEach(() => {
    useHealthCheck.getState().reset();
  });

  afterEach(() => cleanup());

  it("renders a degraded banner when the cloud API is unreachable", () => {
    useHealthCheck.setState({
      phase: "checked",
      apiReachable: false,
      selfStatus: null,
      azureSearchStatus: null,
      lastCheckedAt: Date.now(),
      lastError: "Network Error",
    });

    render(<HealthStatusIndicator />);

    // role="alert" only renders in the degraded branch.
    const banner = screen.getByRole("alert");
    expect(banner).toBeInTheDocument();
    expect(screen.getByText("Cloud API:")).toBeInTheDocument();
    expect(screen.getByText("Unreachable")).toBeInTheDocument();
    expect(screen.getByText(/Network Error/)).toBeInTheDocument();
  });

  it("renders a degraded banner when Azure Search is unhealthy", () => {
    useHealthCheck.setState({
      phase: "checked",
      apiReachable: true,
      selfStatus: "Healthy",
      azureSearchStatus: "Unhealthy",
      lastCheckedAt: Date.now(),
      lastError: null,
    });

    render(<HealthStatusIndicator />);

    const banner = screen.getByRole("alert");
    expect(banner).toBeInTheDocument();
    expect(screen.getByText("Azure Search:")).toBeInTheDocument();
    expect(screen.getByText("Unhealthy")).toBeInTheDocument();
  });

  it("renders the subtle strip (no banner) when everything is healthy", () => {
    useHealthCheck.setState({
      phase: "checked",
      apiReachable: true,
      selfStatus: "Healthy",
      azureSearchStatus: "Healthy",
      lastCheckedAt: Date.now(),
      lastError: null,
    });

    render(<HealthStatusIndicator />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByText("Reachable")).toBeInTheDocument();
    expect(screen.getByText("Healthy")).toBeInTheDocument();
  });
});
