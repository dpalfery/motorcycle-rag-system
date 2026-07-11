import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MessageBubble } from "./MessageBubble";
import type { Message } from "../../types/chat";

describe("MessageBubble", () => {
  afterEach(cleanup);

  it("renders a user message without markdown chrome", () => {
    const message: Message = {
      id: "1",
      role: "user",
      content: "What oil for an R1?",
      timestamp: new Date("2026-07-10T12:00:00Z"),
    };

    render(<MessageBubble message={message} />);
    expect(screen.getByText("What oil for an R1?")).toBeInTheDocument();
  });

  it("renders assistant markdown and action buttons", () => {
    const onAction = vi.fn();
    const message: Message = {
      id: "2",
      role: "assistant",
      content: "## Specs\n\n- Torque\n\n`code`",
      timestamp: new Date("2026-07-10T12:00:00Z"),
      actions: [
        { label: "Manuals", type: "callback", value: "manuals", icon: "manuals", reason: "docs" },
        { label: "Specs", type: "callback", value: "specs", icon: "specs" },
        { label: "Other", type: "link", value: "#" },
      ],
    };

    render(<MessageBubble message={message} onAction={onAction} />);

    expect(screen.getByRole("heading", { name: "Specs" })).toBeInTheDocument();
    expect(screen.getByText("Torque")).toBeInTheDocument();
    expect(screen.getByText("code")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Manuals/i }));
    expect(onAction).toHaveBeenCalledWith(message.actions![0]);

    fireEvent.click(screen.getByRole("button", { name: /Specs/i }));
    expect(onAction).toHaveBeenCalledWith(message.actions![1]);
  });
});
