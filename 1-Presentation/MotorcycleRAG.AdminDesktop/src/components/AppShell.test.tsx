import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import AppShell from "./AppShell";

const { ensureProcessorReady, save, signOut, useAuthExpiry } = vi.hoisted(() => ({
  ensureProcessorReady: vi.fn(),
  save: vi.fn(),
  signOut: vi.fn(),
  useAuthExpiry: vi.fn(),
}));

const config = {
  localProcessorWorkingDir: "",
};

vi.mock("@/lib/config", () => ({
  useConfig: () => ({ config, save }),
}));
vi.mock("@/lib/auth", () => ({
  useAuth: () => ({ account: "admin@example.com", signOut }),
}));
vi.mock("@/lib/processor", () => ({ ensureProcessorReady }));
vi.mock("@/lib/useAuthExpiry", () => ({ useAuthExpiry }));
vi.mock("./HealthStatusIndicator", () => ({
  default: () => <div>Health ready</div>,
}));

function renderShell(path = "/processor") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="*" element={<div>Screen content</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe("AppShell", () => {
  beforeEach(() => {
    ensureProcessorReady.mockReset();
    save.mockReset();
    signOut.mockReset();
    useAuthExpiry.mockReset();
    ensureProcessorReady.mockResolvedValue({ localProcessorWorkingDir: "" });
    signOut.mockResolvedValue(undefined);
  });

  afterEach(cleanup);

  it("renders navigation, account, health, and nested content", () => {
    renderShell();

    expect(screen.getByText("MotorcycleRAG Admin")).toBeInTheDocument();
    expect(screen.getByText("Health ready")).toBeInTheDocument();
    expect(screen.getByText("Screen content")).toBeInTheDocument();
    expect(screen.getByText("admin@example.com")).toBeInTheDocument();
    for (const label of ["Processor", "Web sources", "MCP tools", "Users", "Settings"]) {
      expect(screen.getByRole("link", { name: label })).toBeInTheDocument();
    }
    expect(useAuthExpiry).toHaveBeenCalled();
    expect(screen.getByRole("link", { name: "Processor" })).toHaveClass("text-primary");
  });

  it("persists an auto-resolved processor directory", async () => {
    ensureProcessorReady.mockResolvedValue({
      localProcessorWorkingDir: "/resolved/processor",
    });
    renderShell();

    await waitFor(() => expect(save).toHaveBeenCalledWith({
      localProcessorWorkingDir: "/resolved/processor",
    }));
    expect(ensureProcessorReady).toHaveBeenCalledOnce();
  });

  it("does not save an unchanged directory and logs startup failures", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const first = renderShell();
    await waitFor(() => expect(ensureProcessorReady).toHaveBeenCalledOnce());
    expect(save).not.toHaveBeenCalled();
    first.unmount();

    ensureProcessorReady.mockRejectedValueOnce(new Error("processor unavailable"));
    renderShell();
    await waitFor(() => expect(error).toHaveBeenCalledWith(
      "Failed to auto-start local processor:",
      expect.any(Error),
    ));
    error.mockRestore();
  });

  it("signs out from the account action", async () => {
    renderShell("/settings");
    expect(screen.getByRole("link", { name: "Settings" })).toHaveClass("text-primary");

    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));
    await waitFor(() => expect(signOut).toHaveBeenCalledOnce());
  });
});
