using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Reliability",
    "CA2234:Pass System.Uri objects instead of strings",
    Justification = "Integration tests intentionally use relative path strings for readability; WebApplicationFactory sets BaseAddress, so string overloads are safe.",
    Scope = "module")]

[assembly: SuppressMessage(
    "SonarAnalyzer.CSharp",
    "S4005:Do not use string overloads for HttpClient methods",
    Justification = "Integration tests use relative URLs with the in-memory test server; string overloads keep tests concise and safe in this context.",
    Scope = "module")]

[assembly: SuppressMessage(
    "Microsoft.CodeAnalysis",
    "CA1506:Avoid excessive class coupling",
    Justification = "Test fixtures intentionally aggregate many dependencies to mirror production wiring; refactoring would reduce coverage clarity without production impact.",
    Scope = "module")]

[assembly: SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "Tests use fixed-seed Random solely for deterministic embedding stubs; no security impact.",
    Scope = "module")]
