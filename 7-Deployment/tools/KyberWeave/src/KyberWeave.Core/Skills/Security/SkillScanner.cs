using System.Text.RegularExpressions;
using KyberWeave.Core.Diagnostics;
using KyberWeave.Core.Skills.Model;

namespace KyberWeave.Core.Skills.Security;

/// <summary>
/// Scans a skill as an untrusted artifact. Per Microsoft, Anthropic and GitHub guidance,
/// a skill (SKILL.md + bundled scripts) is a trust surface to be reviewed like third-party
/// code. These are heuristics — necessary but NOT sufficient. Treat the scanner as a gate
/// that raises the bar, paired with human review and (optionally) a semantic pass.
/// </summary>
public sealed class SkillScanner
{
    private static readonly (string Code, Regex Rx, string Label)[] InjectionPatterns =
    {
        ("KW-SKILL-SEC-001", new(@"ignore\s+(all\s+)?(previous|prior|above)\s+instructions", RegexOptions.IgnoreCase | RegexOptions.Compiled), "prompt-injection: 'ignore previous instructions'"),
        ("KW-SKILL-SEC-002", new(@"disregard\s+(your|all|the)\s+(guidelines|rules|instructions|policy|policies)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "prompt-injection: 'disregard your guidelines'"),
        ("KW-SKILL-SEC-003", new(@"\b(system\s+override|override\s+(the\s+)?system\s+prompt|reveal\s+(your\s+)?system\s+prompt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "prompt-injection: system-prompt override/exfiltration"),
        ("KW-SKILL-SEC-004", new(@"you\s+are\s+now\s+(a|an|the)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "persona hijack: 'you are now …'"),
        ("KW-SKILL-SEC-005", new(@"\b(exfiltrat|send\s+(the\s+)?(api\s+key|secret|token|credential|password)s?\s+to|forward\s+.*\s+to\s+https?://)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "data exfiltration verb"),
    };

    private static readonly Regex HtmlComment = new(@"<!--(.*?)-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly (string Code, Regex Rx, string Label)[] ScriptPatterns =
    {
        ("KW-SKILL-SEC-010", new(@"curl\s+[^\n|]*\|\s*(ba)?sh", RegexOptions.IgnoreCase | RegexOptions.Compiled), "remote code execution: 'curl … | sh'"),
        ("KW-SKILL-SEC-011", new(@"wget\s+[^\n|]*\|\s*(ba)?sh", RegexOptions.IgnoreCase | RegexOptions.Compiled), "remote code execution: 'wget … | sh'"),
        ("KW-SKILL-SEC-012", new(@"eval\s*\(?\s*\$?\(?\s*.*base64\s+(--?d|--decode)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "obfuscated execution: eval of base64-decoded content"),
        ("KW-SKILL-SEC-013", new(@"\b(rm\s+-rf\s+[~/]|:\(\)\s*\{\s*:\|:&\s*\};:)", RegexOptions.Compiled), "destructive command"),
    };

    private static readonly (string Code, Regex Rx, string Label)[] SecretPatterns =
    {
        ("KW-SKILL-SEC-020", new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled), "hardcoded AWS access key id"),
        ("KW-SKILL-SEC-021", new(@"gh[pousr]_[A-Za-z0-9]{36,}", RegexOptions.Compiled), "hardcoded GitHub token"),
        ("KW-SKILL-SEC-022", new(@"-----BEGIN\s+(RSA|EC|OPENSSH|PGP|DSA)?\s*PRIVATE KEY-----", RegexOptions.Compiled), "embedded private key"),
        ("KW-SKILL-SEC-023", new(@"xox[baprs]-[A-Za-z0-9-]{10,}", RegexOptions.Compiled), "hardcoded Slack token"),
    };

    // base64 blob of meaningful length
    private static readonly Regex Base64Blob = new(@"[A-Za-z0-9+/]{120,}={0,2}", RegexOptions.Compiled);

    public IEnumerable<Diagnostic> Scan(Skill skill)
    {
        var id = skill.Frontmatter.Name ?? skill.DirectoryName;
        var file = skill.SkillFilePath;
        var body = skill.InstructionsBody;

        // 1. Instruction-layer prompt injection in the body
        foreach (var (code, rx, label) in InjectionPatterns)
            if (rx.IsMatch(body))
                yield return new Diagnostic(code, Severity.Critical,
                    $"Instruction body contains a {label}. Review carefully before trusting this skill.", id, file);

        // 2. Hidden HTML comments carrying instructions (invisible to a human skimming rendered MD)
        foreach (Match m in HtmlComment.Matches(body))
        {
            var inner = m.Groups[1].Value;
            if (Regex.IsMatch(inner, @"\b(ignore|disregard|system|instruction|tool|exfiltrat|send|password|token|secret)\b", RegexOptions.IgnoreCase))
                yield return new Diagnostic("KW-SKILL-SEC-006", Severity.Critical,
                    "Hidden HTML comment in the body contains instruction-like text. Hidden directives are a classic skill-injection vector.",
                    id, file, $"Comment: \"{Truncate(inner.Trim(), 80)}\"");
        }

        // 3. Suspicious base64 blobs in the body
        foreach (Match m in Base64Blob.Matches(body))
        {
            yield return new Diagnostic("KW-SKILL-SEC-007", Severity.Warning,
                "Body contains a long base64-like blob. Encoded payloads can hide instructions or data; confirm what it decodes to.",
                id, file, $"Starts: {Truncate(m.Value, 24)}…");
            break; // one finding per skill is enough to flag for review
        }

        // 4. Scan bundled scripts
        foreach (var script in skill.Scripts)
        {
            string text;
            try { text = File.ReadAllText(script.AbsolutePath); }
            catch { continue; }

            foreach (var (code, rx, label) in ScriptPatterns)
                if (rx.IsMatch(text))
                    yield return new Diagnostic(code, Severity.Critical,
                        $"Script '{script.RelativePath}' contains a {label}.", id, script.AbsolutePath);

            foreach (var (code, rx, label) in SecretPatterns)
                if (rx.IsMatch(text))
                    yield return new Diagnostic(code, Severity.Critical,
                        $"Script '{script.RelativePath}' contains a {label}.", id, script.AbsolutePath);
        }

        // Secrets directly in the SKILL.md too
        foreach (var (code, rx, label) in SecretPatterns)
            if (rx.IsMatch(body))
                yield return new Diagnostic(code, Severity.Critical,
                    $"SKILL.md body contains a {label}.", id, file);

        // 5. Provenance / governance metadata for audit trails
        var meta = skill.Frontmatter.Metadata;
        if (meta is null || !meta.ContainsKey("author"))
            yield return new Diagnostic("KW-SKILL-SEC-030", Severity.Info,
                "No 'metadata.author' for provenance. Enterprise governance benefits from a clear owner on every skill.", id, file);

        if (meta is null || !meta.ContainsKey("version"))
            yield return new Diagnostic("KW-SKILL-SEC-031", Severity.Info,
                "No 'metadata.version' for provenance. Versioning skills makes review and rollback auditable.", id, file);

        if (string.IsNullOrWhiteSpace(skill.Frontmatter.License))
            yield return new Diagnostic("KW-SKILL-SEC-032", Severity.Info,
                "No 'license' declared. Declare a license, especially for shared or community skills.", id, file);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
