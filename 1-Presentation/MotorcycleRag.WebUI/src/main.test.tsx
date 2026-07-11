import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { createRoot, render } = vi.hoisted(() => ({
  createRoot: vi.fn(),
  render: vi.fn(),
}));

vi.mock("react-dom/client", () => ({
  default: { createRoot },
  createRoot,
}));
vi.mock("./App", () => ({ default: () => null }));
vi.mock("./index.css", () => ({}));

describe("application bootstrap", () => {
  beforeEach(() => {
    vi.resetModules();
    document.body.innerHTML = '<div id="root"></div>';
    createRoot.mockReset();
    render.mockReset();
    createRoot.mockReturnValue({ render });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("creates a React root and renders the app", async () => {
    await import("./main");

    expect(createRoot).toHaveBeenCalledWith(document.getElementById("root"));
    expect(render).toHaveBeenCalledOnce();
  });
});
