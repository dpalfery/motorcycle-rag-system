import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import IngestionJobFailurePanel from "./IngestionJobFailurePanel";
import type { IngestionJobStatus } from "@/lib/ingestionJob";

/**
 * Builds a minimal IngestionJobStatus for failure-panel scenarios. Defaults to a failed
 * PDF job so the panel has a reason to render; override any field per test.
 */
function makeJob(overrides: Partial<IngestionJobStatus> = {}): IngestionJobStatus {
  return {
    jobId: "job-failed-1",
    status: "Failed",
    createdAtUtc: new Date().toISOString(),
    inputType: "manual-pdf",
    inputRef: "blob://source.pdf",
    failureReason: "The document could not be processed.",
    ...overrides,
  };
}

describe("IngestionJobFailurePanel", () => {
  afterEach(() => cleanup());

  it("renders the failure detail text when present", () => {
    render(
      <IngestionJobFailurePanel job={makeJob({ failureDetail: "OCR pipeline timed out" })} />,
    );
    expect(screen.getByText("OCR pipeline timed out")).toBeInTheDocument();
  });

  it("renders null when there is no failure text", () => {
    const { container } = render(
      <IngestionJobFailurePanel
        job={makeJob({ failureReason: undefined, failureDetail: undefined })}
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  describe("Enter Metadata button", () => {
    it("renders a clickable button when requiresManualMetadata is true and a callback is supplied", () => {
      const onEnterMetadata = vi.fn();
      render(
        <IngestionJobFailurePanel
          job={makeJob({ requiresManualMetadata: true })}
          onEnterMetadata={onEnterMetadata}
        />,
      );

      const button = screen.getByRole("button", { name: /enter metadata/i });
      expect(button).toBeInTheDocument();

      fireEvent.click(button);
      expect(onEnterMetadata).toHaveBeenCalledTimes(1);
    });

    it("does not render the button when requiresManualMetadata is false", () => {
      render(
        <IngestionJobFailurePanel
          job={makeJob({ requiresManualMetadata: false })}
          onEnterMetadata={vi.fn()}
        />,
      );
      expect(screen.queryByRole("button", { name: /enter metadata/i })).not.toBeInTheDocument();
    });

    it("does not render the button when requiresManualMetadata is undefined", () => {
      render(
        <IngestionJobFailurePanel
          job={makeJob({ requiresManualMetadata: undefined })}
          onEnterMetadata={vi.fn()}
        />,
      );
      expect(screen.queryByRole("button", { name: /enter metadata/i })).not.toBeInTheDocument();
    });

    it("does not render the button when requiresManualMetadata is true but onEnterMetadata is omitted", () => {
      render(<IngestionJobFailurePanel job={makeJob({ requiresManualMetadata: true })} />);
      expect(screen.queryByRole("button", { name: /enter metadata/i })).not.toBeInTheDocument();
    });
  });
});
