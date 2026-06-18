import { useRef, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Upload, FileText, X } from "lucide-react";
import { api, uploadApi } from "@/lib/apiClient";
import { Button, PageHeader, StatusPill, Empty } from "@/components/ui";
import { cn } from "@/lib/utils";

interface UploadConstraints {
  maxFileSizeBytes: number;
  allowedExtensions: string[];
}

interface UploadJob {
  jobId: string;
  fileName: string;
  status: string;
  createdAt: string;
  error?: string;
}

function fmt(bytes: number) {
  if (bytes >= 1_073_741_824) return `${(bytes / 1_073_741_824).toFixed(1)} GB`;
  if (bytes >= 1_048_576) return `${(bytes / 1_048_576).toFixed(0)} MB`;
  return `${(bytes / 1024).toFixed(0)} KB`;
}

const STATUS_OK = ["completed", "complete", "done", "succeeded"];
const STATUS_ERR = ["failed", "error", "cancelled"];

function jobTone(s: string): "success" | "danger" | "default" {
  if (STATUS_OK.includes(s.toLowerCase())) return "success";
  if (STATUS_ERR.includes(s.toLowerCase())) return "danger";
  return "default";
}

export default function IngestionScreen() {
  const qc = useQueryClient();
  const fileRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [withProcessing, setWithProcessing] = useState(true);
  const [progress, setProgress] = useState(0);
  const [dragOver, setDragOver] = useState(false);

  const constraints = useQuery({
    queryKey: ["ingestion", "constraints"],
    queryFn: async () => {
      const res = await api.get<UploadConstraints>("/api/datapipeline/upload-constraints");
      return res.data;
    },
    retry: 1,
  });

  const recentJobs = useQuery({
    queryKey: ["ingestion", "upload-jobs"],
    queryFn: async () => {
      const res = await api.get<{ items?: UploadJob[] } | UploadJob[]>(
        "/api/ingestion/jobs/upload"
      );
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
    refetchInterval: 15_000,
  });

  const upload = useMutation({
    mutationFn: async (f: File) => {
      const form = new FormData();
      form.append("file", f);
      const endpoint = withProcessing
        ? "/api/file-upload/with-processing"
        : "/api/file-upload";
      await uploadApi.post(endpoint, form, {
        onUploadProgress: (e) =>
          setProgress(Math.round((e.loaded / (e.total ?? 1)) * 100)),
      });
    },
    onSuccess: () => {
      setFile(null);
      setProgress(0);
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
    onError: () => setProgress(0),
  });

  function pickFile(f: File) {
    const max = constraints.data?.maxFileSizeBytes;
    if (max && f.size > max) {
      alert(`File exceeds the ${fmt(max)} limit.`);
      return;
    }
    setFile(f);
    setProgress(0);
  }

  const extList = constraints.data?.allowedExtensions?.join(", ") ?? "";
  const maxLabel = constraints.data?.maxFileSizeBytes
    ? fmt(constraints.data.maxFileSizeBytes)
    : null;

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
          accept={constraints.data?.allowedExtensions?.map((e) => `.${e}`).join(",")}
          onChange={(e) => { const f = e.target.files?.[0]; if (f) pickFile(f); }}
        />
      </div>

      {/* Options + upload */}
      <div className="mb-6 flex items-center gap-4">
        <label className="flex cursor-pointer items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={withProcessing}
            onChange={(e) => setWithProcessing(e.target.checked)}
            className="accent-primary"
          />
          Process immediately after upload
        </label>
        <Button
          variant="primary"
          disabled={!file || upload.isPending}
          onClick={() => file && upload.mutate(file)}
        >
          {upload.isPending ? `Uploading… ${progress}%` : "Upload"}
        </Button>
      </div>

      {upload.isError && (
        <div className="mb-4 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          {String((upload.error as Error).message)}
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
        ) : (recentJobs.data ?? []).length === 0 ? (
          <Empty>No uploads yet.</Empty>
        ) : (
          (recentJobs.data ?? []).map((j) => (
            <div
              key={j.jobId}
              className="flex items-center justify-between border-b border-border px-4 py-3 last:border-b-0"
            >
              <div className="flex min-w-0 items-center gap-3">
                <FileText className="h-4 w-4 shrink-0 text-muted" />
                <div className="min-w-0">
                  <div className="truncate text-sm">{j.fileName}</div>
                  {j.error && (
                    <div className="truncate text-xs text-danger">{j.error}</div>
                  )}
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-3">
                <span className="text-xs text-muted">
                  {new Date(j.createdAt).toLocaleString()}
                </span>
                <StatusPill ok={jobTone(j.status) === "success"} label={j.status} />
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
