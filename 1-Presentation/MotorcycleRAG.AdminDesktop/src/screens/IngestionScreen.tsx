import { useEffect, useRef, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Upload, FileText, X } from "lucide-react";
import axios from "axios";
import { api } from "@/lib/apiClient";
import { Button, PageHeader, StatusPill, Empty } from "@/components/ui";
import IngestionJobFailurePanel from "@/components/IngestionJobFailurePanel";
import { cn, formatLocalDateTime } from "@/lib/utils";
import {
  getTauriFilePath,
  queueLocalIngestionWorkItem,
} from "@/lib/localIngestion";
import {
  formatIngestionJobLabel,
  isIngestionFailed,
  type IngestionJobStatus,
  updateIngestionJobInList,
} from "@/lib/ingestionJob";
interface UploadConstraints {
  maxFileSizeBytes: number;
  supportedExtensions: string[];
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  traceId?: string;
  referenceId?: string;
  extensions?: {
    traceId?: string;
    referenceId?: string;
  };
}

function fmt(bytes: number) {
  if (bytes >= 1_073_741_824) return `${(bytes / 1_073_741_824).toFixed(1)} GB`;
  if (bytes >= 1_048_576) return `${(bytes / 1_048_576).toFixed(0)} MB`;
  return `${(bytes / 1024).toFixed(0)} KB`;
}

const STATUS_OK = ["completed", "complete", "done", "succeeded"];
const STATUS_ERR = ["failed", "error", "cancelled"];
const STATUS_ACTIVE = ["queued", "pending", "processing", "running", "inprogress"];

function jobTone(s: string): "success" | "danger" | "default" {
  if (STATUS_OK.includes(s.toLowerCase())) return "success";
  if (STATUS_ERR.includes(s.toLowerCase())) return "danger";
  return "default";
}

function isActiveJobStatus(status?: string) {
  return status ? STATUS_ACTIVE.includes(status.toLowerCase()) : false;
}

function isPdf(f: File) {
  return f.name.toLowerCase().endsWith(".pdf");
}

function getDocumentType(f: File) {
  const name = f.name.toLowerCase();
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

function formatUploadError(error: unknown) {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    const problem = error.response?.data;
    const message = problem?.detail ?? problem?.title ?? error.message;
    const traceId = problem?.traceId ?? problem?.extensions?.traceId;
    const referenceId =
      problem?.referenceId ?? problem?.extensions?.referenceId;
    const suffixParts = [
      traceId ? `trace: ${traceId}` : null,
      referenceId ? `reference: ${referenceId}` : null,
    ].filter(Boolean);

    return suffixParts.length > 0
      ? `${message} (${suffixParts.join(", ")})`
      : message;
  }

  return error instanceof Error ? error.message : String(error);
}

function uploadStepError(step: string, error: unknown) {
  return new Error(`${step}: ${formatUploadError(error)}`);
}

function newId() {
  return crypto.randomUUID();
}

function formatChunkProgress(job: IngestionJobStatus) {
  if (job.indexedChunkCount === undefined && job.expectedChunkCount === undefined) {
    return null;
  }

  return `${job.indexedChunkCount ?? 0}/${job.expectedChunkCount ?? "?"} chunks`;
}

export default function IngestionScreen() {
  const qc = useQueryClient();
  const fileRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState(0);
  const [submittedJobId, setSubmittedJobId] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState(false);

  const constraints = useQuery({
    queryKey: ["ingestion", "constraints"],
    queryFn: async () => {
      const res = await api.get<UploadConstraints>(
        "/api/ingestion/jobs/upload-constraints"
      );
      return res.data;
    },
    retry: 1,
  });

  const recentJobs = useQuery({
    queryKey: ["ingestion", "upload-jobs"],
    queryFn: async () => {
      const res = await api.get<
        { items?: IngestionJobStatus[] } | IngestionJobStatus[]
      >(
        "/api/ingestion/jobs?top=50"
      );
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
    refetchInterval: 15_000,
  });

  const submittedJob = useQuery({
    queryKey: ["ingestion", "job", submittedJobId],
    enabled: !!submittedJobId,
    queryFn: async () => {
      const res = await api.get<IngestionJobStatus>(
        `/api/ingestion/jobs/${submittedJobId}`,
      );
      return res.data;
    },
    refetchInterval: (query) =>
      isActiveJobStatus(query.state.data?.status) ? 2_000 : false,
  });

  const upload = useMutation({
    mutationFn: async (f: File) => {
      const documentType = getDocumentType(f);
      const uploadId = newId();
      const processorRunId = newId();
      const sourcePath = getTauriFilePath(f);
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

        await queueLocalIngestionWorkItem({
          sourcePath,
          jobId: startRes.data.jobId,
          uploadId,
          processorRunId,
          documentType,
          sourceFileName: f.name,
          size: f.size,
          createdAtUtc: new Date().toISOString(),
        });
        setSubmittedJobId(startRes.data.jobId);
        setProgress(100);

        return startRes.data;
      } catch (error) {
        throw uploadStepError(
          `Creating local ${isPdf(f) ? "PDF manual" : "CSV specification"} ingestion work item failed`,
          error
        );
      }
    },
    onSuccess: (job) => {
      setFile(null);
      setProgress(0);
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        old ? [job, ...old.filter((existing) => existing.jobId !== job.jobId)] : [job],
      );
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
    onError: () => setProgress(0),
  });

  useEffect(() => {
    if (!submittedJob.data) return;

    qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
      updateIngestionJobInList(old, submittedJob.data.jobId, () => submittedJob.data),
    );
  }, [qc, submittedJob.data]);

  function pickFile(f: File) {
    const max = constraints.data?.maxFileSizeBytes;
    if (max && f.size > max) {
      alert(`File exceeds the ${fmt(max)} limit.`);
      return;
    }
    setFile(f);
    setProgress(0);
  }

  const allowedExtensions = constraints.data?.supportedExtensions?.map(normalizeExtension);
  const extList = allowedExtensions?.join(", ") ?? "";
  const maxLabel = constraints.data?.maxFileSizeBytes
    ? fmt(constraints.data.maxFileSizeBytes)
    : null;
  const displayedRecentJobs = recentJobs.data ?? [];

  return (
    <div>
      <PageHeader
        title="Ingestion"
        subtitle="Upload documents for chunking and embedding"
        actions={
          <Button
            onClick={() => void qc.invalidateQueries({ queryKey: ["ingestion"] })}
          >
            Refresh
          </Button>
        }
      />

      {/* Drop zone */}
      <div
        className={cn(
          "mb-4 flex min-h-[140px] cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed transition-colors",
          dragOver ? "border-primary bg-primary/5" : "border-border hover:border-primary/50"
        )}
        onClick={() => fileRef.current?.click()}
        onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragOver(false);
          const f = e.dataTransfer.files[0];
          if (f) pickFile(f);
        }}
      >
        <Upload className="h-6 w-6 text-muted" />
        {file ? (
          <div className="flex items-center gap-2 text-sm">
            <FileText className="h-4 w-4" />
            <span>{file.name}</span>
            <span className="text-muted">({fmt(file.size)})</span>
            <button
              className="text-muted hover:text-danger"
              onClick={(e) => { e.stopPropagation(); setFile(null); }}
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
        <input
          ref={fileRef}
          type="file"
          className="hidden"
          accept={allowedExtensions?.join(",")}
          onChange={(e) => { const f = e.target.files?.[0]; if (f) pickFile(f); }}
        />
      </div>

      {/* Upload */}
      <div className="mb-6 flex items-center gap-4">
        <Button
          variant="primary"
          disabled={!file || upload.isPending}
          onClick={() => file && upload.mutate(file)}
        >
          {upload.isPending ? `Queueing... ${progress}%` : "Queue for local processing"}
        </Button>
      </div>

      {upload.isError && (
        <div className="mb-4 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger whitespace-pre-wrap break-words">
          {formatUploadError(upload.error)}
        </div>
      )}

      {/* Upload progress bar */}
      {upload.isPending && (
        <div className="mb-4 h-1.5 overflow-hidden rounded-full bg-secondary">
          <div
            className="h-full bg-primary transition-all"
            style={{ width: `${progress}%` }}
          />
        </div>
      )}

      {/* Recent upload jobs */}
      <div className="overflow-hidden rounded-xl border border-border">
        <div className="border-b border-border px-4 py-3 text-sm font-medium">
          Recent uploads
        </div>
        {recentJobs.isLoading ? (
          <Empty>Loading…</Empty>
        ) : recentJobs.isError ? (
          <Empty>Could not load upload history.</Empty>
        ) : displayedRecentJobs.length === 0 ? (
          <Empty>No uploads yet.</Empty>
        ) : (
          displayedRecentJobs.map((j) => (
            <div
              key={j.jobId}
              className="border-b border-border px-4 py-3 last:border-b-0"
            >
              <div className="flex items-center justify-between gap-3">
                <div className="flex min-w-0 flex-1 items-start gap-3">
                  <FileText className="mt-0.5 h-4 w-4 shrink-0 text-muted" />
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-sm">
                      {formatIngestionJobLabel(j)}
                    </div>
                    <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
                      <span>Status: {j.status}</span>
                      {j.currentStage && <span>Stage: {j.currentStage}</span>}
                      {j.stageSetAtUtc && (
                        <span>Stage set: {formatLocalDateTime(j.stageSetAtUtc)}</span>
                      )}
                      {formatChunkProgress(j) && <span>{formatChunkProgress(j)}</span>}
                    </div>
                    {isIngestionFailed(j.status) && <IngestionJobFailurePanel job={j} />}
                  </div>
                </div>
                <div className="flex shrink-0 items-center gap-3">
                  <span className="text-xs text-muted">
                    {formatLocalDateTime(j.createdAtUtc)}
                  </span>
                  <StatusPill ok={jobTone(j.status) === "success"} label={j.status} />
                </div>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
