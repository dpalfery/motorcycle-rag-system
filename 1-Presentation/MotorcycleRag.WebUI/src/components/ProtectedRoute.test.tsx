import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import ProtectedRoute from "./ProtectedRoute";

const { useAuth } = vi.hoisted(() => ({
  useAuth: vi.fn(),
}));

vi.mock("../contexts/useAuth", () => ({ useAuth }));

function renderProtected(auth: { user: unknown; isLoading: boolean }) {
  useAuth.mockReturnValue(auth);
  return render(
    <MemoryRouter initialEntries={["/secure"]}>
      <Routes>
        <Route
          path="/secure"
          element={
            <ProtectedRoute>
              <div>Protected content</div>
            </ProtectedRoute>
          }
        />
        <Route path="/login" element={<div>Login page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ProtectedRoute", () => {
  afterEach(() => {
    cleanup();
    useAuth.mockReset();
  });

  it("shows the boot message while auth is loading", () => {
    renderProtected({ user: null, isLoading: true });
    expect(screen.getByText(/I was sleeping/i)).toBeInTheDocument();
  });

  it("redirects unauthenticated users to login", () => {
    renderProtected({ user: null, isLoading: false });
    expect(screen.getByText("Login page")).toBeInTheDocument();
  });

  it("redirects when authenticated is false", () => {
    renderProtected({
      user: { user: "x", authenticated: false },
      isLoading: false,
    });
    expect(screen.getByText("Login page")).toBeInTheDocument();
  });

  it("renders children for authenticated users", () => {
    renderProtected({
      user: { user: "rider@example.com", authenticated: true },
      isLoading: false,
    });
    expect(screen.getByText("Protected content")).toBeInTheDocument();
  });
});
