use std::io::Read;
use std::process::ChildStderr;
use std::sync::{Arc, Mutex};
use std::thread::{self, JoinHandle};

use crate::bounded_byte_tail::BoundedByteTail;

/// Maximum raw stderr bytes retained for start-failure diagnostics.
pub const MAX_PROCESSOR_STDERR_BYTES: usize = 4 * 1024;

/// Background reader that retains a bounded tail of a child process stderr pipe.
pub struct ChildStderrCapture {
    join: Option<JoinHandle<()>>,
    buffer: Arc<Mutex<BoundedByteTail>>,
}

impl ChildStderrCapture {
    pub fn start(stderr: Option<ChildStderr>) -> Self {
        let buffer = Arc::new(Mutex::new(BoundedByteTail::new(MAX_PROCESSOR_STDERR_BYTES)));
        let join = stderr.map(|mut stderr| {
            let buffer = Arc::clone(&buffer);
            thread::spawn(move || {
                let mut chunk = [0_u8; 1024];
                loop {
                    match stderr.read(&mut chunk) {
                        Ok(0) => break,
                        Ok(n) => {
                            if let Ok(mut guard) = buffer.lock() {
                                guard.push(&chunk[..n]);
                            }
                        }
                        Err(_) => break,
                    }
                }
            })
        });

        Self { join, buffer }
    }

    /// Waits for the reader to finish and returns the bounded UTF-8 lossy tail.
    pub fn finish(mut self) -> String {
        if let Some(join) = self.join.take() {
            let _ = join.join();
        }

        self.buffer
            .lock()
            .map(|guard| guard.to_string_lossy())
            .unwrap_or_default()
    }

    /// Keeps draining stderr for a live child so the pipe cannot fill and block.
    ///
    /// Dropping the [`JoinHandle`] detaches the thread; it exits when the child closes stderr.
    pub fn detach(mut self) {
        let _ = self.join.take();
    }
}

/// Appends a redacted stderr tail (or a log-path pointer) to a start-failure message.
pub fn append_processor_stderr_detail(base_error: String, redacted_stderr: &str) -> String {
    let trimmed = redacted_stderr.trim();
    if trimmed.is_empty() {
        format!(
            "{base_error}. No stderr was captured; check logs/local-processor.log under the processor working directory (cwd) for details."
        )
    } else {
        format!("{base_error}. stderr: {trimmed}")
    }
}

#[cfg(test)]
mod tests {
    use super::{append_processor_stderr_detail, ChildStderrCapture, MAX_PROCESSOR_STDERR_BYTES};
    use std::process::{Command, Stdio};

    #[test]
    fn append_processor_stderr_detail_when_stderr_present_includes_tail() {
        let message = append_processor_stderr_detail(
            "processor process exited early with exit status: 1".to_string(),
            "  Embedding discovery failed  ",
        );

        assert!(message.contains("exit status: 1"));
        assert!(message.contains("stderr: Embedding discovery failed"));
    }

    #[test]
    fn append_processor_stderr_detail_when_stderr_empty_points_at_log_path() {
        let message = append_processor_stderr_detail(
            "processor process exited early with exit status: 1".to_string(),
            "   \n\t  ",
        );

        assert!(message.contains("No stderr was captured"));
        assert!(message.contains("logs/local-processor.log"));
        assert!(!message.contains("stderr:"));
    }

    #[test]
    fn child_stderr_capture_finish_returns_child_stderr_text() {
        let mut child = Command::new("python3")
            .args(["-c", "import sys; print('discovery failed', file=sys.stderr); raise SystemExit(1)"])
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()
            .expect("spawn python stderr child");

        let capture = ChildStderrCapture::start(child.stderr.take());
        let status = child.wait().expect("wait for child");
        assert!(!status.success());

        let stderr = capture.finish();
        assert!(stderr.contains("discovery failed"));
    }

    #[test]
    fn child_stderr_capture_finish_retains_only_bounded_tail() {
        let mut child = Command::new("python3")
            .args([
                "-c",
                &format!(
                    "import sys; sys.stderr.write('a' * 100 + 'b' * {}); sys.stderr.flush(); raise SystemExit(1)",
                    MAX_PROCESSOR_STDERR_BYTES
                ),
            ])
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()
            .expect("spawn python stderr flood child");

        let capture = ChildStderrCapture::start(child.stderr.take());
        let _ = child.wait();
        let stderr = capture.finish();

        assert_eq!(stderr.len(), MAX_PROCESSOR_STDERR_BYTES);
        assert!(stderr.chars().all(|ch| ch == 'b'));
    }

    #[test]
    fn child_stderr_capture_detach_drains_pipe_so_child_can_exit() {
        // A child that writes more than a typical pipe buffer must not block forever
        // when the parent detaches the drain thread instead of reading synchronously.
        let mut child = Command::new("python3")
            .args([
                "-c",
                "import sys; sys.stderr.write('x' * (256 * 1024)); sys.stderr.flush()",
            ])
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()
            .expect("spawn python stderr flood child");

        let capture = ChildStderrCapture::start(child.stderr.take());
        capture.detach();

        let status = child
            .wait()
            .expect("detached stderr drain must let the child finish");
        assert!(status.success());
    }

    #[test]
    fn child_stderr_capture_start_when_stderr_missing_finishes_empty() {
        let capture = ChildStderrCapture::start(None);
        assert_eq!(capture.finish(), "");
    }
}
