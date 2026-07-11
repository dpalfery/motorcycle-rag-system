import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import MainLayout from "./MainLayout";

const { useAuth } = vi.hoisted(() => ({
  useAuth: vi.fn(),
}));

vi.mock("../contexts/useAuth", () => ({ useAuth }));

function renderLayout(path = "/") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={<MainLayout />}>
          <Route path="/" element={<div>Chat outlet</div>} />
          <Route path="/settings" element={<div>Settings outlet</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe("MainLayout", () => {
  const logout = vi.fn();

  afterEach(() => {
    cleanup();
    logout.mockReset();
    useAuth.mockReset();
  });

  it("renders navigation, user, and nested outlet", () => {
    useAuth.mockReturnValue({
      user: { user: "rider@example.com", authenticated: true },
      logout,
    });
    renderLayout("/");

    expect(screen.getByText("MOTO")).toBeInTheDocument();
    expect(screen.getByText("RAG")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Chat/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Settings/i })).toBeInTheDocument();
    expect(screen.getByText("Logged in as rider@example.com")).toBeInTheDocument();
    expect(screen.getByText("Chat outlet")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Chat/i })).toHaveClass("text-primary");
  });

  it("marks settings as active and hides user when missing", () => {
    useAuth.mockReturnValue({ user: null, logout });
    renderLayout("/settings");

    expect(screen.getByText("Settings outlet")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Settings/i })).toHaveClass("text-primary");
    expect(screen.queryByText(/Logged in as/)).not.toBeInTheDocument();
  });

  it("invokes logout from the sidebar button", () => {
    useAuth.mockReturnValue({
      user: { user: "rider@example.com", authenticated: true },
      logout,
    });
    renderLayout();

    fireEvent.click(screen.getByTitle("Logout"));
    expect(logout).toHaveBeenCalledOnce();
  });
});
