---
name: snyk-security
description: Run generic security vulnerability scans and code quality checks using Snyk MCP server integration. Works across any project type, language, or framework.
license: MIT
metadata:
  author: David R Palfery
  version: 1.0.0
---

# Snyk Security Scanning

**Goal:** Run automated security scans on any project using Snyk's MCP server tools.

**Trigger:** When you are asked to:
- "Run security scan"
- "Check for vulnerabilities"
- "Audit code for security issues"
- "Scan dependencies"
- "Review infrastructure security"

---

## MCP Server Lifecycle Management

The Snyk MCP server is **disabled by default** to reduce context bloat. This skill follows this lifecycle:

### Step 1: Enable Snyk MCP Server (REQUIRED FIRST STEP)

1. Read `.mcp.json` file
2. Update the `"snyk"` server section: set `"disabled": false`
3. Write the updated configuration back to `.mcp.json`

This enables the Snyk MCP server and makes Snyk tools available for use.

```json
"snyk": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "snyk@latest", "mcp", "-t", "stdio"],
    "disabled": false  // Enable for scanning
}
```

### Step 2: Execute Scans

After enabling the MCP server, proceed with scanning based on the detected project type (see below).

### Step 3: Disable Snyk MCP Server (REQUIRED FINAL STEP)

1. Read `.mcp.json` file
2. Update the `"snyk"` server section: set `"disabled": true`
3. Write the updated configuration back to `/.mcp.json`

This removes Snyk tools from context when not needed.

---

## Project Detection & Scanning

The skill **automatically detects project type** and runs appropriate scans.

### Language/Framework Detection

Scan the current directory for these files to identify the project type:

| Language/Framework | Files to Detect | Scan Type |
|-------------------|----------------|------------|
| **C#/.NET** | `.csproj`, `.sln` | `snyk_sca_scan` |
| **JavaScript/TypeScript** | `package.json`, `yarn.lock`, `package-lock.json` | `snyk_sca_scan` |
| **Python** | `requirements.txt`, `pyproject.toml`, `Pipfile.lock` | `snyk_sca_scan` |
| **Java** | `pom.xml`, `build.gradle` | `snyk_sca_scan` |
| **Go** | `go.mod`, `go.sum` | `snyk_sca_scan` |
| **Ruby** | `Gemfile`, `Gemfile.lock` | `snyk_sca_scan` |
| **Rust** | `Cargo.toml`, `Cargo.lock` | `snyk_sca_scan` |
| **PHP** | `composer.json`, `composer.lock` | `snyk_sca_scan` |

### Infrastructure Detection

| Infrastructure Type | Files to Detect | Scan Type |
|-------------------|----------------|------------|
| **Terraform** | `*.tf`, `*.tf.json` | `snyk_iac_scan` |
| **CloudFormation** | `*.yaml`, `*.yml` (AWS templates) | `snyk_iac_scan` |
| **Pulumi** | `*.ts` (Pulumi projects) | `snyk_iac_scan` |
| **Bicep** | `*.bicep` | `snyk_iac_scan` |
| **Kubernetes** | `*.yaml`, `*.yml` (k8s manifests) | `snyk_iac_scan` |
| **Docker** | `Dockerfile`, `docker-compose.yml` | `snyk_container_scan` |

### Universal Scans (Always Available)

These scans work on **any project** regardless of language:

- **`snyk_code_scan`** (SAST): Scans source code for security vulnerabilities
- **`snyk_sca_scan`** (SCA): Scans package managers for known vulnerabilities in dependencies
- **`snyk_iac_scan`** (IaC): Scans infrastructure-as-code files for security issues
- **`snyk_container_scan`** (Container): Scans Dockerfiles and container images
- **`snyk_sbom_scan`** (SBOM): Analyzes Software Bill of Materials files if present

---

## Universal Scanning Workflow

Follow this process for any project:

### 1. Enable MCP Server
- Read `.mcp.json`
- Update `"snyk.disabled": false`
- Write updated configuration

### 2. Detect Project Type
- Scan current directory for lock files and config files
- Identify primary language(s) and frameworks
- Determine which scan types are relevant

### 3. Execute Appropriate Scans
- **If dependency files found**: Run `snyk_sca_scan` on detected package managers
- **Always run**: `snyk_code_scan` on source code (if source exists)
- **If IaC files found**: Run `snyk_iac_scan`
- **If Dockerfiles found**: Run `snyk_container_scan`
- **If SBOM files found**: Run `snyk_sbom_scan`

### 4. Analyze Results
- Aggregate all scan findings
- Categorize by severity (Critical, High, Medium, Low)
- Provide remediation recommendations

### 5. Disable MCP Server
- Read `.mcp.json`
- Update `"snyk.disabled": true`
- Write updated configuration

### 6. Report Summary
- Vulnerability counts by severity
- Affected packages/files
- Recommended actions

---

## Remediation Guidance

The skill provides **language-agnostic** remediation steps:

### For Dependency Vulnerabilities (SCA)
- Update vulnerable packages to safe versions
- Use the recommended version from Snyk's output
- Run package manager update command (e.g., `npm update`, `dotnet add package`, `pip install --upgrade`)
- Re-scan after updating to verify resolution

### For Code Issues (SAST)
- Fix identified security issues in source code
- Apply patches recommended by Snyk
- Follow OWASP security best practices
- Re-scan code after fixing

### For Infrastructure Issues (IaC)
- Update infrastructure-as-code with secure configurations
- Apply recommended security settings
- Remove deprecated or insecure features
- Re-scan after changes

### For Container Issues
- Update base Docker image to secure version
- Remove vulnerable packages from final image
- Use multi-stage builds to minimize attack surface
- Re-scan container image

### General Process
1. Identify vulnerabilities by severity (fix Critical/High first)
2. Apply recommended fixes
3. Re-scan the affected component
4. Verify all issues are resolved
5. Repeat until no vulnerabilities remain


## Example Commands

When invoked, agents may use prompts like:
- "Run a full security scan on this project"
- "Check for vulnerabilities in dependencies"
- "Scan my Dockerfile for security issues"
- "Audit the infrastructure code"
- "Generate a security report for this repository"

---

