import type { IngestionJobStatus } from "@/lib/ingestionJob";
import { jobFailureText } from "@/lib/ingestionJobFailure";

interface IngestionJobFailurePanelProps {
  job: IngestionJobStatus;
  /** When provided and the failure indicates manual entry is required, a clickable
   * "Enter Metadata" button is rendered that invokes this callback. */
  onEnterMetadata?: () => void;
}

export default function IngestionJobFailurePanel({
  job,
  onEnterMetadata,
}: IngestionJobFailurePanelProps) {
  const detail = jobFailureText(job);
  if (!detail) {
    return null;
  }

  const showManualEntry = !!onEnterMetadata && job.requiresManualMetadata === true;

  return (
    <div className="mt-2 max-h-80 overflow-auto rounded-md border border-danger/30 bg-danger/10 p-3 text-xs leading-relaxed text-danger">
      <pre className="whitespace-pre-wrap break-words">{detail}</pre>
      {showManualEntry && (
        <button
          type="button"
          onClick={onEnterMetadata}
          className="mt-2 rounded border border-warning/50 bg-warning/15 px-2.5 py-1 font-medium text-warning hover:bg-warning/25"
        >
          Enter Metadata →
        </button>
      )}
    </div>
  );
}
