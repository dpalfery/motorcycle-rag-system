import { create } from "zustand";
import { api } from "./apiClient";

/**
 * Startup + background health probe for the cloud API.
 *
 * Mirrors the backend `HealthCheckResponseWriter` shape
 * (`1-Presentation/MotorcycleRAG.API/Services/HealthCheckResponseWriter.cs`). The
 * `/health` endpoint (`WebApplicationExtensions.cs:75-78`) is **anonymous** and
 * **always returns HTTP 200** — the real state lives in the JSON `status` field and
 * the per-check `checks` map. Two keys matter to the operator:
 *   - `checks.self`         → the cloud API process itself
 *   - `checks.azure_search` → Azure AI Search reachability (registered in
 *                              `HealthChecksConfiguration.cs:23`; absent only when the
 *                              `InMemoryShim` test provider is active, which we treat as
 *                              "not reported" rather than "down").
 *
 * The admin app holds **no direct Search credentials** — both signals come from this
 * single anonymous call via the existing `api` axios client
 * (`src/lib/apiClient.ts`). The request interceptor may attach a bearer token; that is
 * additive and harmless because `/health` is anonymous.
 */

/** ASP.NET Core health check statuses (see `HealthStatus` enum). */
export type HealthStatus = "Healthy" | "Degraded" | "Unhealthy";

export interface HealthCheckEntry {
  status: HealthStatus;
  duration?: string;
  description?: string | null;
  data?: Record<string, unknown> | null;
}

export interface HealthResponse {
  status: HealthStatus;
  totalDuration?: string;
  checks: Record<string, HealthCheckEntry>;
}

export type ProbePhase = "idle" | "checking" | "checked";

export interface HealthCheckState {
  phase: ProbePhase;
  /** True when the `GET /health` call returned a response (transport-level reachability). */
  apiReachable: boolean;
  /** Status of the API process self-check; null when the key is absent or the call failed. */
  selfStatus: HealthStatus | null;
  /** Status of the Azure Search check; null when the key is absent or the call failed. */
  azureSearchStatus: HealthStatus | null;
  /** Epoch ms of the last completed probe. */
  lastCheckedAt: number | null;
  /** Last transport-level failure message (null on success). */
  lastError: string | null;
  probe: () => Promise<void>;
  reset: () => void;
}

/**
 * Per-request timeout for the probe. Shorter than the `api` client's 30s default so a
 * dead/unreachable API surfaces to the operator as "unreachable" within seconds instead
 * of hanging the startup indicator for half a minute (per acceptance criterion #2: no
 * silent hang). The backend `AzureSearchHealthCheck` itself caps at 5s internally.
 */
export const PROBE_TIMEOUT_MS = 10_000;

/** Safely read a health-check status from an untrusted response body. */
export function pickStatus(checks: unknown, key: string): HealthStatus | null {
  if (!checks || typeof checks !== "object") return null;
  const entry = (checks as Record<string, unknown>)[key];
  if (!entry || typeof entry !== "object") return null;
  const status = (entry as Record<string, unknown>).status;
  return typeof status === "string" ? (status as HealthStatus) : null;
}

export const useHealthCheck = create<HealthCheckState>((set) => ({
  phase: "idle",
  apiReachable: false,
  selfStatus: null,
  azureSearchStatus: null,
  lastCheckedAt: null,
  lastError: null,
  probe: async () => {
    set({ phase: "checking", lastError: null });
    try {
      const res = await api.get<HealthResponse>("/health", { timeout: PROBE_TIMEOUT_MS });
      const checks = res.data?.checks;
      set({
        phase: "checked",
        apiReachable: true,
        selfStatus: pickStatus(checks, "self"),
        azureSearchStatus: pickStatus(checks, "azure_search"),
        lastCheckedAt: Date.now(),
        lastError: null,
      });
    } catch (err) {
      // Any rejection = transport-level failure (the endpoint is unreachable / timed out /
      // returned non-2xx). `/health` always returns 200 server-side, so a rejection is a
      // genuine connectivity problem, not a modeled health state.
      set({
        phase: "checked",
        apiReachable: false,
        selfStatus: null,
        azureSearchStatus: null,
        lastCheckedAt: Date.now(),
        lastError: err instanceof Error ? err.message : String(err),
      });
    }
  },
  reset: () =>
    set({
      phase: "idle",
      apiReachable: false,
      selfStatus: null,
      azureSearchStatus: null,
      lastCheckedAt: null,
      lastError: null,
    }),
}));
