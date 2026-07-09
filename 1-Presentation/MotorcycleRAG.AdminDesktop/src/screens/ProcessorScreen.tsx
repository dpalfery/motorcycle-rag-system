import { Fragment, useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  RefreshCw,
  Square,
  Play,
  Trash2,
  Upload,
  FileText,
  Sheet,
  Workflow,
  X,
  RotateCcw,
} from "lucide-react";
import axios from "axios";
import { useConfig, type AppConfig } from "@/lib/config";
import { api } from "@/lib/apiClient";
import { formatAdminError, sanitizeForLog } from "@/lib/adminError";
import {
  isMissingProcessorJobError,
  processor,
  toStartConfig,
  type ProcessorJob,
} from "@/lib/processor";
import { Button, Card, MetricCard, PageHeader, StatusPill, Empty } from "@/components/ui";
import IngestionJobFailurePanel from "@/components/IngestionJobFailurePanel";
import {
  filterSupersededIngestionJobs,
  formatIngestionJobLabel,
  isIngestionFailed,
  markIngestionJobRetrying,
  replaceRetriedIngestionJob,
  type IngestionJobStatus,
  updateIngestionJobInList,
} from "@/lib/ingestionJob";
import { cn, formatLocalDateTime, parseUtcIso } from "@/lib/utils";
import {
  getTauriFilePath,
  pickLocalIngestionFile,
  queueLocalIngestionWorkItem,
} from "@/lib/localIngestion";
import { isPathSafe } from "@/lib/pathUtils";

interface UploadConstraints {
  maxFileSizeBytes: number;
  supportedExtensions: string[];
}

const STATUS_OK = ["completed", "complete", "done", "succeeded"];
const STATUS_ERR = ["failed", "error", "cancelled"];
const STATUS_ACTIVE = ["queued", "pending", "processing", "running", "inprogress"];

async function sleep(ms: number) {
  await new Promise((resolve) => window.setTimeout(resolve, ms));
}

function jobIcon(job: ProcessorJob) {
  const t = (job.document_type ?? job.job_id ?? "").toLowerCase();
  if (t.includes("pdf")) return <FileText className="h-5 w-5 text-muted" />;
  if (t.includes("csv")) return <Sheet className="h-5 w-5 text-muted" />;
  return <Workflow className="h-5 w-5 text-muted" />;
}

function isDone(s: string) {
  return ["completed", "complete", "done"].includes(s.toLowerCase());
}

function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).pop() ?? path;
}

function stageLabel(job: ProcessorJob): string {
  if (isDone(job.status)) return "Done";
  const stage = job.stage ?? job.status;
  return stage
    .replace(/-/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function localChunksLabel(job: ProcessorJob): string | null {
  const done = job.chunks_processed ?? 0;
  const total = job.total_chunks ?? 0;
  if (total <= 0) return null;
  return `${done}/${total} chunks`;
}

function fmt(bytes: number) {
  if (bytes >= 1_073_741_824) return `${(bytes / 1_073_741_824).toFixed(1)} GB`;
  if (bytes >= 1_048_576) return `${(bytes / 1_048_576).toFixed(0)} MB`;
  return `${(bytes / 1024).toFixed(0)} KB`;
}

function jobTone(s: string): "success" | "danger" | "default" {
  if (STATUS_OK.includes(s.toLowerCase())) return "success";
  if (STATUS_ERR.includes(s.toLowerCase())) return "danger";
  return "default";
}

function isActiveJobStatus(status?: string) {
  return status ? STATUS_ACTIVE.includes(status.toLowerCase()) : false;
}

function isPdfPath(path: string) {
  return fileNameFromPath(path).toLowerCase().endsWith(".pdf");
}

function getDocumentType(path: string) {
  const name = fileNameFromPath(path).toLowerCase();
  if (name.endsWith(".pdf")) return "manual-pdf";
  if (name.endsWith(".csv")) return "spec-dataset";
  throw new Error("Only PDF manuals and CSV specification files are supported.");
}

function getStartConfiguration(documentType: string) {
  return documentType === "manual-pdf"
    ? {
        extractGraphRelationships: true,
        ocrEnabled: true,
      }
    : undefined;
}

function normalizeExtension(extension: string) {
  return extension.startsWith(".") ? extension : `.${extension}`;
}

async function validateLocalArtifactUpload(config: AppConfig, port: number) {
  if (!config.pythonUploadJobSecret.trim()) {
    throw new Error(
      "Upload job secret is required for local processing. Set it in Settings under Authentication, then restart the local processor.",
    );
  }

  if (!(await processor.isListening(port))) {
    return;
  }

  const currentHealth = await processor.health(port);
  if (currentHealth.api_client_configured !== true) {
    throw new Error(
      "The running local processor was started without the upload job secret. Stop and start the processor after saving the secret.",
    );
  }

  if (currentHealth.accepting_work !== true) {
    throw new Error(
      `The running local processor is not accepting work: ${currentHealth.message ?? currentHealth.status}`,
    );
  }
}

function newId() {
  return crypto.randomUUID();
}

function relativeTime(iso: string) {
  const diff = Date.now() - parseUtcIso(iso).getTime();
  const m = Math.floor(diff / 60_000);
  if (m < 1) return "just now";
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

function formatChunkProgress(job: IngestionJobStatus) {
  if (job.indexedChunkCount === undefined && job.expectedChunkCount === undefined) {
    return null;
  }

  return `${job.indexedChunkCount ?? 0}/${job.expectedChunkCount ?? "?"} chunks`;
}

export default function ProcessorScreen() {
  const { config } = useConfig();
  const qc = useQueryClient();
  const port = config.localProcessorPort;
  const [selectedSourcePath, setSelectedSourcePath] = useState<string | null>(null);
  const [progress, setProgress] = useState(0);
  const [submittedJobId, setSubmittedJobId] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const [supersededRetryJobIds, setSupersededRetryJobIds] = useState<Set<string>>(
    () => new Set(),
  );
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const queueIngestionAction = (sourcePath: string) =>
    `Creating local ${isPdfPath(sourcePath) ? "PDF manual" : "CSV specification"} ingestion work item`;

  const listening = useQuery({
    queryKey: ["proc", "listening", port],
    queryFn: () => processor.isListening(port),
    refetchInterval: 5000,
  });

  const health = useQuery({
    queryKey: ["proc", "health", port],
    queryFn: () => processor.health(port),
    refetchInterval: 15000,
    enabled: !!listening.data,
    retry: 0,
  });

  const localJobs = useQuery({
    queryKey: ["proc", "jobs", port],
    queryFn: () => processor.jobs(port),
    refetchInterval: 15000,
    enabled: !!listening.data,
    retry: 0,
  });

  const constraints = useQuery({
    queryKey: ["ingestion", "constraints"],
    queryFn: async () => {
      const res = await api.get<UploadConstraints>("/api/ingestion/jobs/upload-constraints");
      return res.data;
    },
    retry: 1,
  });

  const jobs = useQuery({
    queryKey: ["jobs", "cloud"],
    queryFn: async () => {
      const res = await api.get<{ items?: IngestionJobStatus[] } | IngestionJobStatus[]>(
        "/api/ingestion/jobs",
      );
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
    refetchInterval: 15_000,
  });

  const submittedJob = useQuery({
    queryKey: ["ingestion", "job", submittedJobId],
    enabled: !!submittedJobId,
    queryFn: async () => {
      const res = await api.get<IngestionJobStatus>(`/api/ingestion/jobs/${submittedJobId}`);
      return res.data;
    },
    refetchInterval: (query) => (isActiveJobStatus(query.state.data?.status) ? 2_000 : false),
  });

  useEffect(() => {
    if (!localJobs.isSuccess || !jobs.isSuccess || !health.isSuccess) return;
    if (health.data.status !== "ok" || health.data.accepting_work !== true) return;

    const currentLocalJobIds = new Set(localJobs.data.map((j) => j.job_id));

    jobs.data.forEach((cloudJob) => {
      if (isActiveJobStatus(cloudJob.status) && cloudJob.docIngestionRunId) {
        if (!currentLocalJobIds.has(cloudJob.docIngestionRunId)) {
          const ageMs = Date.now() - new Date(cloudJob.createdAtUtc).getTime();
          if (ageMs > 30000) {
            api.post(`/api/ingestion/jobs/${cloudJob.jobId}/fail`, null, { params: { reason: "Local processor lost the job (stale)" } })
              .then(() => qc.invalidateQueries({ queryKey: ["jobs", "cloud"] }))
              .catch((err) => console.error("Failed to fail stale job due to local processor losing it", sanitizeForLog(err)));
          }
        }
      }
    });
  }, [localJobs.data, jobs.data, health.data, localJobs.isSuccess, jobs.isSuccess, health.isSuccess, qc]);

  const invalidateProcessor = () => {
    void qc.invalidateQueries({ queryKey: ["proc"] });
  };

  const refreshAll = () => {
    invalidateProcessor();
    void qc.invalidateQueries({ queryKey: ["ingestion"] });
    void qc.invalidateQueries({ queryKey: ["jobs"] });
  };

  const start = useMutation({
    mutationFn: () => processor.start(toStartConfig(config)),
    onSuccess: invalidateProcessor,
  });

  const stop = useMutation({
    mutationFn: () => processor.stop(port),
    onSuccess: invalidateProcessor,
  });

  const cleanup = useMutation({
    mutationFn: () => processor.cleanupJobs(port),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["proc", "jobs"] }),
  });

  const upload = useMutation({
    mutationFn: async (sourcePath: string) => {
      try {
        await validateLocalArtifactUpload(config, port);
      } catch (error) {
        const detail = error instanceof Error ? error.message : String(error);
        throw new Error(
          `Local processor preflight failed before queueing ${isPdfPath(sourcePath) ? "the PDF manual" : "the CSV specification"}: ${detail}`,
        );
      }

      const documentType = getDocumentType(sourcePath);
      const uploadId = newId();
      const processorRunId = newId();
      setProgress(10);

      try {
        const startRes = await api.post<IngestionJobStatus>("/api/ingestion/jobs", {
          uploadId,
          documentType,
          processorRunId,
          configuration: getStartConfiguration(documentType),
        });
        setProgress(45);

        const jobStatus = startRes.data.status?.toLowerCase() ?? "";
        if (STATUS_ERR.includes(jobStatus)) {
          const lines = [
            startRes.data.failureReason ?? "Processing failed to start.",
            `Job ID: ${startRes.data.jobId}`,
            startRes.data.docIngestionRunId
              ? `Pipeline run ID: ${startRes.data.docIngestionRunId}`
              : null,
            JSON.stringify(startRes.data, null, 2),
          ].filter(Boolean);
          throw new Error(lines.join("\n\n"));
        }

        try {
          if (!isPathSafe(sourcePath)) {
            throw new Error(
              `Invalid file path detected: "${sourcePath}". Path traversal or system files are not allowed.`,
            );
          }

          await queueLocalIngestionWorkItem({
            sourcePath,
            jobId: startRes.data.jobId,
            uploadId,
            processorRunId,
            documentType,
            createdAtUtc: new Date().toISOString(),
            sourceFileName: fileNameFromPath(sourcePath),
          });
        } catch (queueError) {
          try {
            await api.delete(`/api/ingestion/jobs/${startRes.data.jobId}`);
          } catch (deleteError) {
            console.error("Failed to clean up job after queue failure:", sanitizeForLog(deleteError));
            // The original queue failure is the actionable error for the operator.
          }
          throw new Error(
            formatAdminError(queueError, {
              action: queueIngestionAction(sourcePath),
              kind: "local-queue",
            }),
          );
        }
        setSubmittedJobId(startRes.data.jobId);
        setProgress(100);

        return startRes.data;
      } catch (error) {
        throw new Error(
          formatAdminError(error, {
            action: queueIngestionAction(sourcePath),
            kind: "cloud-api",
            apiBaseUrl: config.apiBaseUrl,
          }),
        );
      }
    },
    onSuccess: (job) => {
      setSelectedSourcePath(null);
      setSelectionError(null);
      setProgress(0);
      qc.setQueryData<IngestionJobStatus[]>(["jobs", "cloud"], (old) =>
        old ? [job, ...old.filter((existing) => existing.jobId !== job.jobId)] : [job],
      );
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        old ? [job, ...old.filter((existing) => existing.jobId !== job.jobId)] : [job],
      );
      void qc.invalidateQueries({ queryKey: ["jobs", "cloud"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
    onError: () => setProgress(0),
  });

  const retry = useMutation({
    mutationFn: (id: string) => api.post<IngestionJobStatus>(`/api/ingestion/jobs/${id}/retry`),
    onMutate: async (id) => {
      await Promise.all([qc.cancelQueries({ queryKey: ["jobs"] }), qc.cancelQueries({ queryKey: ["ingestion", "upload-jobs"] })]);
      qc.setQueriesData<IngestionJobStatus[]>({ queryKey: ["jobs"] }, (old) =>
        updateIngestionJobInList(old, id, (job) => markIngestionJobRetrying(job)),
      );
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        updateIngestionJobInList(old, id, (job) => markIngestionJobRetrying(job)),
      );
    },
    onSuccess: (res, id) => {
      const retriedJob = res.data;
      if (retriedJob.jobId !== id) {
        setSupersededRetryJobIds((prev) => new Set(prev).add(id));
      }
      qc.setQueriesData<IngestionJobStatus[]>({ queryKey: ["jobs"] }, (old) =>
        replaceRetriedIngestionJob(old, id, retriedJob),
      );
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        replaceRetriedIngestionJob(old, id, retriedJob),
      );
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ["jobs"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
  });

  // Derive isRunning early so it's available to stopLocalProcessorRun
  const isRunning = !!listening.data;

  async function stopLocalProcessorRun(job: IngestionJobStatus, ignoreErrors = false) {
    if (!isActiveJobStatus(job.status) || !job.docIngestionRunId || !isRunning) {
      return;
    }

    try {
      await processor.stopJob(job.docIngestionRunId, port);
    } catch (error) {
      if (isMissingProcessorJobError(error)) {
        console.warn(
          "Per-job local processor stop skipped because the run is no longer present:",
          job.docIngestionRunId,
        );
        return;
      }

      if (!ignoreErrors) {
        throw new Error(
          formatAdminError(error, {
            action: `Stopping local processor run ${job.docIngestionRunId}`,
            kind: "local-processor",
            localProcessorPort: port,
            location: "endpoint",
          }),
        );
      }
      console.warn("Per-job local processor stop failed before delete:", sanitizeForLog(error));
    }
  }

  async function waitForDeleteReady(jobId: string) {
    const deadline = Date.now() + 10_000;
    while (Date.now() < deadline) {
      const status = (await api.get<IngestionJobStatus>(`/api/ingestion/jobs/${jobId}`)).data;
      if (!isActiveJobStatus(status.status)) {
        return;
      }

      await sleep(250);
    }

    throw new Error(`Timed out waiting for ingestion job ${jobId} to stop before deletion.`);
  }

  async function prepareJobForDeletion(job: IngestionJobStatus) {
    if (!isActiveJobStatus(job.status)) {
      return;
    }

    await stopLocalProcessorRun(job);
    await api.post(`/api/ingestion/jobs/${job.jobId}/cancel`);
    await waitForDeleteReady(job.jobId);
  }

  const stopJob = useMutation({
    mutationFn: async (job: IngestionJobStatus) => {
      await stopLocalProcessorRun(job);
      await api.post(`/api/ingestion/jobs/${job.jobId}/cancel`);
    },
    onSuccess: () => {
      setDeleteError(null);
      void qc.invalidateQueries({ queryKey: ["proc", "jobs"] });
      void qc.invalidateQueries({ queryKey: ["proc", "health"] });
      void qc.invalidateQueries({ queryKey: ["jobs"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
    onError: (err: unknown) => {
      setDeleteError(
        formatAdminError(err, {
          action: "Stopping ingestion job",
          kind: "cloud-api",
          apiBaseUrl: config.apiBaseUrl,
        }),
      );
    },
  });

  const remove = useMutation({
    mutationFn: async (job: IngestionJobStatus) => {
      await prepareJobForDeletion(job);
      await api.delete(`/api/ingestion/jobs/${job.jobId}`);
      return job.jobId;
    },
    onSuccess: (jobId) => {
      setDeleteError(null);
      qc.setQueriesData<IngestionJobStatus[]>({ queryKey: ["jobs"] }, (old) =>
        old?.filter((job) => job.jobId !== jobId),
      );
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        old?.filter((job) => job.jobId !== jobId),
      );
      void qc.invalidateQueries({ queryKey: ["proc", "jobs"] });
      void qc.invalidateQueries({ queryKey: ["proc", "health"] });
      void qc.invalidateQueries({ queryKey: ["jobs"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
    onError: (err: unknown) => {
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        setDeleteError(err.response.data?.detail ?? "Delete is already in progress for this job.");
        return;
      }
      setDeleteError(
        formatAdminError(err, {
          action: "Deleting ingestion job",
          kind: "cloud-api",
          apiBaseUrl: config.apiBaseUrl,
        }),
      );
    },
  });

  useEffect(() => {
    if (!submittedJob.data) return;

    qc.setQueryData<IngestionJobStatus[]>(["jobs", "cloud"], (old) =>
      updateIngestionJobInList(old, submittedJob.data.jobId, () => submittedJob.data),
    );
    qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
      updateIngestionJobInList(old, submittedJob.data.jobId, () => submittedJob.data),
    );
  }, [qc, submittedJob.data]);

  const healthy = (health.data?.status ?? "").toLowerCase() === "healthy";
  const localJobList = localJobs.data ?? [];
  const jobList = filterSupersededIngestionJobs(jobs.data, supersededRetryJobIds);
  const active = jobList.filter((j) => STATUS_ACTIVE.includes(j.status.toLowerCase())).length;
  const allowedExtensions = constraints.data?.supportedExtensions?.map(normalizeExtension);
  const extList = allowedExtensions?.join(", ") ?? "";
  const maxLabel = constraints.data?.maxFileSizeBytes ? fmt(constraints.data.maxFileSizeBytes) : null;
  const apiConfigured = health.data?.api_client_configured === true;
  const selectedSourceName = selectedSourcePath ? fileNameFromPath(selectedSourcePath) : null;

  async function selectLocalSourceFile() {
    setSelectionError(null);
    upload.reset();

    try {
      const selectedPath = await pickLocalIngestionFile();
      if (!selectedPath) {
        return;
      }

      setSelectedSourcePath(selectedPath);
      setProgress(0);
    } catch (error) {
      setSelectionError(error instanceof Error ? error.message : String(error));
    }
  }

  function clearSelectedSourceFile() {
    setSelectedSourcePath(null);
    setSelectionError(null);
    upload.reset();
    setProgress(0);
  }

  function pickDroppedFile(f: File) {
    const max = constraints.data?.maxFileSizeBytes;
    if (max && f.size > max) {
      setSelectionError(`File exceeds the ${fmt(max)} limit.`);
      return;
    }

    // Validate file extension matches allowed extensions
    if (allowedExtensions && allowedExtensions.length > 0) {
      const fileName = f.name.toLowerCase();
      const hasValidExtension = allowedExtensions.some((ext) => fileName.endsWith(ext.toLowerCase()));
      if (!hasValidExtension) {
        setSelectionError(`File type not supported. Allowed types: ${allowedExtensions.join(", ")}`);
        return;
      }
    }

    try {
      const sourcePath = getTauriFilePath(f as File);
      setSelectedSourcePath(sourcePath);
      setSelectionError(null);
      upload.reset();
      setProgress(0);
    } catch (error) {
      setSelectionError(error instanceof Error ? error.message : String(error));
    }
  }

  return (
    <div>
      <PageHeader
        title="Processor"
        subtitle={<span className="font-mono">127.0.0.1:{port}</span>}
        actions={
          <>
            <StatusPill ok={isRunning} label={isRunning ? "Running" : "Stopped"} />
            <Button onClick={refreshAll}>
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
        <div className="mb-4 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          <span className="flex-1">
            Could not start the processor: {formatAdminError(start.error, {
              action: "Starting the local processor",
              kind: "local-processor",
              localProcessorPort: port,
              location: "integration",
            })}
          </span>
          <button
            className="shrink-0 rounded p-0.5 text-danger hover:bg-danger/20"
            onClick={() => start.reset()}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}

      {stop.isError && (
        <div className="mb-4 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          <span className="flex-1">
            Could not stop the processor: {formatAdminError(stop.error, {
              action: "Stopping the local processor",
              kind: "local-processor",
              localProcessorPort: port,
              location: "integration",
            })}
          </span>
          <button
            className="shrink-0 rounded p-0.5 text-danger hover:bg-danger/20"
            onClick={() => stop.reset()}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}

      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
        <MetricCard
          label="Health"
          tone={healthy ? "success" : "default"}
          value={isRunning ? (healthy ? "Healthy" : (health.data?.status ?? "—")) : "Offline"}
        />
        <MetricCard label="Local jobs" value={health.data?.active_jobs ?? localJobList.length} />
        <MetricCard label="Cloud jobs" value={jobList.length} />
        <MetricCard label="API client" value={apiConfigured ? "Configured" : "Missing"} />
      </div>

      <Card className="mb-4">
        <div className="mb-3 flex items-center justify-between">
          <div>
            <div className="text-sm font-medium">Queue ingestion</div>
            <div className="text-xs text-muted">Upload documents for local chunking and embedding</div>
          </div>
          <span className="text-xs text-muted">Set constraints come from the cloud API</span>
        </div>
        {selectionError && (
          <div className="mb-4 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
            <span className="flex-1">{selectionError}</span>
            <button
              className="shrink-0 rounded p-0.5 text-danger hover:bg-danger/20"
              onClick={() => setSelectionError(null)}
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        )}
        <div
          className={cn(
            "flex min-h-[140px] cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed transition-colors",
            dragOver ? "border-primary bg-primary/5" : "border-border hover:border-primary/50",
          )}
          onClick={() => void selectLocalSourceFile()}
          onDragOver={(e) => {
            e.preventDefault();
            setDragOver(true);
          }}
          onDragLeave={() => setDragOver(false)}
          onDrop={(e) => {
            e.preventDefault();
            setDragOver(false);
            const f = e.dataTransfer.files[0];
            if (f) pickDroppedFile(f);
          }}
        >
          <Upload className="h-6 w-6 text-muted" />
          {selectedSourcePath ? (
            <div className="flex w-full max-w-full items-center gap-2 text-sm">
              <FileText className="h-4 w-4" />
              <div className="min-w-0 flex-1">
                <div className="truncate font-medium" title={selectedSourcePath}>
                  {selectedSourceName}
                </div>
                <div className="truncate text-xs text-muted" title={selectedSourcePath}>
                  {selectedSourcePath}
                </div>
              </div>
              <button
                className="shrink-0 rounded p-0.5 text-muted hover:text-danger"
                onClick={(e) => {
                  e.stopPropagation();
                  clearSelectedSourceFile();
                }}
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          ) : (
            <span className="text-sm text-muted">
              Click to browse or drag a file here
              {extList && <> &middot; {extList}</>}
              {maxLabel && <> &middot; max {maxLabel}</>}
            </span>
          )}
        </div>
        <div className="mt-4 flex items-center gap-4">
          <Button
            variant="primary"
            disabled={!selectedSourcePath || upload.isPending}
            onClick={() => selectedSourcePath && upload.mutate(selectedSourcePath)}
          >
            {upload.isPending ? `Queueing... ${progress}%` : "Queue for local processing"}
          </Button>
        </div>

        {upload.isError && (
          <div className="mt-4 flex items-start gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger whitespace-pre-wrap break-words">
            <span className="flex-1">{upload.error.message}</span>
            <button
              className="shrink-0 rounded p-0.5 text-danger hover:bg-danger/20"
              onClick={() => upload.reset()}
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        )}

        {upload.isPending && (
          <div className="mt-4 h-1.5 overflow-hidden rounded-full bg-secondary">
            <div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} />
          </div>
        )}
      </Card>

      <div className="mb-4 overflow-hidden rounded-xl border border-border">
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <span className="text-sm font-medium">Local processor jobs</span>
          <Button onClick={() => cleanup.mutate()} disabled={!isRunning || cleanup.isPending}>
            <Trash2 className="h-4 w-4" /> Clean up finished
          </Button>
        </div>
        {!isRunning ? (
          <Empty>Start the processor to see jobs.</Empty>
        ) : localJobs.isLoading ? (
          <Empty>Loading local jobs...</Empty>
        ) : localJobs.isError ? (
          <Empty>
            Could not load local jobs: {formatAdminError(localJobs.error, {
              action: "Loading local processor jobs",
              kind: "local-processor",
              localProcessorPort: port,
              location: "endpoint",
            })}
          </Empty>
        ) : localJobList.length === 0 ? (
          <Empty>No local jobs yet.</Empty>
        ) : (
          localJobList.map((job) => {
            const pct = isDone(job.status) ? 100 : Math.round((job.progress ?? 0) * 100);
            const chunks = localChunksLabel(job);
            return (
              <div key={job.job_id} className="flex items-center gap-3 border-b border-border px-4 py-3 last:border-b-0">
                {jobIcon(job)}
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <div className="truncate text-sm font-mono">{job.job_id.slice(0, 8)}</div>
                    <div className="text-xs text-muted">{stageLabel(job)}</div>
                    {chunks && <div className="text-xs text-muted">· {chunks}</div>}
                  </div>
                  <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-secondary">
                    <div
                      className={isDone(job.status) ? "h-full bg-success" : "h-full bg-primary"}
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                </div>
                <StatusPill ok={isDone(job.status)} label={isDone(job.status) ? "Done" : `${pct}%`} />
              </div>
            );
          })
        )}
      </div>

      <Card>
        <div className="mb-3 flex items-center justify-between gap-3">
          <div>
            <span className="text-sm font-medium">Cloud ingestion jobs</span>
            <div className="text-xs text-muted">Ingestion job records from the cloud API</div>
          </div>
        </div>

        <div className="mb-4 flex gap-3">
          <div className="rounded-lg bg-secondary/60 px-4 py-2.5">
            <div className="text-xs text-muted">Total</div>
            <div className="text-lg font-medium">{jobList.length}</div>
          </div>
          <div className="rounded-lg bg-secondary/60 px-4 py-2.5">
            <div className="text-xs text-muted">Active</div>
            <div className={cn("text-lg font-medium", active > 0 ? "text-primary" : "")}>{active}</div>
          </div>
        </div>

        {deleteError && (
          <div className="mb-4 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
            <span className="flex-1">{deleteError}</span>
            <button
              className="shrink-0 rounded p-0.5 text-danger hover:bg-danger/20"
              onClick={() => setDeleteError(null)}
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        )}

        {jobs.isLoading ? (
          <Empty>Loading jobs…</Empty>
        ) : jobs.isError ? (
          <Empty>
            Could not load jobs: {formatAdminError(jobs.error, {
              action: "Loading ingestion jobs",
              kind: "cloud-api",
              apiBaseUrl: config.apiBaseUrl,
            })}
          </Empty>
        ) : jobList.length === 0 ? (
          <Empty>No jobs found.</Empty>
        ) : (
          <div className="overflow-hidden rounded-xl border border-border">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-left text-xs text-muted">
                  <th className="px-4 py-2.5 font-medium">Job</th>
                  <th className="px-4 py-2.5 font-medium">Status</th>
                  <th className="px-4 py-2.5 font-medium">Created</th>
                  <th className="px-4 py-2.5 font-medium" />
                </tr>
              </thead>
              <tbody>
                {jobList.map((j) => (
                  <Fragment key={j.jobId}>
                    <tr className="border-b border-border hover:bg-secondary/30">
                      <td className="max-w-[480px] px-4 py-3 align-top">
                        <div className="truncate">{formatIngestionJobLabel(j)}</div>
                        {j.sourceFileName && (
                          <div className="truncate text-xs text-muted" title={j.sourceFileName}>
                            {j.sourceFileName}
                          </div>
                        )}
                        {formatChunkProgress(j) && (
                          <div className="mt-1 text-xs text-muted">{formatChunkProgress(j)}</div>
                        )}
                        {isIngestionFailed(j.status) && <IngestionJobFailurePanel job={j} />}
                      </td>
                      <td className="px-4 py-3 align-top">
                        <span
                          className={cn(
                            "rounded-full px-2.5 py-0.5 text-xs font-medium capitalize",
                            jobTone(j.status) === "success"
                              ? "bg-success/15 text-success"
                              : jobTone(j.status) === "danger"
                                ? "bg-danger/15 text-danger"
                                : "bg-secondary text-muted",
                          )}
                        >
                          {isActiveJobStatus(j.status) && j.currentStage ? j.currentStage.replace(/-/g, " ") : j.status}
                        </span>
                      </td>
                      <td
                        className="px-4 py-3 align-top text-xs text-muted"
                        title={formatLocalDateTime(j.createdAtUtc)}
                      >
                        {relativeTime(j.createdAtUtc)}
                      </td>
                      <td className="px-4 py-3 align-top">
                        <div className="flex justify-end gap-1">
                          {isIngestionFailed(j.status) && (
                            <button
                              title="Retry"
                              disabled={retry.isPending}
                              onClick={() => retry.mutate(j.jobId)}
                              className="rounded p-1 text-muted hover:text-primary disabled:opacity-50"
                            >
                              <RotateCcw className="h-4 w-4" />
                            </button>
                          )}
                          {isActiveJobStatus(j.status) && (
                            <button
                              title="Stop"
                              disabled={stopJob.isPending}
                              onClick={() => stopJob.mutate(j)}
                              className="rounded p-1 text-muted hover:text-primary disabled:opacity-50"
                            >
                              <Square className="h-4 w-4" />
                            </button>
                          )}
                          <button
                            title="Delete"
                            disabled={remove.isPending}
                            onClick={() => remove.mutate(j)}
                            className="rounded p-1 text-muted hover:text-danger disabled:opacity-50"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}
