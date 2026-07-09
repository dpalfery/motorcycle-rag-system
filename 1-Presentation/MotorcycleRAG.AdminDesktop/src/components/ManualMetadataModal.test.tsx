import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import ManualMetadataModal from "./ManualMetadataModal";

const VALID_JSON = JSON.stringify(
  { make: "Honda", model: "CBR600RR", year: 2023, category: "sport", tags: ["600cc"] },
  null,
  2,
);

function renderModal(overrides: Partial<React.ComponentProps<typeof ManualMetadataModal>> = {}) {
  const props = {
    isOpen: true,
    jobId: "job-abc",
    initialMetadata: undefined,
    onSubmit: vi.fn().mockResolvedValue(undefined),
    onClose: vi.fn(),
    ...overrides,
  };
  render(<ManualMetadataModal {...props} />);
  return props;
}

describe("ManualMetadataModal", () => {
  afterEach(() => cleanup());

  it("renders nothing when closed", () => {
    renderModal({ isOpen: false });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("renders title, subtitle, the job id and a JSON textarea when open", () => {
    renderModal();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Manual Metadata Required")).toBeInTheDocument();
    expect(
      screen.getByText(/Automatic metadata extraction could not determine all required fields/),
    ).toBeInTheDocument();
    expect(screen.getByText("job-abc")).toBeInTheDocument();
    expect(screen.getByLabelText("Manual metadata JSON")).toBeInTheDocument();
  });

  it("pre-fills the textarea with initialMetadata", () => {
    renderModal({ initialMetadata: VALID_JSON });
    expect(screen.getByLabelText("Manual metadata JSON")).toHaveValue(VALID_JSON);
  });

  it("shows the JSON schema hint", () => {
    renderModal();
    expect(screen.getByText(/Expected schema/i)).toBeInTheDocument();
    expect(screen.getByText(/"CBR600RR"/)).toBeInTheDocument();
  });

  it("renders Submit and Cancel buttons", () => {
    renderModal();
    expect(screen.getByRole("button", { name: "Submit" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
  });

  it("shows an inline error and does not submit when the JSON is invalid", () => {
    const props = renderModal();
    fireEvent.change(screen.getByLabelText("Manual metadata JSON"), {
      target: { value: "{not valid" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(props.onSubmit).not.toHaveBeenCalled();
  });

  it("shows an error listing missing required fields", () => {
    renderModal();
    fireEvent.change(screen.getByLabelText("Manual metadata JSON"), {
      target: { value: '{"make":"Honda"}' },
    });
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    expect(screen.getByRole("alert").textContent).toMatch(/model.*year.*category|category|year/i);
  });

  it("calls onSubmit with the trimmed JSON and resolves on success", async () => {
    const props = renderModal({ initialMetadata: VALID_JSON });
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() => {
      expect(props.onSubmit).toHaveBeenCalledTimes(1);
    });
    expect(props.onSubmit).toHaveBeenCalledWith(VALID_JSON);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("disables the Submit button while submitting", async () => {
    let resolveSubmit: () => void = () => undefined;
    const onSubmit = vi.fn(
      () => new Promise<void>((resolve) => {
        resolveSubmit = resolve;
      }),
    );
    renderModal({ initialMetadata: VALID_JSON, onSubmit });

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Submit" }));
    });

    const submitButton = await screen.findByRole("button", { name: /Submitting/i });
    expect(submitButton).toBeDisabled();

    // Resolve the pending submit so React's finally-block state update flushes inside act.
    await act(async () => {
      resolveSubmit();
      await Promise.resolve();
    });
  });

  it("shows the rejected error inline and keeps the modal open on submit failure", async () => {
    const onSubmit = vi.fn().mockRejectedValue(new Error("Server rejected metadata"));
    renderModal({ initialMetadata: VALID_JSON, onSubmit });

    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("Server rejected metadata");
    });
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("calls onClose when Cancel is clicked", () => {
    const props = renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(props.onClose).toHaveBeenCalledTimes(1);
  });

  it("calls onClose when the X button is clicked", () => {
    const props = renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(props.onClose).toHaveBeenCalledTimes(1);
  });

  it("calls onClose when Escape is pressed", () => {
    const props = renderModal();
    fireEvent.keyDown(window, { key: "Escape" });
    expect(props.onClose).toHaveBeenCalledTimes(1);
  });
});
