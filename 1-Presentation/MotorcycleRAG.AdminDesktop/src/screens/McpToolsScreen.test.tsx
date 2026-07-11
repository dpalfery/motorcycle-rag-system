import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import McpToolsScreen from "./McpToolsScreen";
import { renderWithQuery } from "@/test/renderWithQuery";

const { get, put } = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
}));

vi.mock("@/lib/apiClient", () => ({ api: { get, put } }));

describe("McpToolsScreen", () => {
  beforeEach(() => {
    get.mockReset();
    put.mockReset();
    put.mockResolvedValue({ data: {} });
  });

  afterEach(cleanup);

  it("renders loading, empty, and error states", async () => {
    let resolve!: (value: unknown) => void;
    get.mockReturnValueOnce(new Promise((r) => { resolve = r; }));
    const first = renderWithQuery(<McpToolsScreen />);
    expect(screen.getByText("Loading tools…")).toBeInTheDocument();
    resolve({ data: [] });
    expect(await screen.findByText("No MCP tools registered.")).toBeInTheDocument();
    first.unmount();

    get.mockRejectedValueOnce(new Error("offline"));
    renderWithQuery(<McpToolsScreen />);
    expect(await screen.findByText("Could not load MCP tools.")).toBeInTheDocument();
  });

  it("renders wrapped tools, descriptions, disabled state, and counts", async () => {
    get.mockResolvedValue({
      data: {
        items: [
          { id: "one", name: "Search", description: "Search manuals", isEnabled: true },
          { id: "two", name: "Crawler", isEnabled: false },
        ],
      },
    });
    renderWithQuery(<McpToolsScreen />);

    expect(await screen.findByText("1 of 2 enabled")).toBeInTheDocument();
    expect(screen.getByText("Search manuals")).toBeInTheDocument();
    expect(screen.getByText("disabled")).toBeInTheDocument();
    expect(screen.getAllByRole("switch")[0]).toHaveAttribute("aria-checked", "true");
    expect(screen.getAllByRole("switch")[1]).toHaveAttribute("aria-checked", "false");
  });

  it("optimistically toggles a tool and refreshes it after success", async () => {
    get.mockResolvedValue({
      data: [{ id: "one", name: "Search", isEnabled: true }],
    });
    let resolve!: (value: unknown) => void;
    put.mockReturnValue(new Promise((r) => { resolve = r; }));
    const { queryClient } = renderWithQuery(<McpToolsScreen />);
    const toggle = await screen.findByRole("switch");

    fireEvent.click(toggle);

    await waitFor(() => expect(put).toHaveBeenCalledWith(
      "/api/admin/mcp-tools/one",
      { isEnabled: false },
    ));
    await waitFor(() => expect(
      queryClient.getQueryData<Array<{ isEnabled: boolean }>>(["mcp-tools"])?.[0].isEnabled,
    ).toBe(false));
    resolve({ data: {} });
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2));
  });

  it("rolls back optimistic state when the toggle request fails", async () => {
    get.mockResolvedValue({
      data: [{ id: "one", name: "Search", isEnabled: true }],
    });
    put.mockRejectedValue(new Error("denied"));
    const { queryClient } = renderWithQuery(<McpToolsScreen />);
    fireEvent.click(await screen.findByRole("switch"));

    await waitFor(() => expect(put).toHaveBeenCalledOnce());
    await waitFor(() => expect(
      queryClient.getQueryData<Array<{ isEnabled: boolean }>>(["mcp-tools"])?.[0].isEnabled,
    ).toBe(true));
  });

  it("refreshes the tools query", async () => {
    get.mockResolvedValue({ data: [] });
    renderWithQuery(<McpToolsScreen />);
    await screen.findByText("No MCP tools registered.");

    fireEvent.click(screen.getByRole("button", { name: /refresh/i }));
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2));
  });
});
