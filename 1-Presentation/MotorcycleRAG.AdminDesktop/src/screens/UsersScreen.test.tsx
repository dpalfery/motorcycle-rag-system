import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, screen } from "@testing-library/react";
import UsersScreen from "./UsersScreen";
import { renderWithQuery } from "@/test/renderWithQuery";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/apiClient", () => ({ api: { get } }));

describe("UsersScreen", () => {
  beforeEach(() => get.mockReset());
  afterEach(cleanup);

  it("renders loading then empty results", async () => {
    let resolve!: (value: unknown) => void;
    get.mockReturnValue(new Promise((r) => { resolve = r; }));
    renderWithQuery(<UsersScreen />);

    expect(screen.getByText("Loading users…")).toBeInTheDocument();
    resolve({ data: [] });
    expect(await screen.findByText("No users found.")).toBeInTheDocument();
    expect(get).toHaveBeenCalledWith("/api/admin/users");
  });

  it("renders wrapped users with optional tier and enabled status", async () => {
    get.mockResolvedValue({
      data: {
        items: [
          { userId: "1", email: "enabled@example.com", tier: "admin" },
          { userId: "2", email: "disabled@example.com", enabled: false },
        ],
      },
    });
    renderWithQuery(<UsersScreen />);

    expect(await screen.findByText("enabled@example.com")).toBeInTheDocument();
    expect(screen.getByText("disabled@example.com")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
    expect(screen.getByText("Enabled")).toBeInTheDocument();
    expect(screen.getByText("Disabled")).toBeInTheDocument();
  });
});
