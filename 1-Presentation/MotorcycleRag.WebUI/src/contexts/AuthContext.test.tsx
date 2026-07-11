import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, screen, waitFor } from "@testing-library/react";
import { AuthProvider } from "./AuthContext";
import { useAuth } from "./useAuth";
import { renderWithQuery } from "../test/renderWithQuery";

function AuthProbe() {
  const { user, isLoading, login, logout } = useAuth();
  return (
    <div>
      <div data-testid="loading">{String(isLoading)}</div>
      <div data-testid="user">{user ? user.user : "none"}</div>
      <div data-testid="authenticated">{String(user?.authenticated ?? false)}</div>
      <button type="button" onClick={login}>Login</button>
      <button type="button" onClick={() => void logout()}>Logout</button>
    </div>
  );
}

describe("AuthProvider", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ user: "x", authenticated: false }),
      }),
    );
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("exposes the authenticated user from /auth/me", async () => {
    vi.mocked(fetch).mockResolvedValue({
      ok: true,
      json: async () => ({ user: "rider@example.com", authenticated: true }),
    } as Response);

    renderWithQuery(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("loading")).toHaveTextContent("false"));
    expect(screen.getByTestId("user")).toHaveTextContent("rider@example.com");
    expect(screen.getByTestId("authenticated")).toHaveTextContent("true");
  });

  it("treats authenticated:false as signed out", async () => {
    vi.mocked(fetch).mockResolvedValue({
      ok: true,
      json: async () => ({ user: "rider@example.com", authenticated: false }),
    } as Response);

    renderWithQuery(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("loading")).toHaveTextContent("false"));
    expect(screen.getByTestId("user")).toHaveTextContent("none");
  });

  it("returns null when /auth/me is not ok", async () => {
    vi.mocked(fetch).mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => ({}),
    } as Response);

    renderWithQuery(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("loading")).toHaveTextContent("false"));
    expect(screen.getByTestId("user")).toHaveTextContent("none");
  });

  it("navigates to /auth/login on login", async () => {
    const location = { ...window.location, href: "http://localhost/", assign: vi.fn() };
    Object.defineProperty(window, "location", {
      configurable: true,
      value: location,
    });

    renderWithQuery(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("loading")).toHaveTextContent("false"));
    screen.getByRole("button", { name: "Login" }).click();
    expect(window.location.href).toBe("/auth/login");
  });

  it("posts logout then navigates home", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ user: "rider@example.com", authenticated: true }),
      } as Response)
      .mockResolvedValueOnce({ ok: true } as Response);

    const location = { ...window.location, href: "http://localhost/", assign: vi.fn() };
    Object.defineProperty(window, "location", {
      configurable: true,
      value: location,
    });

    renderWithQuery(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("user")).toHaveTextContent("rider@example.com"));
    screen.getByRole("button", { name: "Logout" }).click();

    await waitFor(() => {
      expect(fetch).toHaveBeenCalledWith("/auth/logout", { method: "POST" });
      expect(window.location.href).toBe("/");
    });
  });
});

describe("useAuth", () => {
  afterEach(cleanup);

  it("throws when used outside AuthProvider", () => {
    expect(() => renderWithQuery(<AuthProbe />)).toThrow(
      "useAuth must be used within an AuthProvider",
    );
  });
});
