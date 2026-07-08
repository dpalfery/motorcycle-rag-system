import { useEffect, useRef } from "react";
import { CheckCircle2, AlertTriangle, XCircle, RefreshCw, Loader2 } from "lucide-react";
import { useHealthCheck, type HealthStatus } from "@/lib/healthCheck";
import { cn } from "@/lib/utils";

/**
 * Startup + live health indicator for the cloud API and Azure AI Search.
 *
 * Reads the Zustand store populated by the early startup probe in `main.tsx`
 * (`GET /health` via the `api` axios client). Surfaces two signals:
 *   - Cloud API   (transport reachability + `checks.self`)
 *   - Azure Search (`checks.azure_search`)
 * When either is not fully healthy, a full-width degraded banner replaces the subtle
 * strip so the operator always sees a clear status — never a silent hang or white screen.
 */

const REFRESH_INTERVAL_MS = 60_000;
/** Re-probe on shell mount only if the early startup data is older than this (or absent). */
const STALE_MS = 30_000;

type Level = "ok" | "warn" | "down";

interface Derived {
  level: Level;
  label: string;
}

function deriveApi(apiReachable: boolean, selfStatus: HealthStatus | null): Derived {
  if (!apiReachable) return { level: "down", label: "Unreachable" };
  if (selfStatus === "Unhealthy") return { level: "down", label: "Unhealthy" };
  if (selfStatus === "Degraded") return { level: "warn", label: "Degraded" };
  return { level: "ok", label: "Reachable" };
}

function deriveSearch(status: HealthStatus | null): Derived {
  if (status === "Healthy") return { level: "ok", label: "Healthy" };
  if (status === "Degraded") return { level: "warn", label: "Degraded" };
  if (status === "Unhealthy") return { level: "down", label: "Unhealthy" };
  // Absent key (e.g. InMemoryShim provider) — cautionary, not a hard failure.
  return { level: "warn", label: "Not reported" };
}

const LEVEL_ICON: Record<Level, typeof CheckCircle2> = {
  ok: CheckCircle2,
  warn: AlertTriangle,
  down: XCircle,
};

const LEVEL_TEXT: Record<Level, string> = {
  ok: "text-success",
  warn: "text-warning",
  down: "text-danger",
};

function StatusItem({ name, derived }: { name: string; derived: Derived }) {
  const Icon = LEVEL_ICON[derived.level];
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
      <Icon className={cn("h-3.5 w-3.5 shrink-0", LEVEL_TEXT[derived.level])} />
      <span className="text-muted">{name}:</span>
      <span className={cn("font-medium", LEVEL_TEXT[derived.level])}>{derived.label}</span>
    </span>
  );
}

export default function HealthStatusIndicator() {
  const phase = useHealthCheck((s) => s.phase);
  const apiReachable = useHealthCheck((s) => s.apiReachable);
  const selfStatus = useHealthCheck((s) => s.selfStatus);
  const azureSearchStatus = useHealthCheck((s) => s.azureSearchStatus);
  const lastCheckedAt = useHealthCheck((s) => s.lastCheckedAt);
  const lastError = useHealthCheck((s) => s.lastError);
  const probe = useHealthCheck((s) => s.probe);

  const probedOnMount = useRef(false);

  // Refresh on mount if the early startup data is stale/absent, then poll on a light cadence.
  useEffect(() => {
    if (!probedOnMount.current) {
      probedOnMount.current = true;
      const checkedAt = useHealthCheck.getState().lastCheckedAt;
      if (checkedAt === null || Date.now() - checkedAt > STALE_MS) {
        void probe();
      }
    }
    const id = window.setInterval(() => {
      void probe();
    }, REFRESH_INTERVAL_MS);
    return () => window.clearInterval(id);
  }, [probe]);

  const apiState = deriveApi(apiReachable, selfStatus);
  const searchState = deriveSearch(azureSearchStatus);
  const checking = phase === "checking";
  const degraded = apiState.level !== "ok" || searchState.level !== "ok";
  const critical = apiState.level === "down" || searchState.level === "down";

  const lastCheckedText = lastCheckedAt
    ? new Date(lastCheckedAt).toLocaleTimeString(undefined, { timeStyle: "short" })
    : "—";

  // Degraded: prominent banner. Healthy: subtle one-line strip under the title bar.
  if (degraded) {
    return (
      <div
        role="alert"
        className={cn(
          "flex items-center gap-3 border-b px-4 py-2 text-xs",
          critical
            ? "border-danger/40 bg-danger/10 text-danger"
            : "border-warning/40 bg-warning/10 text-warning",
        )}
      >
        <AlertTriangle className="h-4 w-4 shrink-0" />
        <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-4 gap-y-1">
          <StatusItem name="Cloud API" derived={apiState} />
          <StatusItem name="Azure Search" derived={searchState} />
          {lastError && (
            <span className="min-w-0 truncate text-muted-foreground">
              <span className="opacity-70">Detail:</span> {lastError}
            </span>
          )}
        </div>
        <button
          type="button"
          className="no-drag inline-flex items-center gap-1.5 rounded px-2 py-1 text-xs font-medium text-current hover:bg-black/10 disabled:opacity-50"
          onClick={() => void probe()}
          disabled={checking}
        >
          {checking ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
          ) : (
            <RefreshCw className="h-3.5 w-3.5" />
          )}
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="flex items-center gap-3 border-b border-border bg-secondary/30 px-4 py-1.5 text-xs">
      <div className="flex items-center gap-4">
        <StatusItem name="Cloud API" derived={apiState} />
        <StatusItem name="Azure Search" derived={searchState} />
      </div>
      <div className="ml-auto flex items-center gap-2 text-muted">
        {checking && <Loader2 className="h-3 w-3 animate-spin" />}
        <span className="opacity-70">Last checked {lastCheckedText}</span>
        <button
          type="button"
          className="no-drag inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-muted hover:bg-secondary hover:text-foreground disabled:opacity-50"
          onClick={() => void probe()}
          disabled={checking}
          aria-label="Refresh health status"
        >
          {checking ? <Loader2 className="h-3 w-3 animate-spin" /> : <RefreshCw className="h-3 w-3" />}
        </button>
      </div>
    </div>
  );
}
