import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useAuth } from "./auth";
import { useAuthExpiry } from "./useAuthExpiry";

const { refreshAccessToken, toastError } = vi.hoisted(() => ({
  refreshAccessToken: vi.fn(),
  toastError: vi.fn(),
}));

vi.mock("./apiClient", () => ({ refreshAccessToken }));
vi.mock("sonner", () => ({ toast: { error: toastError } }));

describe("useAuthExpiry", () => {
  const originalSignOut = useAuth.getState().signOut;

  beforeEach(() => {
    vi.useFakeTimers();
    refreshAccessToken.mockReset();
    toastError.mockReset();
    useAuth.setState({
      signedIn: false,
      expiresAt: null,
      accessToken: null,
      account: null,
      signOut: originalSignOut,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("does not refresh without an active expiring session", () => {
    renderHook(() => useAuthExpiry());
    act(() => vi.runAllTimers());
    expect(refreshAccessToken).not.toHaveBeenCalled();
  });

  it("refreshes five minutes before expiry", async () => {
    const now = new Date("2026-07-10T12:00:00Z");
    vi.setSystemTime(now);
    refreshAccessToken.mockResolvedValue("new-token");
    useAuth.setState({
      signedIn: true,
      expiresAt: (now.getTime() + 10 * 60_000) / 1000,
    });

    renderHook(() => useAuthExpiry());
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5 * 60_000);
    });

    expect(refreshAccessToken).toHaveBeenCalledOnce();
    expect(toastError).not.toHaveBeenCalled();
  });

  it("refreshes immediately when already inside the buffer", async () => {
    refreshAccessToken.mockResolvedValue("new-token");
    useAuth.setState({
      signedIn: true,
      expiresAt: (Date.now() + 60_000) / 1000,
    });

    renderHook(() => useAuthExpiry());

    await act(async () => {
      await vi.runOnlyPendingTimersAsync();
    });

    expect(refreshAccessToken).toHaveBeenCalledOnce();
  });

  it("reports expiry and signs out when refresh returns no token", async () => {
    const signOut = vi.fn().mockRejectedValue(new Error("keychain unavailable"));
    refreshAccessToken.mockResolvedValue(null);
    useAuth.setState({
      signedIn: true,
      expiresAt: (Date.now() + 60_000) / 1000,
      signOut,
    });

    renderHook(() => useAuthExpiry());

    await act(async () => {
      await vi.runOnlyPendingTimersAsync();
    });

    expect(signOut).toHaveBeenCalledOnce();
    expect(toastError).toHaveBeenCalledWith(
      "Your session has expired. Please sign in again.",
    );
  });

  it("cancels a scheduled refresh after unmount", async () => {
    useAuth.setState({
      signedIn: true,
      expiresAt: (Date.now() + 10 * 60_000) / 1000,
    });
    const { unmount } = renderHook(() => useAuthExpiry());

    unmount();
    await act(async () => {
      await vi.runAllTimersAsync();
    });

    expect(refreshAccessToken).not.toHaveBeenCalled();
  });
});
