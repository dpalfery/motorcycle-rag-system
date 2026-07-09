import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import JobsScreen from "./JobsScreen";
import { useConfig, DEFAULT_CONFIG } from "@/lib/config";
import type { IngestionJobStatus } from "@/lib/ingestionJob";

// `vi.hoisted` runs before imports so the mock factories can reference the stubs.
const { apiGet, apiPost, apiDelete } = vi.hoisted(() => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiDelete: vi.fn(),
}));

vi.mock("@/lib/apiClient", () => ({
  api: { get: apiGet, post: apiPost, delete: apiDelete },
}));

/** Builds a minimal IngestionJobStatus; defaults to an awaiting-metadata PDF job. */
function makeJob(overrides: Partial<IngestionJobStatus> = {}): IngestionJobStatus {
  return {
    jobId: "job-1",
    id: 1,
    status: "AwaitingMetadata",
    createdAtUtc: new Date().toISOString(),
    inputType: "manual-pdf",
    inputRef: "blob://upload.pdf",
    ...overrides,
  };
}

/**
 * Programs the mocked cloud API for a jobs list. The jobs GET returns the array directly
 * (matching the screen's `Array.isArray(res.data)` branch); the metadata GET returns an
 * empty metadata response so the modal's pre-fill query resolves cleanly.
 */
function configureApiJobs(jobs: IngestionJobStatus[]) {
  apiGet.mockImplementation(async (url: string) => {
    if (url === "/api/ingestion/jobs") {
      return { data: jobs };
    }
    if (/\/api\/ingestion\/jobs\/[^/]+\/metadata$/.test(url)) {
      return {
        data: {
          jobId: jobs[0]?.jobId ?? "job-1",
          make: null,
          model: null,
          year: null,
          category: null,
          tags: [],
          fillRate: 0,
          isComplete: false,
        },
      };
    }
    return { data: {} };
  });
  apiPost.mockResolvedValue({ data: {} });
  apiDelete.mockResolvedValue({ data: {} });
}

function renderScreen() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0, staleTime: Infinity, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  });
  // Seed the Zustand config store with deterministic defaults (isolated per test).
  useConfig.setState({ config: { ...DEFAULT_CONFIG }, loaded: true });

  return render(
    <QueryClientProvider client={queryClient}>
      <JobsScreen />
    </QueryClientProvider>,
  );
}

describe("JobsScreen — Enter Metadata button", () => {
  beforeEach(() => {
    apiGet.mockReset();
    apiPost.mockReset();
    apiDelete.mockReset();
  });
  afterEach(() => cleanup());

  it("renders the Enter Metadata button for an AwaitingMetadata job", async () => {
    configureApiJobs([makeJob({ status: "AwaitingMetadata" })]);
    renderScreen();

    expect(await screen.findByRole("button", { name: "Enter Metadata" })).toBeInTheDocument();
  });

  it("does not render the Enter Metadata button for a completed job", async () => {
    configureApiJobs([makeJob({ id: 2, jobId: "job-2", status: "Completed" })]);
    renderScreen();

    await screen.findByText("Job 2");

    expect(
      screen.queryByRole("button", { name: "Enter Metadata" }),
    ).not.toBeInTheDocument();
  });

  it("does not render the Enter Metadata button for a failed job", async () => {
    configureApiJobs([
      makeJob({ id: 3, jobId: "job-3", status: "Failed", failureReason: "boom" }),
    ]);
    renderScreen();

    await screen.findByText("Job 3");

    expect(
      screen.queryByRole("button", { name: "Enter Metadata" }),
    ).not.toBeInTheDocument();
  });

  it("opens the ManualMetadataModal when the Enter Metadata button is clicked", async () => {
    configureApiJobs([makeJob({ status: "AwaitingMetadata" })]);
    renderScreen();

    const button = await screen.findByRole("button", { name: "Enter Metadata" });
    fireEvent.click(button);

    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("reopens the modal after dismissing it and clicking the button again", async () => {
    configureApiJobs([makeJob({ status: "AwaitingMetadata" })]);
    renderScreen();

    // A job is awaiting metadata, so the modal auto-opens on first render.
    expect(await screen.findByRole("dialog")).toBeInTheDocument();

    // Dismiss the modal via Cancel.
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    // Re-open it from the row action button.
    fireEvent.click(screen.getByRole("button", { name: "Enter Metadata" }));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("surfaces a clickable Enter Metadata entry in the failure panel for a manual-metadata failure and opens the modal on click", async () => {
    // A failed job (panel renders) flagged by the backend as requiring manual metadata.
    configureApiJobs([
      makeJob({
        id: 20,
        jobId: "job-20",
        status: "Failed",
        failureDetail: "Could not determine make and model.",
        requiresManualMetadata: true,
      }),
    ]);
    renderScreen();

    const panelButton = await screen.findByRole("button", { name: "Enter Metadata →" });
    fireEvent.click(panelButton);

    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });
});

describe("JobsScreen — source file name subtitle", () => {
  beforeEach(() => {
    apiGet.mockReset();
    apiPost.mockReset();
    apiDelete.mockReset();
  });
  afterEach(() => cleanup());

  it("renders sourceFileName as a subtitle when present", async () => {
    configureApiJobs([
      makeJob({
        id: 10,
        jobId: "job-10",
        status: "Completed",
        sourceFileName: "Honda-CBR600RR-2023.pdf",
      }),
    ]);
    renderScreen();

    await screen.findByText("Job 10");

    expect(screen.getByText("Honda-CBR600RR-2023.pdf")).toBeInTheDocument();
  });

  it("does not render a subtitle when sourceFileName is undefined", async () => {
    configureApiJobs([
      makeJob({ id: 11, jobId: "job-11", status: "Completed", sourceFileName: undefined }),
    ]);
    renderScreen();

    await screen.findByText("Job 11");

    // No file-name subtitle should be present anywhere on the screen.
    expect(screen.queryByText(/\.pdf$/i)).not.toBeInTheDocument();
  });

  it("renders only the basename value of sourceFileName", async () => {
    // The IngestionJobStatus.sourceFileName contract is basename-only (the data layer
    // strips directory paths before populating it). The component surfaces the value
    // verbatim, so passing a basename verifies the subtitle shows just the file name.
    configureApiJobs([
      makeJob({
        id: 12,
        jobId: "job-12",
        status: "Completed",
        sourceFileName: "owner-manual.pdf",
      }),
    ]);
    renderScreen();

    await screen.findByText("Job 12");

    const subtitle = screen.getByText("owner-manual.pdf");
    expect(subtitle).toBeInTheDocument();
    // The subtitle's title attribute carries the same value (used for the hover tooltip).
    expect(subtitle).toHaveAttribute("title", "owner-manual.pdf");
  });
});
