import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import LoginPage from "./LoginPage";

const { useAuth, submitAccessRequest } = vi.hoisted(() => ({
  useAuth: vi.fn(),
  submitAccessRequest: vi.fn(),
}));

vi.mock("../contexts/useAuth", () => ({ useAuth }));
vi.mock("../lib/accessRequests", () => ({ submitAccessRequest }));

function renderLogin() {
  return render(
    <MemoryRouter initialEntries={["/login"]}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/" element={<div>Home</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("LoginPage", () => {
  const login = vi.fn();

  beforeEach(() => {
    login.mockReset();
    submitAccessRequest.mockReset();
    useAuth.mockReturnValue({
      user: null,
      isLoading: false,
      login,
    });
  });

  afterEach(cleanup);

  it("shows the boot message while loading", () => {
    useAuth.mockReturnValue({ user: null, isLoading: true, login });
    renderLogin();
    expect(screen.getByText(/I was sleeping/i)).toBeInTheDocument();
  });

  it("redirects authenticated users home", () => {
    useAuth.mockReturnValue({
      user: { user: "rider@example.com", authenticated: true },
      isLoading: false,
      login,
    });
    renderLogin();
    expect(screen.getByText("Home")).toBeInTheDocument();
  });

  it("shows approval state and signs in via SSO", () => {
    useAuth.mockReturnValue({
      user: {
        user: "rider@example.com",
        authenticated: false,
        sessionAuthenticated: true,
        accessApproved: false,
        approvalStatus: "Pending Review",
        approvalMessage: "Waiting on admin.",
      },
      isLoading: false,
      login,
    });
    renderLogin();

    expect(screen.getByText("Pending Review")).toBeInTheDocument();
    expect(screen.getByText("Waiting on admin.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Sign In with SSO/i }));
    expect(login).toHaveBeenCalledOnce();
  });

  it("uses default approval copy when status fields are missing", () => {
    useAuth.mockReturnValue({
      user: {
        user: "rider@example.com",
        authenticated: false,
        sessionAuthenticated: true,
        accessApproved: false,
      },
      isLoading: false,
      login,
    });
    renderLogin();

    expect(screen.getByText("Approval Required")).toBeInTheDocument();
    expect(screen.getByText("This account cannot access the application yet.")).toBeInTheDocument();
  });

  it("submits an access request and shows the result", async () => {
    submitAccessRequest.mockResolvedValue({
      request: {
        requestId: "req-1",
        email: "new@example.com",
        provider: "Google",
        requesterVisibleStatus: "PendingReview",
        statusMessage: "Queued for review",
        requestDecisionState: "Pending",
        onboardingExecutionState: "NotStarted",
        rowState: "PendingApproval",
        requestedAtUtc: "2026-07-10T00:00:00Z",
      },
      duplicate: false,
    });
    renderLogin();

    fireEvent.change(screen.getByLabelText(/Email address/i), {
      target: { value: "new@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/Sign-in provider/i), {
      target: { value: "Google" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Request Access/i }));

    await waitFor(() => expect(screen.getByText("PendingReview")).toBeInTheDocument());
    expect(screen.getByText("Queued for review")).toBeInTheDocument();
    expect(screen.getByText("Provider: Google")).toBeInTheDocument();
    expect(screen.getByText("Email: new@example.com")).toBeInTheDocument();
    expect(submitAccessRequest).toHaveBeenCalledWith({
      email: "new@example.com",
      provider: "Google",
    });
  });

  it("shows submit errors from Error instances and unknown failures", async () => {
    submitAccessRequest.mockRejectedValueOnce(new Error("Already pending"));
    renderLogin();

    fireEvent.change(screen.getByLabelText(/Email address/i), {
      target: { value: "new@example.com" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Request Access/i }));
    await waitFor(() => expect(screen.getByText("Already pending")).toBeInTheDocument());

    cleanup();
    useAuth.mockReturnValue({ user: null, isLoading: false, login });
    submitAccessRequest.mockRejectedValueOnce("boom");
    renderLogin();
    fireEvent.change(screen.getByLabelText(/Email address/i), {
      target: { value: "new@example.com" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Request Access/i }));
    await waitFor(() =>
      expect(screen.getByText("Unable to submit your access request.")).toBeInTheDocument(),
    );
  });
});
