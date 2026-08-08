# Taste

- Prefers disabling/unconfiguring tools (e.g., MCP servers) in the shared, repo-level config file (`.mcp.json`) so the change applies to the whole project and is checked in — rather than using session-only toggles or personal/local scope overrides. Distinguishes "disable" from "delete": disabling should preserve the config entry and flip it off (e.g., `"disabled": true`) rather than removing the block entirely. Confidence: 0.8
- Prefers to keep system context/token overhead minimal and proactively seeks to shrink it when high (e.g., flagged 13k in system tools as too large and asked how to reduce it). Confidence: 0.68
- Prefers local LLM inference via LM Studio (OpenAI-compatible API on localhost:1234) and wants it wired as a custom provider for Pi coding agent, specifically using qwen/qwen3.6-35b-a3b as the selected model. Confidence: 0.88
