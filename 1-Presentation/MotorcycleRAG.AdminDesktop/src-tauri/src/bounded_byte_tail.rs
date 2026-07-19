/// Fixed-capacity byte buffer that retains only the most recent bytes (a tail).
pub struct BoundedByteTail {
    max_len: usize,
    data: Vec<u8>,
}

impl BoundedByteTail {
    pub fn new(max_len: usize) -> Self {
        Self {
            max_len: max_len.max(1),
            data: Vec::new(),
        }
    }

    pub fn push(&mut self, bytes: &[u8]) {
        if bytes.is_empty() {
            return;
        }

        if bytes.len() >= self.max_len {
            self.data.clear();
            self.data
                .extend_from_slice(&bytes[bytes.len() - self.max_len..]);
            return;
        }

        let overflow = self
            .data
            .len()
            .saturating_add(bytes.len())
            .saturating_sub(self.max_len);
        if overflow > 0 {
            self.data.drain(..overflow);
        }
        self.data.extend_from_slice(bytes);
    }

    pub fn to_string_lossy(&self) -> String {
        String::from_utf8_lossy(&self.data).into_owned()
    }
}

#[cfg(test)]
mod tests {
    use super::BoundedByteTail;

    #[test]
    fn push_when_under_capacity_retains_all_bytes() {
        let mut tail = BoundedByteTail::new(16);
        tail.push(b"hello");
        assert_eq!(tail.to_string_lossy(), "hello");
    }

    #[test]
    fn push_when_exceeding_capacity_keeps_only_last_bytes() {
        let mut tail = BoundedByteTail::new(4 * 1024);
        let prefix = vec![b'a'; 100];
        let suffix = vec![b'b'; 4 * 1024];
        tail.push(&prefix);
        tail.push(&suffix);

        let text = tail.to_string_lossy();
        assert_eq!(text.len(), 4 * 1024);
        assert!(text.chars().all(|ch| ch == 'b'));
        assert!(!text.contains('a'));
    }

    #[test]
    fn push_when_single_chunk_exceeds_capacity_keeps_chunk_tail() {
        let mut tail = BoundedByteTail::new(8);
        tail.push(b"0123456789abcdef");
        assert_eq!(tail.to_string_lossy(), "89abcdef");
    }

    #[test]
    fn push_when_empty_is_noop() {
        let mut tail = BoundedByteTail::new(8);
        tail.push(b"keep");
        tail.push(b"");
        assert_eq!(tail.to_string_lossy(), "keep");
    }

    #[test]
    fn new_when_zero_capacity_clamps_to_one_byte() {
        let mut tail = BoundedByteTail::new(0);
        tail.push(b"xy");
        assert_eq!(tail.to_string_lossy(), "y");
    }
}
