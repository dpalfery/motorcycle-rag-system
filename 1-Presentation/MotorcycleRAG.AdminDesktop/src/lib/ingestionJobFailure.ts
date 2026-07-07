import { type IngestionJobStatus } from "./ingestionJob";

export function jobFailureText(job: Pick<IngestionJobStatus, "failureDetail" | "failureReason">): string | undefined {
  return job.failureDetail ?? job.failureReason;
}
