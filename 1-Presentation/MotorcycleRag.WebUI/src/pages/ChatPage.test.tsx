import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import ChatPage from "./ChatPage";

vi.mock("../components/organisms/ChatInterface", () => ({
  default: () => <div>Chat interface</div>,
}));

describe("ChatPage", () => {
  afterEach(cleanup);

  it("renders the chat interface", () => {
    render(<ChatPage />);
    expect(screen.getByText("Chat interface")).toBeInTheDocument();
  });
});
