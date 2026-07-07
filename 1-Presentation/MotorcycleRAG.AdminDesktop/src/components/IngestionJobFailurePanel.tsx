import type { IngestionJobStatus } from "@/lib/ingestionJob";
import { jobFailureText } from "@/lib/ingestionJobFailure";

export default function IngestionJobFailurePanel({ job }: { job: IngestionJobStatus }) {
  const detail = jobFailureText(job);
  if (!detail) {
    return null;
  }

  return (
    <pre className="mt-2 max-h-80 overflow-auto rounded-md border border-danger/30 bg-danger/10 p-3 text-xs leading-relaxed text-danger whitespace-pre-wrap break-words">
      {detail}
    </pre>
  );
}
