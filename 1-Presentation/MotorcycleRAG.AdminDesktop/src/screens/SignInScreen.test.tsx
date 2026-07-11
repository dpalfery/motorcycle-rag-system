import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import SignInScreen from "./SignInScreen";
import { DEFAULT_CONFIG, useConfig } from "@/lib/config";

const { invoke, signIn } = vi.hoisted(() => ({
  invoke: vi.fn(),
  signIn: vi.fn(),
}));

vi.mock("@tauri-apps/api/core", () => ({ invoke }));
vi.mock("@/lib/auth", () => ({ signIn }));

describe("SignInScreen", () => {
  const save = vi.fn();

  beforeEach(() => {
    invoke.mockReset();
    signIn.mockReset();
    save.mockReset();
    save.mockResolvedValue(undefined);
    useConfig.setState({
      config: { ...DEFAULT_CONFIG, selectedChromeProfile: "Profile 2" },
      save,
      loaded: true,
    });
  });

  afterEach(cleanup);

  it("loads profiles, selects the persisted match, and saves changes", async () => {
    invoke.mockResolvedValue([
      { directory: "Default", name: "Default" },
      { directory: "Profile 2", name: "Work", userName: "admin@example.com" },
    ]);
    render(<SignInScreen />);

    expect(screen.getByText("Loading profiles…")).toBeInTheDocument();
    const select = await screen.findByLabelText("Chrome Profile");
    expect(select).toHaveValue("Profile 2");
    expect(screen.getByRole("option", { name: "Work (admin@example.com)" })).toBeInTheDocument();

    fireEvent.change(select, { target: { value: "Default" } });
    expect(save).toHaveBeenCalledWith({ selectedChromeProfile: "Default" });
  });

  it("falls back to Default when the persisted profile is missing", async () => {
    invoke.mockResolvedValue([{ directory: "Default", name: "Default" }]);
    render(<SignInScreen />);

    expect(await screen.findByLabelText("Chrome Profile")).toHaveValue("Default");
  });

  it("shows the no-profiles fallback after an empty or failed lookup", async () => {
    invoke.mockResolvedValueOnce([]);
    const first = render(<SignInScreen />);
    expect(await screen.findByText("No Chrome profiles detected. Using Default.")).toBeInTheDocument();
    first.unmount();

    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    invoke.mockRejectedValueOnce(new Error("Chrome unavailable"));
    render(<SignInScreen />);
    expect(await screen.findByText("No Chrome profiles detected. Using Default.")).toBeInTheDocument();
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });

  it("signs in with the selected profile and exposes pending state", async () => {
    invoke.mockResolvedValue([{ directory: "Default", name: "Default" }]);
    let resolve!: () => void;
    signIn.mockReturnValue(new Promise<void>((r) => { resolve = r; }));
    render(<SignInScreen />);
    await screen.findByLabelText("Chrome Profile");

    fireEvent.click(screen.getByRole("button", { name: "Sign in with Microsoft" }));
    expect(screen.getByRole("button", { name: "Signing in…" })).toBeDisabled();
    expect(signIn).toHaveBeenCalledWith("Default");

    resolve();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Sign in with Microsoft" })).toBeEnabled(),
    );
  });

  it.each([
    [new Error("Account denied"), "Account denied"],
    ["unknown failure", "Sign-in failed."],
  ])("renders sign-in errors", async (failure, message) => {
    invoke.mockResolvedValue([]);
    signIn.mockRejectedValue(failure);
    render(<SignInScreen />);
    await screen.findByText("No Chrome profiles detected. Using Default.");

    fireEvent.click(screen.getByRole("button", { name: "Sign in with Microsoft" }));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in with Microsoft" })).toBeEnabled();
  });
});
