import { useEffect, useState } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui";
import { validateMetadataJson } from "@/lib/metadataApi";

/**
 * Modal shown when a PDF ingestion job pauses awaiting manual metadata entry. This is a
 * presentational component: it owns only the textarea state and client-side validation. The
 * parent supplies the pre-fill text (from GET /metadata), performs the actual submit, and
 * closes the modal on success. Submit errors surface inline via the rejected `onSubmit`
 * promise so the parent can keep the modal open and show a formatted message.
 */

const SCHEMA_HINT = `{
  "make": "Honda",
  "model": "CBR600RR",
  "year": 2023,
  "category": "sport",
  "tags": ["sport", "inline-4", "600cc"]
}`;

export interface ManualMetadataModalProps {
  isOpen: boolean;
  jobId: string;
  /** Pre-fill text, typically the result of GET /metadata (pretty-printed JSON). */
  initialMetadata?: string;
  /** Submit the validated JSON string. Reject to keep the modal open and show an error. */
  onSubmit: (metadataJson: string) => Promise<void>;
  onClose: () => void;
}

export default function ManualMetadataModal({
  isOpen,
  jobId,
  initialMetadata,
  onSubmit,
  onClose,
}: ManualMetadataModalProps) {
  const [text, setText] = useState(initialMetadata ?? "");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Sync the external pre-fill into the textarea whenever it changes (e.g. when GET
  // resolves after the modal opens). User edits in between are preserved until a new value
  // arrives.
  useEffect(() => {
    setText(initialMetadata ?? "");
    setError(null);
  }, [initialMetadata]);

  // Close on Escape (disabled mid-submit to avoid leaving the API in a half-applied state).
  useEffect(() => {
    if (!isOpen) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape" && !submitting) onClose();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [isOpen, submitting, onClose]);

  if (!isOpen) return null;

  async function handleSubmit() {
    setError(null);
    const result = validateMetadataJson(text);
    if (!result.ok) {
      setError(result.error ?? "Invalid metadata.");
      return;
    }

    setSubmitting(true);
    try {
      await onSubmit(text.trim());
      // On success the parent flips isOpen to false and this component unmounts.
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to submit metadata.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="manual-metadata-title"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !submitting) onClose();
      }}
    >
      <div className="w-full max-w-lg rounded-xl border border-border bg-card shadow-xl">
        <div className="flex items-start justify-between gap-3 border-b border-border px-5 py-4">
          <div className="min-w-0">
            <h2 id="manual-metadata-title" className="text-base font-medium text-foreground">
              Manual Metadata Required
            </h2>
            <p className="mt-1 text-xs text-muted">
              Automatic metadata extraction could not determine all required fields. Please
              enter the metadata manually.
            </p>
          </div>
          <button
            type="button"
            aria-label="Close"
            disabled={submitting}
            onClick={onClose}
            className="shrink-0 rounded p-1 text-muted hover:text-foreground disabled:opacity-50"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="px-5 py-4">
          <div className="mb-1 text-xs text-muted">Job</div>
          <div className="mb-3 truncate font-mono text-xs text-foreground">{jobId}</div>

          <label htmlFor="manual-metadata-json" className="mb-1 block text-xs text-muted">
            Metadata JSON
          </label>
          <textarea
            id="manual-metadata-json"
            aria-label="Manual metadata JSON"
            value={text}
            onChange={(e) => setText(e.target.value)}
            spellCheck={false}
            rows={10}
            className="w-full resize-y rounded-md border border-border bg-background px-3 py-2 font-mono text-xs text-foreground focus:border-primary focus:outline-none disabled:opacity-60"
            disabled={submitting}
          />

          {error && (
            <p role="alert" className="mt-2 text-xs text-danger">
              {error}
            </p>
          )}

          <div className="mt-3 rounded-md border border-border bg-secondary/40 p-3">
            <div className="mb-1 text-[11px] uppercase tracking-wide text-muted">
              Expected schema
            </div>
            <pre className="overflow-x-auto whitespace-pre-wrap font-mono text-[11px] text-muted">
              {SCHEMA_HINT}
            </pre>
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t border-border px-5 py-3">
          <Button variant="default" onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Button variant="primary" onClick={handleSubmit} disabled={submitting}>
            {submitting ? "Submitting…" : "Submit"}
          </Button>
        </div>
      </div>
    </div>
  );
}
