//! RED contract: `Cargo.lock` must resolve `quinn-proto` to ≥0.11.15 (GHSA-4w2j-m93h-cj5j).
//! GREEN when T7 bumps the lock; this test fails while the lock still pins 0.11.14.

use std::fs;
use std::path::PathBuf;

const PACKAGE_NAME: &str = "quinn-proto";
const MINIMUM_VERSION: (u64, u64, u64) = (0, 11, 15);

#[test]
fn quinn_proto_version_in_cargo_lock_is_at_least_0_11_15() {
    let lock_path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("Cargo.lock");
    let lock = fs::read_to_string(&lock_path)
        .unwrap_or_else(|e| panic!("failed to read {}: {e}", lock_path.display()));

    let version = package_version_from_cargo_lock(&lock, PACKAGE_NAME).unwrap_or_else(|| {
        panic!(
            "{PACKAGE_NAME} not found in {}; expected a [[package]] entry",
            lock_path.display()
        )
    });

    let parsed = parse_semver_triple(&version).unwrap_or_else(|| {
        panic!("could not parse {PACKAGE_NAME} version '{version}' as major.minor.patch")
    });

    assert!(
        parsed >= MINIMUM_VERSION,
        "{PACKAGE_NAME} in Cargo.lock is {version}; required ≥{}.{}.{} (GHSA-4w2j-m93h-cj5j)",
        MINIMUM_VERSION.0,
        MINIMUM_VERSION.1,
        MINIMUM_VERSION.2
    );
}

/// Returns the `version` field for the first `[[package]]` whose `name` matches.
fn package_version_from_cargo_lock(lock: &str, package_name: &str) -> Option<String> {
    let mut current_name: Option<&str> = None;
    let mut current_version: Option<&str> = None;

    for line in lock.lines() {
        let trimmed = line.trim();
        if trimmed == "[[package]]" {
            if current_name == Some(package_name) {
                return current_version.map(str::to_owned);
            }
            current_name = None;
            current_version = None;
            continue;
        }

        if let Some(value) = quoted_toml_string_value(trimmed, "name") {
            current_name = Some(value);
        } else if let Some(value) = quoted_toml_string_value(trimmed, "version") {
            current_version = Some(value);
        }
    }

    if current_name == Some(package_name) {
        return current_version.map(str::to_owned);
    }
    None
}

fn quoted_toml_string_value<'a>(line: &'a str, key: &str) -> Option<&'a str> {
    let prefix = format!("{key} = \"");
    let rest = line.strip_prefix(&prefix)?;
    let end = rest.find('"')?;
    Some(&rest[..end])
}

fn parse_semver_triple(version: &str) -> Option<(u64, u64, u64)> {
    let core = version.split(['+', '-']).next().unwrap_or(version);
    let mut parts = core.split('.');
    let major = parts.next()?.parse().ok()?;
    let minor = parts.next()?.parse().ok()?;
    let patch = parts.next()?.parse().ok()?;
    Some((major, minor, patch))
}
