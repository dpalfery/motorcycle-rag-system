import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, Square, Play, Trash2, FileText, Sheet, Workflow } from "lucide-react";
import { useConfig } from "@/lib/config";
import { processor, toStartConfig, type ProcessorJob } from "@/lib/processor";
import { Button, Card, MetricCard, PageHeader, StatusPill, Empty } from "@/components/ui";

function jobIcon(job: ProcessorJob) {
  const t = (job.document_type ?? job.job_id ?? "").toLowerCase();
  if (t.includes("pdf")) return <FileText className="h-5 w-5 text-muted" />;
  if (t.includes("csv")) return <Sheet className="h-5 w-5 text-muted" />;
  return <Workflow className="h-5 w-5 text-muted" />;
}

function isDone(s: string) {
  return ["completed", "complete", "done"].includes(s.toLowerCase());
}

export default function ProcessorScreen() {
  const { config } = useConfig();
  const qc = useQueryClient();
  const port = config.localProcessorPort;

  const running = useQuery({
    queryKey: ["proc", "running"],
    queryFn: () => processor.isRunning(),
    refetchInterval: 5000,
  });

  const health = useQuery({
    queryKey: ["proc", "health", port],
    queryFn: () => processor.health(port),
    refetchInterval: 15000,
    enabled: !!running.data,
    retry: 0,
  });

  const jobs = useQuery({
    queryKey: ["proc", "jobs", port],
    queryFn: () => processor.jobs(port),
    refetchInterval: 15000,
    enabled: !!running.data,
    retry: 0,
  });

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ["proc"] });
  };

  const start = useMutation({
    mutationFn: () => processor.start(toStartConfig(config)),
    onSuccess: invalidate,
  });
  const stop = useMutation({ mutationFn: () => processor.stop(), onSuccess: invalidate });
  const cleanup = useMutation({
    mutationFn: () => processor.cleanupJobs(port),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["proc", "jobs"] }),
  });

  const isRunning = !!running.data;
  const healthy = (health.data?.status ?? "").toLowerCase() === "healthy";
  const jobList = jobs.data ?? [];

  return (
    <div>
      <PageHeader
        title="Local processor"
        subtitle={<span className="font-mono">127.0.0.1:{port}</span>}
        actions={
          <>
            <StatusPill ok={isRunning} label={isRunning ? "Running" : "Stopped"} />
            <Button onClick={invalidate}>
              <RefreshCw className="h-4 w-4" /> Refresh
            </Button>
            {isRunning ? (
              <Button variant="danger" onClick={() => stop.mutate()} disabled={stop.isPending}>
                <Square className="h-4 w-4" /> Stop
              </Button>
            ) : (
              <Button variant="primary" onClick={() => start.mutate()} disabled={start.isPending}>
                <Play className="h-4 w-4" /> Start
              </Button>
            )}
          </>
        }
      />

      {start.isError && (
        <div className="mb-4 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          Could not start the processor: {start.error instanceof Error ? start.error.message : String(start.error)}
        </div>
      )}

      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
        <MetricCard
          label="Health"
          tone={healthy ? "success" : "default"}
          value={isRunning ? (healthy ? "Healthy" : (health.data?.status ?? "—")) : "Offline"}
        />
        <MetricCard label="Active jobs" value={health.data?.active_jobs ?? jobList.length} />
        <MetricCard label="Provider" value={config.embeddingModel ? "Configured" : "—"} />
        <MetricCard label="Port" value={port} />
      </div>

      <Card className="mb-4">
        <div className="mb-3 flex items-center justify-between">
          <span className="text-sm font-medium">Embedding model</span>
          <span className="text-xs text-muted">Set on the Settings screen</span>
        </div>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <div>
            <div className="mb-1 text-xs text-muted">Endpoint</div>
            <div className="truncate rounded-md border border-border bg-background/40 px-2.5 py-1.5 font-mono text-xs">
              {config.embeddingProviderEndpoint || "—"}
            </div>
          </div>
          <div>
            <div className="mb-1 text-xs text-muted">Model</div>
            <div className="truncate rounded-md border border-border bg-background/40 px-2.5 py-1.5 text-sm">
              {config.embeddingModel || "—"}
            </div>
          </div>
        </div>
      </Card>

      <div className="overflow-hidden rounded-xl border border-border">
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <span className="text-sm font-medium">Jobs</span>
          <Button onClick={() => cleanup.mutate()} disabled={!isRunning || cleanup.isPending}>
            <Trash2 className="h-4 w-4" /> Clean up finished
          </Button>
        </div>
        {!isRunning ? (
          <Empty>Start the processor to see jobs.</Empty>
        ) : jobList.length === 0 ? (
          <Empty>No jobs yet.</Empty>
        ) : (
          jobList.map((job) => {
            const pct = isDone(job.status) ? 100 : Math.round((job.progress ?? 0) * 100);
            return (
              <div key={job.job_id} className="flex items-center gap-3 border-b border-border px-4 py-3 last:border-b-0">
                {jobIcon(job)}
                <div className="min-w-0 flex-1">
                  <div className="truncate text-sm">{job.job_id}</div>
                  <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-secondary">
                    <div
                      className={isDone(job.status) ? "h-full bg-success" : "h-full bg-primary"}
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                </div>
                <span className="w-20 text-right text-xs text-muted">
                  {isDone(job.status) ? "Done" : `${pct}% · ${job.status}`}
                </span>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
