import { describe, it, expect, vi, beforeEach } from "vitest";
import { useHealthCheck, pickStatus, PROBE_TIMEOUT_MS } from "./healthCheck";
import type { HealthResponse } from "./healthCheck";

vi.mock("./apiClient", () => ({
  api: { get: vi.fn() },
}));

// Imported after the module mock so `api` is the mocked instance.
import { api } from "./apiClient";

function healthResponse(checks: HealthResponse["checks"]): HealthResponse {
  return { status: "Healthy", totalDuration: "00:00:00.0100000", checks };
}

function ok(data: HealthResponse) {
  vi.mocked(api.get).mockResolvedValueOnce({ data } as any);
}

describe("pickStatus", () => {
  it("reads a status from a known check key", () => {
    expect(pickStatus({ self: { status: "Healthy" } }, "self")).toBe("Healthy");
  });

  it("returns null for missing keys", () => {
    expect(pickStatus({ self: { status: "Healthy" } }, "azure_search")).toBeNull();
  });

  it("is defensive against malformed payloads", () => {
    expect(pickStatus(null, "self")).toBeNull();
    expect(pickStatus(undefined, "self")).toBeNull();
    expect(pickStatus("nope", "self")).toBeNull();
    expect(pickStatus({ self: null }, "self")).toBeNull();
    expect(pickStatus({ self: { status: 123 } }, "self")).toBeNull();
    expect(pickStatus({ self: {} }, "self")).toBeNull();
  });
});

describe("useHealthCheck probe", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useHealthCheck.getState().reset();
  });

  it("starts in the idle phase with nothing reachable", () => {
    const s = useHealthCheck.getState();
    expect(s.phase).toBe("idle");
    expect(s.apiReachable).toBe(false);
    expect(s.selfStatus).toBeNull();
    expect(s.azureSearchStatus).toBeNull();
    expect(s.lastCheckedAt).toBeNull();
    expect(s.lastError).toBeNull();
  });

  it("parses self + azure_search on a successful probe", async () => {
    ok(
      healthResponse({
        self: { status: "Healthy", duration: "00:00:00.0010000" },
        azure_search: { status: "Healthy", duration: "00:00:00.0050000" },
      }),
    );

    await useHealthCheck.getState().probe();

    const s = useHealthCheck.getState();
    expect(s.phase).toBe("checked");
    expect(s.apiReachable).toBe(true);
    expect(s.selfStatus).toBe("Healthy");
    expect(s.azureSearchStatus).toBe("Healthy");
    expect(s.lastCheckedAt).not.toBeNull();
    expect(s.lastError).toBeNull();
    expect(api.get).toHaveBeenCalledWith("/health", { timeout: PROBE_TIMEOUT_MS });
  });

  it("reflects a degraded Azure Search check", async () => {
    ok(
      healthResponse({
        self: { status: "Healthy" },
        azure_search: { status: "Degraded", description: "returned 503" },
      }),
    );

    await useHealthCheck.getState().probe();

    const s = useHealthCheck.getState();
    expect(s.apiReachable).toBe(true);
    expect(s.azureSearchStatus).toBe("Degraded");
  });

  it("treats an absent azure_search key as null (not down)", async () => {
    // When Search:ChunkIndexingProvider == InMemoryShim the key is unregistered server-side.
    ok(healthResponse({ self: { status: "Healthy" } }));

    await useHealthCheck.getState().probe();

    const s = useHealthCheck.getState();
    expect(s.apiReachable).toBe(true);
    expect(s.selfStatus).toBe("Healthy");
    expect(s.azureSearchStatus).toBeNull();
  });

  it("marks the API unreachable when the call rejects", async () => {
    vi.mocked(api.get).mockRejectedValueOnce(new Error("Network Error"));

    await useHealthCheck.getState().probe();

    const s = useHealthCheck.getState();
    expect(s.phase).toBe("checked");
    expect(s.apiReachable).toBe(false);
    expect(s.selfStatus).toBeNull();
    expect(s.azureSearchStatus).toBeNull();
    expect(s.lastCheckedAt).not.toBeNull();
    expect(s.lastError).toBe("Network Error");
  });

  it("surfaces non-Error rejections as strings", async () => {
    vi.mocked(api.get).mockRejectedValueOnce("boom");

    await useHealthCheck.getState().probe();

    expect(useHealthCheck.getState().lastError).toBe("boom");
  });

  it("reset returns the store to idle", async () => {
    ok(healthResponse({ self: { status: "Healthy" }, azure_search: { status: "Healthy" } }));
    await useHealthCheck.getState().probe();
    expect(useHealthCheck.getState().phase).toBe("checked");

    useHealthCheck.getState().reset();

    const s = useHealthCheck.getState();
    expect(s.phase).toBe("idle");
    expect(s.apiReachable).toBe(false);
    expect(s.lastCheckedAt).toBeNull();
  });
});
