name: "Security-General-Rule"
description: "Enforces fundamental security practices that apply across all development activities in Hotshot Logistics."
when-to-apply: "always" At the start of every task
rule: |

This document is authoritative for security directives; other rule files must reference it for security-related guidance.

## Secrets Management

- Never use a .env file always use environment variabled. if they don't exist ask the user to create one for you
- Never check secrets into source control or store them in plain text.
- Appsettings files are not secure and secrets and passwords should never be stored there.
- database connection strings are secrets and should never be stored in any file. Every for any reason. even as a fall back or generic string. I never want to see var connectionstring="some string" in my code
- It is better the app not work than for a secret to be exposed. Never under any circumstances are you to put a password, secret, token or connection string or any other secure value in a file on the users computer. I mean NEVER!!!!!!!!!

#### **1. Secrets Management (Immediate Actions)**
*   **NEVER** hardcode secrets. Reject any code containing strings like `password=`, `ConnectionString=`, `api_key=`, `token=`, or `secret=` in plain text.
*   **ALWAYS** retrieve secrets from a secure source. In code, this must be represented as a call to:
    *   `Environment.GetEnvironmentVariable("SECRET_NAME")` (or language equivalent).
    *   A secure service like `AzureKeyVault.getSecret("secret-name")`.
*   **VALIDATE** that any configuration file (e.g., `appsettings.json`, `.env`) loaded in code is excluded from version control via `.gitignore`. If you see a secret in a config file in a code block, flag it.

#### **2. Input Validation & Sanitization (For Every User Input)**
*   **ESCAPE ALL INPUTS** contextually before use:
    *   **For SQL:** Use **parameterized queries ONLY**. Never construct queries with string concatenation (`"SELECT ... WHERE id = " + userInput` is forbidden).
    *   **For HTML/UI:** Encode output (e.g., `HtmlEncode()` in C#, `escape()` in Python) before rendering to prevent XSS.
    *   **For OS Commands:** Avoid if possible. If necessary, use APIs that accept arguments as a list, not a single command string.
*   **SANITIZE BEFORE LOGGING:** For any user-provided data going into a log, you MUST:
    *   Replace newlines (`\n`, `\r`) and tabs with spaces.
    *   Use structured logging with placeholders: `logger.LogInfo("User {UserId} logged in", sanitizedUserId)`.
    *   **NEVER** do: `logger.LogInfo("User " + rawUserInput + " logged in")`.

#### **3. Secure Communication & Configuration (Production-Readiness)**
*   **ENFORCE HTTPS:** Any code configuring a web server must:
    *   Redirect HTTP to HTTPS.
    *   Set HSTS headers.

#### **4. Authentication & Authorization (Access Controls)**
*   **PRINCIPLE OF LEAST PRIVILEGE:** When defining roles or permissions, the default must be **no access**. Permissions are explicitly granted.
*   **AUTHORIZE EVERY ACTION:** For any function that accesses data or performs an action, you MUST see an authorization check *after* the authentication check.
    *   Example: `if (user.IsInRole("Admin")) { // allow action }` or `[Authorize(Roles="Admin")]` attribute.

#### **5. Dependency & Operational Security**
*   **FLAG VULNERABLE DEPENDENCIES:** If you generate a dependency file (e.g., `package.json`, `requirements.txt`), include a comment instructing the user to regularly scan for vulnerabilities using `npm audit`, `snyk test`, etc.
*   **IMPLEMENT RATE LIMITING:** Enforce rate limiting on public APIs. Document requirements in code and infrastructure (example comment: `// TODO: enforce rate limiting - 60 reqs/min - use gateway or throttling middleware`). Advise implementers to configure API gateway rules or middleware to prevent abuse.
### **Directives for Code Review & Threat Analysis**

When reviewing code, act as a security auditor. For each function or endpoint, ask these questions:

1.  **Spoofing (Authentication):** Is the user who they claim to be? Is there a clear login/authentication step?
2.  **Tampering (Integrity):** Could an attacker change the data in transit or at rest? Is there input validation? Is HTTPS enforced?
3.  **Repudiation (Logging):** Are there sufficient audit logs? Are logs tamper-resistant? Is user activity logged with a correlation ID instead of raw input?
4.  **Information Disclosure (Secrets/Data):** Could this code leak secrets (e.g., in logs, errors)? Does it enforce authorization before returning sensitive data?
5.  **Denial of Service (Resilience):** Could this be abused to crash the service? Is there resource limiting on expensive operations (file uploads, complex calculations)?
6.  **Elevation of Privilege (Authorization):** Does the code check the user's permissions *every time* it accesses a resource? Can a user access another user's data by changing an ID (Insecure Direct Object Reference)?

### **Incident Response Readiness (Code-Level)**
*   **LOG FOR INCIDENTS:** Ensure logs are structured and include correlation IDs. This is non-negotiable for forensic analysis.
*   **CLEAR ERROR HANDLING:** Code must catch exceptions gracefully without exposing stack traces or internal system details to the end-user.

Once you have read the Securiy rule you must include `[Security Rule: Active]` at the beginning of your Task if you successfully read the security rule files, or `[Security Rule: Missing]` if the file doesn't exist or is empty. If security rule is missing. STOP all further work and warn the user about running unsecurly. Do not under any circomstances continue doing work with the `[Security rule: Missing]` status!. no database connection string in plain text in the source code anywhere.

These rules are not optional and should be followed always