import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import App from "./App";

const { useAuth } = vi.hoisted(() => ({
  useAuth: vi.fn(),
}));

vi.mock("./contexts/useAuth", () => ({ useAuth }));
vi.mock("./layouts/MainLayout", async () => {
  const { Outlet } = await import("react-router");
  return { default: () => <><div>Main layout</div><Outlet /></> };
});
vi.mock("./pages/ChatPage", () => ({ default: () => <div>Chat page</div> }));
vi.mock("./pages/SettingsPage", () => ({ default: () => <div>Settings page</div> }));
vi.mock("./pages/LoginPage", () => ({ default: () => <div>Login page</div> }));

describe("App", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ user: "rider@example.com", authenticated: true }),
      }),
    );
  });

  afterEach(() => {
    cleanup();
    useAuth.mockReset();
    vi.unstubAllGlobals();
  });

  it("renders login for unauthenticated users on protected routes", () => {
    useAuth.mockReturnValue({ user: null, isLoading: false, login: vi.fn(), logout: vi.fn() });
    window.history.pushState({}, "", "/");
    render(<App />);
    expect(screen.getByText("Login page")).toBeInTheDocument();
  });

  it("renders chat for authenticated users", () => {
    useAuth.mockReturnValue({
      user: { user: "rider@example.com", authenticated: true },
      isLoading: false,
      login: vi.fn(),
      logout: vi.fn(),
    });
    window.history.pushState({}, "", "/");
    render(<App />);
    expect(screen.getByText("Main layout")).toBeInTheDocument();
    expect(screen.getByText("Chat page")).toBeInTheDocument();
  });

  it("renders settings for authenticated users", () => {
    useAuth.mockReturnValue({
      user: { user: "rider@example.com", authenticated: true },
      isLoading: false,
      login: vi.fn(),
      logout: vi.fn(),
    });
    window.history.pushState({}, "", "/settings");
    render(<App />);
    expect(screen.getByText("Settings page")).toBeInTheDocument();
  });

  it("exposes the login route", () => {
    useAuth.mockReturnValue({ user: null, isLoading: false, login: vi.fn(), logout: vi.fn() });
    window.history.pushState({}, "", "/login");
    render(<App />);
    expect(screen.getByText("Login page")).toBeInTheDocument();
  });

  it("redirects unknown routes to home", () => {
    useAuth.mockReturnValue({
      user: { user: "rider@example.com", authenticated: true },
      isLoading: false,
      login: vi.fn(),
      logout: vi.fn(),
    });
    window.history.pushState({}, "", "/unknown-path");
    render(<App />);
    expect(screen.getByText("Chat page")).toBeInTheDocument();
  });
});
