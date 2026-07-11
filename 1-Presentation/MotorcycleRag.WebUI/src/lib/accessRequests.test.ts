import { afterEach, describe, expect, it, vi } from "vitest";
import { submitAccessRequest } from "./accessRequests";

describe("submitAccessRequest", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("returns the request payload for a 202 acceptance", async () => {
    const payload = {
      requestId: "req-1",
      email: "rider@example.com",
      provider: "Microsoft",
      requesterVisibleStatus: "PendingReview",
      requestDecisionState: "Pending",
      onboardingExecutionState: "NotStarted",
      rowState: "PendingApproval",
      requestedAtUtc: "2026-07-10T00:00:00Z",
    };
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        status: 202,
        json: async () => payload,
      }),
    );

    const result = await submitAccessRequest({
      email: "rider@example.com",
      provider: "Microsoft",
    });

    expect(result).toEqual({ request: payload, duplicate: false });
    expect(fetch).toHaveBeenCalledWith("/api/access-requests", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: "rider@example.com", provider: "Microsoft" }),
    });
  });

  it("marks duplicate submissions for a 409 response", async () => {
    const payload = {
      requestId: "req-2",
      email: "rider@example.com",
      provider: "Google",
      requesterVisibleStatus: "PendingReview",
      requestDecisionState: "Pending",
      onboardingExecutionState: "NotStarted",
      rowState: "PendingApproval",
      requestedAtUtc: "2026-07-10T00:00:00Z",
    };
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        status: 409,
        json: async () => payload,
      }),
    );

    const result = await submitAccessRequest({
      email: "rider@example.com",
      provider: "Google",
    });

    expect(result.duplicate).toBe(true);
    expect(result.request).toEqual(payload);
  });

  it("throws the API error message when present", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        status: 400,
        json: async () => ({ error: "Email is required." }),
      }),
    );

    await expect(
      submitAccessRequest({ email: "", provider: "Microsoft" }),
    ).rejects.toThrow("Email is required.");
  });

  it("falls back to title when error is missing", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        status: 500,
        json: async () => ({ title: "Server Error" }),
      }),
    );

    await expect(
      submitAccessRequest({ email: "a@b.com", provider: "Microsoft" }),
    ).rejects.toThrow("Server Error");
  });

  it("falls back to a default message when the body is not JSON", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        status: 500,
        json: async () => {
          throw new Error("not json");
        },
      }),
    );

    await expect(
      submitAccessRequest({ email: "a@b.com", provider: "Microsoft" }),
    ).rejects.toThrow("Unable to submit access request.");
  });
});
