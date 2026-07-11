import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import SettingsScreen from "./SettingsScreen";
import { DEFAULT_CONFIG, useConfig } from "@/lib/config";

describe("SettingsScreen", () => {
  const save = vi.fn();

  beforeEach(() => {
    save.mockReset();
    save.mockResolvedValue(undefined);
    useConfig.setState({
      config: { ...DEFAULT_CONFIG },
      loaded: true,
      save,
    });
  });

  afterEach(cleanup);

  it("renders all configuration sections and current values", () => {
    render(<SettingsScreen />);

    expect(screen.getByText("Cloud API")).toBeInTheDocument();
    expect(screen.getByText("Authentication (Microsoft Entra)")).toBeInTheDocument();
    expect(screen.getByText("Embedding provider")).toBeInTheDocument();
    expect(screen.getByText("Graph extraction")).toBeInTheDocument();
    expect(screen.getByText("Local processor")).toBeInTheDocument();
    expect(screen.getByLabelText("API base URL")).toHaveValue(DEFAULT_CONFIG.apiBaseUrl);
    expect(screen.getByLabelText("Auto-resolve location")).toBeChecked();
  });

  it("updates every editable field and saves the complete draft", async () => {
    render(<SettingsScreen />);

    const changes: Record<string, string> = {
      "API base URL": "https://api.example.com",
      "Blob storage account URL": "https://storage.example.com",
      Authority: "https://login.example.com/tenant",
      "Client ID": "client-2",
      Scope: "api://scope-2",
      "Upload job secret (M2M)": "secret",
      Model: "embedding-2",
      "Tokenizer model path": "/models/tokenizer",
      "Working directory override": "/repo/processor",
      Port: "9000",
    };

    for (const [label, value] of Object.entries(changes)) {
      const matches = screen.getAllByLabelText(label);
      fireEvent.change(matches[0], { target: { value } });
    }
    const endpoints = screen.getAllByLabelText("Endpoint");
    fireEvent.change(endpoints[0], { target: { value: "http://embed.example/v1" } });
    fireEvent.change(endpoints[1], { target: { value: "http://graph.example/v1" } });
    const models = screen.getAllByLabelText("Model");
    fireEvent.change(models[1], { target: { value: "graph-2" } });
    fireEvent.click(screen.getByLabelText("Auto-resolve location"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(save).toHaveBeenCalledOnce());
    expect(save.mock.calls[0][0]).toMatchObject({
      apiBaseUrl: "https://api.example.com",
      azureStorageAccountUrl: "https://storage.example.com",
      authAuthority: "https://login.example.com/tenant",
      authClientId: "client-2",
      authScope: "api://scope-2",
      pythonUploadJobSecret: "secret",
      embeddingProviderEndpoint: "http://embed.example/v1",
      embeddingModel: "embedding-2",
      tokenizerModelPath: "/models/tokenizer",
      graphExtractionEndpoint: "http://graph.example/v1",
      graphExtractionModel: "graph-2",
      localProcessorWorkingDir: "/repo/processor",
      localProcessorPort: 9000,
      autoResolveProcessor: false,
    });
    expect(await screen.findByRole("button", { name: "Saved" })).toBeInTheDocument();
  });

  it("rejects an invalid API URL and restores Save after editing", async () => {
    render(<SettingsScreen />);
    const apiUrl = screen.getByLabelText("API base URL");

    fireEvent.change(apiUrl, { target: { value: "not-a-url" } });
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();

    fireEvent.change(apiUrl, { target: { value: "" } });
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByRole("button", { name: "Saved" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Client ID"), {
      target: { value: "changed-again" },
    });
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });

  it("falls back to port 8100 for a non-numeric value and syncs store changes", () => {
    const { rerender } = render(<SettingsScreen />);
    fireEvent.change(screen.getByLabelText("Port"), { target: { value: "invalid" } });
    expect(screen.getByLabelText("Port")).toHaveValue("8100");

    act(() => {
      useConfig.setState({
        config: { ...DEFAULT_CONFIG, apiBaseUrl: "https://new.example.com" },
      });
    });
    rerender(<SettingsScreen />);
    expect(screen.getByLabelText("API base URL")).toHaveValue("https://new.example.com");
  });
});
