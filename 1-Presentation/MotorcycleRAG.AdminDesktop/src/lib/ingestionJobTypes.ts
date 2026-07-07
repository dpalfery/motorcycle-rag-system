export interface IngestionCoverageMetrics {
  viewablePageCoveragePercent?: number;
  searchableTextCoveragePercent?: number;
  ocrCoveragePercent?: number;
  nativeTextCoveragePercent?: number;
}

export interface IngestionWorkloadLimits {
  maxPages?: number;
  maxInputBytes?: number;
  maxRuntimeMinutes?: number;
}
