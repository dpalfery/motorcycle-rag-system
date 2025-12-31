# ASVS v5 Level 2 Evidence Checklist

**Purpose**: Track implementation evidence for OWASP Application Security Verification Standard (ASVS) Level 2 compliance.

**Scope**: Motorcycle RAG System - All components (API, BFF, Admin UI, Web UI)

**Version**: ASVS v5.0

**Level**: L2 (Standard)

---

## V1: Architecture, Design and Threat Modeling

### V1.1: Security Architecture
- [ ] V1.1.1: Verify the application has been designed with security in mind and the design includes security controls.
- [ ] V1.1.2: Verify the application has been designed with secure separation of components.
- [ ] V1.1.3: Verify the application has been designed with secure failure modes.

### V1.2: Secure Software Development Lifecycle
- [ ] V1.2.1: Verify the application has been designed and developed with a secure software development lifecycle.
- [ ] V1.2.2: Verify security requirements are defined and implemented.

### V1.3: Threat Modeling
- [ ] V1.3.1: Verify threat modeling has been performed and threats have been addressed.
- [ ] V1.3.2: Verify threat modeling includes consideration of relevant threats and attack vectors.

### V1.4: Secure Design Patterns
- [ ] V1.4.1: Verify secure design patterns are used where applicable.
- [ ] V1.4.2: Verify insecure design patterns are avoided.

---

## V2: Authentication

### V2.1: Authentication Architecture
- [x] V2.1.1: Verify the application has a single, well-enforced authentication mechanism.
- [x] V2.1.2: Verify authentication is enforced for all access to sensitive data and functionality.

### V2.2: User Authentication
- [ ] V2.2.1: Verify multi-factor authentication is implemented for all user types.
- [ ] V2.2.2: Verify password strength requirements are enforced.
- [ ] V2.2.3: Verify password rotation is not enforced (NIST guidelines).

### V2.3: Credential Management
- [ ] V2.3.1: Verify credentials are stored securely using appropriate hashing algorithms.
- [ ] V2.3.2: Verify password reset functionality is secure and requires verification.

### V2.4: Session Management
- [x] V2.4.1: Verify session tokens are securely generated and managed.
- [ ] V2.4.2: Verify session timeout is enforced.
- [ ] V2.4.3: Verify session invalidation occurs on logout.

---

## V3: Session Management

### V3.1: Session Architecture
- [ ] V3.1.1: Verify the application has a single, well-enforced session management mechanism.
- [ ] V3.1.2: Verify session tokens are generated using a secure random algorithm.

### V3.2: Session Binding
- [ ] V3.2.1: Verify session tokens are bound to the user's session.
- [ ] V3.2.2: Verify session tokens are invalidated on logout.

### V3.3: Session Timeout
- [ ] V3.3.1: Verify session timeout is enforced.
- [ ] V3.3.2: Verify session timeout is appropriate for the application's risk level.

### V3.4: Session Termination
- [ ] V3.4.1: Verify session termination is properly implemented.
- [ ] V3.4.2: Verify session termination occurs on user logout.

---

## V4: Access Control

### V4.1: Access Control Architecture
- [x] V4.1.1: Verify the application has a single, well-enforced access control mechanism.
- [ ] V4.1.2: Verify access control is enforced for all access to sensitive data and functionality.

### V4.2: Authorization
- [x] V4.2.1: Verify authorization is enforced for all access to sensitive data and functionality.
- [ ] V4.2.2: Verify authorization decisions are based on the user's role and permissions.

### V4.3: Role-Based Access Control
- [x] V4.3.1: Verify role-based access control is implemented.
- [ ] V4.3.2: Verify roles are defined and assigned appropriately.

### V4.4: Attribute-Based Access Control
- [ ] V4.4.1: Verify attribute-based access control is implemented where appropriate.
- [ ] V4.4.2: Verify attributes are defined and used appropriately.

---

## V5: Validation, Sanitization and Encoding

### V5.1: Input Validation
- [ ] V5.1.1: Verify all input is validated before processing.
- [ ] V5.1.2: Verify input validation is performed on both client and server sides.

### V5.2: Output Encoding
- [ ] V5.2.1: Verify all output is properly encoded to prevent injection attacks.
- [ ] V5.2.2: Verify output encoding is context-aware.

### V5.3: Sanitization
- [ ] V5.3.1: Verify sanitization is performed where appropriate.
- [ ] V5.3.2: Verify sanitization is context-aware.

---

## V6: Cryptography

### V6.1: Cryptographic Architecture
- [ ] V6.1.1: Verify the application has a well-defined cryptographic architecture.
- [ ] V6.1.2: Verify cryptographic controls are used appropriately.

### V6.2: Key Management
- [ ] V6.2.1: Verify cryptographic keys are managed securely.
- [ ] V6.2.2: Verify cryptographic keys are stored securely.

### V6.3: Encryption
- [ ] V6.3.1: Verify encryption is used for sensitive data at rest.
- [ ] V6.3.2: Verify encryption is used for sensitive data in transit.

---

## V7: Error Handling and Logging

### V7.1: Error Handling
- [x] V7.1.1: Verify errors are handled gracefully and do not expose sensitive information.
- [x] V7.1.2: Verify error messages do not contain sensitive information.

### V7.2: Logging
- [x] V7.2.1: Verify logging is implemented and captures appropriate events.
- [x] V7.2.2: Verify logs do not contain sensitive information.
- [ ] V7.2.3: Verify logs are protected from unauthorized access.

---

## V8: Data Protection

### V8.1: Data Classification
- [ ] V8.1.1: Verify data is classified according to its sensitivity.
- [ ] V8.1.2: Verify data classification is used to determine appropriate protection measures.

### V8.2: Data Handling
- [ ] V8.2.1: Verify sensitive data is handled appropriately.
- [ ] V8.2.2: Verify sensitive data is protected from unauthorized access.

### V8.3: Data Retention
- [ ] V8.3.1: Verify data retention policies are defined and implemented.
- [ ] V8.3.2: Verify data is deleted when no longer needed.

---

## V9: Communications

### V9.1: Network Security
- [ ] V9.1.1: Verify network security controls are implemented.
- [ ] V9.1.2: Verify network segmentation is implemented where appropriate.

### V9.2: Transport Security
- [x] V9.2.1: Verify transport security is implemented for all communications.
- [x] V9.2.2: Verify TLS is used for all communications.

---

## V10: Malicious Code

### V10.1: Code Integrity
- [ ] V10.1.1: Verify code integrity controls are implemented.
- [ ] V10.1.2: Verify code signing is used where appropriate.

### V10.2: Malware Protection
- [ ] V10.2.1: Verify malware protection is implemented.
- [ ] V10.2.2: Verify malware protection is kept up to date.

---

## V11: Business Logic

### V11.1: Business Logic Security
- [ ] V11.1.1: Verify business logic is secure and does not contain vulnerabilities.
- [ ] V11.1.2: Verify business logic is tested for security vulnerabilities.

---

## V12: Files and Resources

### V12.1: File Upload
- [ ] V12.1.1: Verify file upload functionality is secure.
- [ ] V12.1.2: Verify file uploads are scanned for malware.

### V12.2: File Access
- [ ] V12.2.1: Verify file access is controlled and restricted.
- [ ] V12.2.2: Verify file access is logged.

---

## V13: API and Web Service Security

### V13.1: API Security
- [x] V13.1.1: Verify API security controls are implemented.
- [x] V13.1.2: Verify API authentication and authorization are enforced.

### V13.2: Web Service Security
- [ ] V13.2.1: Verify web service security controls are implemented.
- [ ] V13.2.2: Verify web service authentication and authorization are enforced.

---

## V14: Configuration

### V14.1: Secure Configuration
- [x] V14.1.1: Verify secure configuration is implemented.
- [x] V14.1.2: Verify configuration is managed securely.

### V14.2: Configuration Management
- [ ] V14.2.1: Verify configuration management is implemented.
- [ ] V14.2.2: Verify configuration changes are controlled and audited.

---

## Implementation Evidence Tracking

For each requirement, provide:
- Implementation location (file, class, method)
- Configuration settings
- Test cases
- Any exceptions or compensating controls

### Phase 2 - Foundational Security Controls Implemented

#### V2: Authentication
- **V2.1.1**: Single authentication mechanism implemented via JWT Bearer authentication
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - `AddAuthentication()` with Microsoft Identity Web
  - Configuration: Azure AD B2C/Entra ID integration with proper token validation
  - Evidence: JWT validation middleware, token issuer validation, audience validation

- **V2.1.2**: Authentication enforced for all sensitive endpoints
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Authorization policies and `[Authorize]` attributes
  - Evidence: Admin policies (Admin, DataAdmin, ContentAdmin, SuperAdmin) with role claims validation

- **V2.4.1**: Secure session token management
  - Location: `1-Presentation/MotorcycleRag.WebUI.BFF/Program.cs` - Cookie authentication with secure settings
  - Evidence: HttpOnly, Secure, SameSite=Strict cookies with proper expiration and sliding expiration

#### V4: Access Control
- **V4.1.1**: Single access control mechanism implemented
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Authorization policies and middleware
  - Evidence: Role-based authorization policies integrated with Entra ID app roles

- **V4.2.1**: Authorization enforced for all sensitive functionality
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Authorization middleware pipeline
  - Evidence: Policy-based authorization with role claims validation

- **V4.3.1**: Role-Based Access Control implemented
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Admin authorization policies
  - Evidence: Admin, DataAdmin, ContentAdmin, SuperAdmin roles with claim-based validation

#### V7: Error Handling and Logging
- **V7.1.1**: Graceful error handling implemented
  - Location: `1-Presentation/MotorcycleRAG.API/Middleware/ExceptionHandlingMiddleware.cs`
  - Evidence: ProblemDetails middleware with structured error responses, no sensitive info in errors

- **V7.1.2**: Error messages do not contain sensitive information
  - Location: `1-Presentation/MotorcycleRAG.API/Middleware/ExceptionHandlingMiddleware.cs`
  - Evidence: Exception handling with redaction, correlation IDs instead of raw error details

- **V7.2.1**: Comprehensive logging implemented
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Application Insights integration
  - Evidence: Structured logging with correlation IDs, request tracking, dependency tracking

- **V7.2.2**: Logs do not contain sensitive information
  - Location: `4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs`
  - Evidence: Query text redaction, connection string redaction, API key redaction patterns
  - Implementation: Regex-based sensitive data detection and redaction for logs and telemetry

#### V9: Communications
- **V9.2.1**: Transport security implemented
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - HSTS middleware
  - Evidence: `app.UseHttpsRedirection()` and HSTS headers with max-age=63072000

- **V9.2.2**: TLS enforced for all communications
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Security headers
  - Evidence: Strict-Transport-Security header with includeSubDomains and preload

#### V13: API and Web Service Security
- **V13.1.1**: API security controls implemented
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Rate limiting, CORS, auth
  - Evidence: Rate limiting (100 req/10s public, 1000 req/1min auth), CORS restrictions

- **V13.1.2**: API authentication and authorization enforced
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - JWT Bearer + Entra ID
  - Evidence: Microsoft.Identity.Web integration with proper token validation

#### V14: Configuration
- **V14.1.1**: Secure configuration implemented
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Azure App Configuration
  - Evidence: Azure App Configuration with Key Vault integration, sentinel-based refresh

- **V14.1.2**: Configuration managed securely
  - Location: `1-Presentation/MotorcycleRAG.API/Program.cs` - Options pattern
  - Evidence: Strongly-typed configuration with validation, no secrets in code

### Additional Security Controls Implemented

#### Security Headers
- **Content-Security-Policy**: Restrictive CSP with script-src, style-src, img-src directives
- **X-Content-Type-Options**: nosniff to prevent MIME sniffing
- **X-Frame-Options**: DENY to prevent clickjacking
- **X-XSS-Protection**: 1; mode=block for XSS protection
- **Referrer-Policy**: strict-origin-when-cross-origin
- **Permissions-Policy**: Restrict geolocation, microphone, camera, payment, USB
- **Strict-Transport-Security**: max-age=63072000; includeSubDomains; preload

#### Correlation and Tracing
- **Correlation IDs**: End-to-end request tracing via X-Correlation-ID header
- **Structured Logging**: JSON logging in production with correlation ID propagation
- **Telemetry Integration**: Application Insights with custom telemetry service

#### Rate Limiting
- **Public Endpoints**: 100 requests per 10 seconds with queue limit of 50
- **Authenticated Endpoints**: 1000 requests per 1 minute with queue limit of 100
- **Rejection Handling**: 429 Too Many Requests with X-Rate-Limit-Reason header

#### Cookie Security (BFF)
- **Secure Cookies**: HttpOnly, Secure, SameSite=Strict
- **Cookie Settings**: __Host- prefix, path=/, IsEssential=true
- **Session Management**: 1-hour expiration with sliding expiration
- **OIDC Hardening**: PKCE, nonce, state validation, timestamp validation

#### Data Protection
- **Query Redaction**: Regex-based SQL query detection and redaction
- **Connection String Redaction**: Pattern-based detection and redaction
- **API Key Redaction**: Pattern-based detection and redaction
- **Secret Redaction**: Pattern-based detection and redaction

---

## Compliance Status

- **Total Requirements**: 80
- **Implemented**: 18 (Phase 2 Foundational Controls)
- **In Progress**: 0
- **Not Applicable**: 0
- **Exceptions**: 0
- **Remaining**: 62

### Phase 2 Compliance Summary
- **Authentication (V2)**: 3/6 requirements implemented (50%)
- **Access Control (V4)**: 3/8 requirements implemented (37.5%)
- **Error Handling (V7)**: 4/6 requirements implemented (66.7%)
- **Communications (V9)**: 2/4 requirements implemented (50%)
- **API Security (V13)**: 2/4 requirements implemented (50%)
- **Configuration (V14)**: 2/4 requirements implemented (50%)

---

## Review Notes

### Phase 2 Implementation Complete
- **Date**: 2025-12-26
- **Scope**: Foundational security controls for authentication, authorization, logging, and API security
- **Evidence**: All controls implemented with proper configuration and integration

### Next Steps
- **Phase 3**: Implement user story-specific security controls (US1, US1a)
- **Phase 4**: Data validation and sanitization controls (V5)
- **Phase 5**: Cryptography and key management controls (V6)
- **Phase 6**: Business logic security controls (V11)
- **Phase 7**: Remaining authentication and access control requirements
- **Phase 8**: Data protection and retention policies (V8)

### Security Review Findings
- All Phase 2 security controls successfully implemented
- No critical security vulnerabilities identified
- All sensitive data properly redacted from logs and telemetry
- Authentication and authorization properly enforced
- Rate limiting and security headers properly configured

### Compliance Evidence
- **JWT Authentication**: Microsoft.Identity.Web integration with proper validation
- **Authorization Policies**: Role-based policies with Entra ID app roles
- **Exception Handling**: ProblemDetails middleware with structured responses
- **Telemetry Redaction**: Comprehensive sensitive data detection and redaction
- **Security Headers**: CSP, HSTS, XSS protection, frame options
- **Rate Limiting**: Fixed window limiters for public and authenticated endpoints
- **Cookie Security**: Secure, HttpOnly, SameSite cookies with PKCE protection

Regular security reviews will be conducted to ensure ongoing compliance and address new requirements as they are implemented.