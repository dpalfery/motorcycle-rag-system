/// Redacts known secrets and secret-shaped values from processor child stderr
/// before the text is returned to the UI.
pub fn redact_processor_stderr(text: &str, known_secrets: &[&str]) -> String {
    let mut out = text.to_string();

    for secret in known_secrets {
        if secret.len() >= 8 {
            out = out.replace(*secret, "[REDACTED]");
        }
    }

    out = redact_labeled_assignment(&out, "MCR_LOCAL_PROCESSOR_CONTROL_TOKEN");
    out = redact_labeled_assignment(&out, "PYTHON_UPLOAD_JOB_SECRET");
    out = redact_bearer_tokens(&out);
    out = redact_secret_shaped_tokens(&out);
    out
}

fn redact_labeled_assignment(text: &str, label: &str) -> String {
    let mut result = String::with_capacity(text.len());
    let mut remainder = text;

    while let Some(index) = find_ignore_ascii_case(remainder, label) {
        result.push_str(&remainder[..index]);
        result.push_str(label);

        let after_label = &remainder[index + label.len()..];
        let trimmed = after_label.trim_start();
        let whitespace_len = after_label.len() - trimmed.len();
        result.push_str(&after_label[..whitespace_len]);

        if let Some(separator) = trimmed.chars().next() {
            if separator == '=' || separator == ':' {
                result.push(separator);
                let after_separator = &trimmed[separator.len_utf8()..];
                let value_trimmed = after_separator.trim_start();
                let value_ws = after_separator.len() - value_trimmed.len();
                result.push_str(&after_separator[..value_ws]);
                result.push_str("[REDACTED]");
                remainder = skip_token(value_trimmed);
                continue;
            }
        }

        remainder = after_label;
    }

    result.push_str(remainder);
    result
}

fn redact_bearer_tokens(text: &str) -> String {
    let mut result = String::with_capacity(text.len());
    let mut remainder = text;

    while let Some(index) = find_ignore_ascii_case(remainder, "Bearer ") {
        result.push_str(&remainder[..index]);
        result.push_str("Bearer [REDACTED]");
        remainder = skip_token(&remainder[index + "Bearer ".len()..]);
    }

    result.push_str(remainder);
    result
}

/// Redacts standalone URL-safe base64-looking tokens (typical control-token length).
fn redact_secret_shaped_tokens(text: &str) -> String {
    let mut result = String::with_capacity(text.len());
    let mut chars = text.char_indices().peekable();

    while let Some((start, ch)) = chars.next() {
        if is_url_safe_base64_char(ch) {
            let mut end = start + ch.len_utf8();
            while let Some(&(next_start, next_ch)) = chars.peek() {
                if is_url_safe_base64_char(next_ch) {
                    end = next_start + next_ch.len_utf8();
                    chars.next();
                } else {
                    break;
                }
            }
            let token = &text[start..end];
            if (40..=64).contains(&token.len()) {
                result.push_str("[REDACTED]");
            } else {
                result.push_str(token);
            }
        } else {
            result.push(ch);
        }
    }

    result
}

fn find_ignore_ascii_case(haystack: &str, needle: &str) -> Option<usize> {
    haystack
        .as_bytes()
        .windows(needle.len())
        .position(|window| window.eq_ignore_ascii_case(needle.as_bytes()))
}

fn skip_token(text: &str) -> &str {
    let end = text
        .char_indices()
        .find(|(_, ch)| ch.is_whitespace())
        .map(|(index, _)| index)
        .unwrap_or(text.len());
    &text[end..]
}

fn is_url_safe_base64_char(ch: char) -> bool {
    ch.is_ascii_alphanumeric() || ch == '-' || ch == '_'
}

#[cfg(test)]
mod tests {
    use super::redact_processor_stderr;

    #[test]
    fn redact_processor_stderr_replaces_known_secrets() {
        let control = "control-token-value-abcdefghijklmnop";
        let upload = "upload-job-secret-value-xyz";
        let input = format!("token={control} secret={upload}");

        let redacted = redact_processor_stderr(&input, &[control, upload]);

        assert!(redacted.contains("[REDACTED]"));
        assert!(!redacted.contains(control));
        assert!(!redacted.contains(upload));
    }

    #[test]
    fn redact_processor_stderr_redacts_labeled_control_token_without_known_value() {
        let input =
            "MCR_LOCAL_PROCESSOR_CONTROL_TOKEN=AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_ab";

        let redacted = redact_processor_stderr(input, &[]);

        assert!(redacted.contains("MCR_LOCAL_PROCESSOR_CONTROL_TOKEN=[REDACTED]"));
        assert!(!redacted.contains("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_ab"));
    }

    #[test]
    fn redact_processor_stderr_redacts_labeled_upload_secret_with_colon() {
        let input = "PYTHON_UPLOAD_JOB_SECRET: super-secret-upload-value";

        let redacted = redact_processor_stderr(input, &[]);

        assert!(redacted.contains("PYTHON_UPLOAD_JOB_SECRET: [REDACTED]"));
        assert!(!redacted.contains("super-secret-upload-value"));
    }

    #[test]
    fn redact_processor_stderr_redacts_bearer_tokens() {
        let input = "Authorization: Bearer AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_token";

        let redacted = redact_processor_stderr(input, &[]);

        assert!(redacted.contains("Bearer [REDACTED]"));
        assert!(!redacted.contains("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_token"));
    }

    #[test]
    fn redact_processor_stderr_redacts_standalone_secret_shaped_tokens() {
        let token_40 = "a".repeat(40);
        let token_64 = "b".repeat(64);
        let short = "c".repeat(39);
        let long = "d".repeat(65);
        let input = format!("{token_40} {token_64} {short} {long}");

        let redacted = redact_processor_stderr(&input, &[]);

        assert!(!redacted.contains(&token_40));
        assert!(!redacted.contains(&token_64));
        assert!(redacted.contains(&short));
        assert!(redacted.contains(&long));
        assert_eq!(redacted.matches("[REDACTED]").count(), 2);
    }

    #[test]
    fn redact_processor_stderr_preserves_non_secret_diagnostic_text() {
        let input = "Embedding discovery failed: connection refused to LM Studio";

        let redacted = redact_processor_stderr(input, &[]);

        assert_eq!(redacted, input);
    }

    #[test]
    fn redact_processor_stderr_ignores_known_secrets_shorter_than_eight_chars() {
        let input = "short=abc1234 remains";

        let redacted = redact_processor_stderr(input, &["abc1234"]);

        assert_eq!(redacted, input);
    }
}
