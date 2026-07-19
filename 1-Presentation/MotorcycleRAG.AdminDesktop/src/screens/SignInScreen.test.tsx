import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import SignInScreen from "./SignInScreen";
import { DEFAULT_CONFIG, useConfig } from "@/lib/config";
import { SYSTEM_DEFAULT_BROWSER } from "@/lib/auth";

const { listChromeProfiles, signIn } = vi.hoisted(() => ({
  listChromeProfiles: vi.fn(),
  signIn: vi.fn(),
}));

vi.mock("@/lib/auth", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth")>();
  return {
    ...actual,
    listChromeProfiles,
    signIn,
  };
});

describe("SignInScreen", () => {
  const save = vi.fn();

  beforeEach(() => {
    listChromeProfiles.mockReset();
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
    listChromeProfiles.mockResolvedValue({
      profiles: [
        { directory: "Default", name: "Default" },
        { directory: "Profile 2", name: "Work", userName: "admin@example.com" },
      ],
      error: null,
    });
    render(<SignInScreen />);

    expect(screen.getByText("Loading profiles…")).toBeInTheDocument();
    const select = await screen.findByLabelText("Browser for sign-in");
    expect(select).toHaveValue("Profile 2");
    expect(screen.getByRole("option", { name: "Work (admin@example.com)" })).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "System default browser (recommended)" }),
    ).toBeInTheDocument();

    fireEvent.change(select, { target: { value: "Default" } });
    expect(save).toHaveBeenCalledWith({ selectedChromeProfile: "Default" });
  });

  it("falls back to system default browser when the persisted profile is missing", async () => {
    listChromeProfiles.mockResolvedValue({
      profiles: [{ directory: "Default", name: "Default" }],
      error: null,
    });
    render(<SignInScreen />);

    expect(await screen.findByLabelText("Browser for sign-in")).toHaveValue(
      SYSTEM_DEFAULT_BROWSER,
    );
  });

  it("shows enumeration failure and offers system default browser when profiles are empty", async () => {
    listChromeProfiles.mockResolvedValueOnce({
      profiles: [],
      error: "Could not read Chrome Local State: permission denied",
    });
    const first = render(<SignInScreen />);
    expect(
      await screen.findByText("Could not read Chrome Local State: permission denied"),
    ).toBeInTheDocument();
    expect(await screen.findByLabelText("Browser for sign-in")).toHaveValue(
      SYSTEM_DEFAULT_BROWSER,
    );
    first.unmount();

    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    listChromeProfiles.mockRejectedValueOnce(new Error("Chrome unavailable"));
    render(<SignInScreen />);
    expect(await screen.findByText("Chrome unavailable")).toBeInTheDocument();
    expect(await screen.findByLabelText("Browser for sign-in")).toHaveValue(
      SYSTEM_DEFAULT_BROWSER,
    );
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });

  it("shows a generic message when discovery returns empty profiles without an error string", async () => {
    listChromeProfiles.mockResolvedValue({ profiles: [], error: null });
    render(<SignInScreen />);

    expect(await screen.findByText("Could not read Chrome profiles")).toBeInTheDocument();
    expect(await screen.findByLabelText("Browser for sign-in")).toHaveValue(
      SYSTEM_DEFAULT_BROWSER,
    );
  });

  it("signs in with null profile directory for system default browser", async () => {
    listChromeProfiles.mockResolvedValue({
      profiles: [{ directory: "Default", name: "Default" }],
      error: null,
    });
    signIn.mockResolvedValue(undefined);
    render(<SignInScreen />);
    const select = await screen.findByLabelText("Browser for sign-in");
    expect(select).toHaveValue(SYSTEM_DEFAULT_BROWSER);

    fireEvent.click(screen.getByRole("button", { name: "Sign in with Microsoft" }));
    await waitFor(() => {
      expect(signIn).toHaveBeenCalledWith(null);
    });
  });

  it("signs in with the selected Chrome profile and exposes pending state", async () => {
    listChromeProfiles.mockResolvedValue({
      profiles: [{ directory: "Default", name: "Default" }],
      error: null,
    });
    useConfig.setState({
      config: { ...DEFAULT_CONFIG, selectedChromeProfile: "Default" },
      save,
      loaded: true,
    });
    let resolve!: () => void;
    signIn.mockReturnValue(new Promise<void>((r) => { resolve = r; }));
    render(<SignInScreen />);
    await screen.findByLabelText("Browser for sign-in");

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
    ["unknown failure", "unknown failure"],
  ])("renders sign-in errors from Error and string rejects", async (failure, message) => {
    listChromeProfiles.mockResolvedValue({ profiles: [], error: "enumeration failed" });
    signIn.mockRejectedValue(failure);
    render(<SignInScreen />);
    await screen.findByText("enumeration failed");

    fireEvent.click(screen.getByRole("button", { name: "Sign in with Microsoft" }));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in with Microsoft" })).toBeEnabled();
    expect(signIn).toHaveBeenCalledWith(null);
  });
});
