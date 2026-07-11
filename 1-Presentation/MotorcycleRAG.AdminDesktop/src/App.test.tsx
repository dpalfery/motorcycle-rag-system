import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import App from "./App";

const { authState } = vi.hoisted(() => ({
  authState: { signedIn: false },
}));

vi.mock("@/lib/auth", () => ({ useAuth: () => authState }));
vi.mock("./components/AppShell", async () => {
  const { Outlet } = await import("react-router-dom");
  return { default: () => <><div>App shell</div><Outlet /></> };
});
vi.mock("./screens/SignInScreen", () => ({ default: () => <div>Sign-in screen</div> }));
vi.mock("./screens/ProcessorScreen", () => ({ default: () => <div>Processor screen</div> }));
vi.mock("./screens/WebSourcesScreen", () => ({ default: () => <div>Web sources screen</div> }));
vi.mock("./screens/McpToolsScreen", () => ({ default: () => <div>MCP tools screen</div> }));
vi.mock("./screens/UsersScreen", () => ({ default: () => <div>Users screen</div> }));
vi.mock("./screens/SettingsScreen", () => ({ default: () => <div>Settings screen</div> }));

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
}

describe("App", () => {
  afterEach(cleanup);

  it("shows sign-in without mounting authenticated routes", () => {
    authState.signedIn = false;
    renderAt("/processor");

    expect(screen.getByText("Sign-in screen")).toBeInTheDocument();
    expect(screen.queryByText("App shell")).not.toBeInTheDocument();
  });

  it.each([
    ["/processor", "Processor screen"],
    ["/web-sources", "Web sources screen"],
    ["/mcp-tools", "MCP tools screen"],
    ["/users", "Users screen"],
    ["/settings", "Settings screen"],
  ])("renders %s for signed-in users", (path, expected) => {
    authState.signedIn = true;
    renderAt(path);

    expect(screen.getByText("App shell")).toBeInTheDocument();
    expect(screen.getByText(expected)).toBeInTheDocument();
  });

  it.each(["/", "/ingestion", "/jobs", "/unknown"])(
    "redirects %s to the processor",
    (path) => {
      authState.signedIn = true;
      renderAt(path);
      expect(screen.getByText("Processor screen")).toBeInTheDocument();
    },
  );
});
