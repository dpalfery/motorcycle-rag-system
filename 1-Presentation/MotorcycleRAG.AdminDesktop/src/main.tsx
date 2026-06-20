import React, { Component, type ReactNode } from "react";
import ReactDOM from "react-dom/client";
import { HashRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import { setApiBaseUrl, setTokenProvider } from "./lib/apiClient";
import { useConfig } from "./lib/config";
import { getAccessToken } from "./lib/auth";
import "./index.css";

class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  constructor(props: { children: ReactNode }) {
    super(props);
    this.state = { error: null };
  }
  static getDerivedStateFromError(error: Error) {
    return { error };
  }
  render() {
    if (this.state.error) {
      return (
        <div style={{ padding: 20, color: "#ff6b6b", fontFamily: "sans-serif" }}>
          <h1 style={{ fontSize: "1.2rem", fontWeight: "bold" }}>Component Crash</h1>
          <pre style={{ marginTop: 10, fontSize: "0.8rem", whiteSpace: "pre-wrap" }}>
            {this.state.error.stack}
          </pre>
        </div>
      );
    }
    return this.props.children;
  }
}

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});

/**
 * Safely renders a boot-level error to the root element.
 * Avoids innerHTML to prevent injection sinks.
 */
function renderBootError(title: string, message: string) {
  const root = document.getElementById("root");
  if (!root) return;
  root.textContent = "";

  const container = document.createElement("div");
  container.setAttribute("style", "padding: 20px; color: #ff6b6b; font-family: sans-serif;");

  const h1 = document.createElement("h1");
  h1.setAttribute("style", "font-size: 1.2rem; font-weight: bold;");
  h1.textContent = title;

  const pre = document.createElement("pre");
  pre.setAttribute("style", "margin-top: 10px; font-size: 0.8rem; white-space: pre-wrap;");
  pre.textContent = message;

  container.appendChild(h1);
  container.appendChild(pre);
  root.appendChild(container);
}

// Setup global error handling for boot diagnosis.
window.onerror = (msg, _url, line, col, _error) => {
  renderBootError("Boot Error", `${msg}\nat ${line}:${col}`);
  return false;
};

// Wire the cloud API client to current config + the auth token provider.
setTokenProvider(getAccessToken);
void useConfig
  .getState()
  .load()
  .then(() => setApiBaseUrl(useConfig.getState().config.apiBaseUrl))
  .catch((err) => {
    // This should usually be handled by load() itself now, but top-level catch prevents white-screen hangs.
    console.error("Configuration boot failure:", err);
  });
useConfig.subscribe((s) => setApiBaseUrl(s.config.apiBaseUrl));

try {
  const rootElement = document.getElementById("root");
  if (!rootElement) throw new Error("Root element not found");

  ReactDOM.createRoot(rootElement).render(
    <React.StrictMode>
      <ErrorBoundary>
        <QueryClientProvider client={queryClient}>
          <HashRouter>
            <App />
          </HashRouter>
        </QueryClientProvider>
      </ErrorBoundary>
    </React.StrictMode>,
  );
} catch (err) {
  console.error("React render failure:", err);
  renderBootError(
    "Render Failure",
    err instanceof Error ? err.stack || err.message : String(err)
  );
}
