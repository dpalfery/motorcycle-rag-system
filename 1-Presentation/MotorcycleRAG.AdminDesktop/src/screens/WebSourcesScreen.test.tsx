import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import WebSourcesScreen from "./WebSourcesScreen";
import { renderWithQuery } from "@/test/renderWithQuery";

const { get, post, put, remove } = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  remove: vi.fn(),
}));

vi.mock("@/lib/apiClient", () => ({
  api: { get, post, put, delete: remove },
}));

describe("WebSourcesScreen", () => {
  beforeEach(() => {
    get.mockReset();
    post.mockReset();
    put.mockReset();
    remove.mockReset();
    post.mockResolvedValue({ data: {} });
    put.mockResolvedValue({ data: {} });
    remove.mockResolvedValue({ data: {} });
  });

  afterEach(cleanup);

  it("shows loading, empty, and error states", async () => {
    let resolve!: (value: unknown) => void;
    get.mockReturnValueOnce(new Promise((r) => { resolve = r; }));
    const first = renderWithQuery(<WebSourcesScreen />);
    expect(screen.getByText("Loading…")).toBeInTheDocument();
    resolve({ data: [] });
    expect(await screen.findByText("No web sources configured.")).toBeInTheDocument();
    first.unmount();

    get.mockRejectedValueOnce(new Error("offline"));
    renderWithQuery(<WebSourcesScreen />);
    expect(await screen.findByText("Could not load web sources.")).toBeInTheDocument();
  });

  it("renders wrapped source data and status badges", async () => {
    get.mockResolvedValue({
      data: {
        items: [
          {
            id: "source-1",
            name: "Manuals",
            url: "https://example.com/manuals",
            trustTier: "trusted",
            isActive: false,
          },
        ],
      },
    });

    renderWithQuery(<WebSourcesScreen />);

    expect(await screen.findByText("Manuals")).toBeInTheDocument();
    expect(screen.getByText("trusted")).toBeInTheDocument();
    expect(screen.getByText("inactive")).toBeInTheDocument();
    expect(screen.getByText("https://example.com/manuals")).toBeInTheDocument();
  });

  it("validates and creates a source", async () => {
    get.mockResolvedValue({ data: [] });
    renderWithQuery(<WebSourcesScreen />);
    await screen.findByText("No web sources configured.");

    fireEvent.click(screen.getByRole("button", { name: /add source/i }));
    const save = screen.getByRole("button", { name: /save/i });
    expect(save).toBeDisabled();

    fireEvent.change(screen.getByPlaceholderText("My source"), {
      target: { value: "OEM manuals" },
    });
    fireEvent.change(screen.getByPlaceholderText("https://example.com"), {
      target: { value: "not a url" },
    });
    expect(save).toBeDisabled();

    fireEvent.change(screen.getByPlaceholderText("https://example.com"), {
      target: { value: "https://manuals.example.com" },
    });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "trusted" } });
    fireEvent.click(save);

    await waitFor(() => expect(post).toHaveBeenCalledWith(
      "/api/admin/web-sources",
      {
        name: "OEM manuals",
        url: "https://manuals.example.com",
        trustTier: "trusted",
      },
    ));
    await waitFor(() => expect(screen.queryByPlaceholderText("My source")).not.toBeInTheDocument());
  });

  it("cancels add and edit forms", async () => {
    get.mockResolvedValue({
      data: [{ id: "source-1", name: "Manuals", url: "https://example.com" }],
    });
    renderWithQuery(<WebSourcesScreen />);
    await screen.findByText("Manuals");

    fireEvent.click(screen.getByRole("button", { name: /add source/i }));
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.getByRole("button", { name: /add source/i })).toBeInTheDocument();

    fireEvent.click(screen.getByTitle("Edit"));
    expect(screen.getByDisplayValue("Manuals")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.queryByDisplayValue("Manuals")).not.toBeInTheDocument();
  });

  it("updates an existing source", async () => {
    get.mockResolvedValue({
      data: [{
        id: "source-1",
        name: "Manuals",
        url: "https://example.com",
        trustTier: "restricted",
      }],
    });
    renderWithQuery(<WebSourcesScreen />);
    await screen.findByText("Manuals");

    fireEvent.click(screen.getByTitle("Edit"));
    fireEvent.change(screen.getByDisplayValue("Manuals"), {
      target: { value: "Updated manuals" },
    });
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => expect(put).toHaveBeenCalledWith(
      "/api/admin/web-sources/source-1",
      {
        name: "Updated manuals",
        url: "https://example.com",
        trustTier: "restricted",
      },
    ));
  });

  it("deletes only after confirmation", async () => {
    get.mockResolvedValue({
      data: [{ id: "source-1", name: "Manuals", url: "https://example.com" }],
    });
    const confirmSpy = vi.spyOn(window, "confirm");
    renderWithQuery(<WebSourcesScreen />);
    await screen.findByText("Manuals");

    confirmSpy.mockReturnValueOnce(false);
    fireEvent.click(screen.getByTitle("Delete"));
    expect(remove).not.toHaveBeenCalled();

    confirmSpy.mockReturnValueOnce(true);
    fireEvent.click(screen.getByTitle("Delete"));
    await waitFor(() => expect(remove).toHaveBeenCalledWith(
      "/api/admin/web-sources/source-1",
    ));
    confirmSpy.mockRestore();
  });
});
