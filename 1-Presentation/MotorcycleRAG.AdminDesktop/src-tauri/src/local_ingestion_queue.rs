use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LocalIngestionWorkItemRequest {
    pub source_path: String,
    pub job_id: String,
    pub upload_id: String,
    pub processor_run_id: String,
    pub document_type: String,
    pub created_at_utc: String,
    #[serde(default)]
    pub source_file_name: Option<String>,
    #[serde(default)]
    pub size: Option<u64>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct LocalIngestionManifest {
    job_id: String,
    upload_id: String,
    processor_run_id: String,
    document_type: String,
    source_file_name: String,
    source_path: String,
    local_file_name: String,
    size_bytes: u64,
    created_at_utc: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LocalIngestionWorkItemResult {
    pub watch_folder: String,
    pub manifest_file_name: String,
    pub paired_local_file_name: String,
}

pub fn queue_local_ingestion_work_item_to_watch_folder(
    watch_folder: &Path,
    request: LocalIngestionWorkItemRequest,
) -> Result<LocalIngestionWorkItemResult, String> {
    validate_manifest_id("jobId", &request.job_id)?;
    validate_manifest_id("uploadId", &request.upload_id)?;
    validate_manifest_id("processorRunId", &request.processor_run_id)?;

    let source_path = PathBuf::from(&request.source_path);
    if !source_path.is_file() {
        return Err("selected source file is not readable".to_string());
    }

    let source_file_name = request
        .source_file_name
        .filter(|value| !value.trim().is_empty())
        .or_else(|| {
            source_path
                .file_name()
                .map(|value| value.to_string_lossy().into_owned())
        })
        .ok_or_else(|| "selected source file must have a file name".to_string())?;

    let actual_size = source_path
        .metadata()
        .map_err(|e| format!("failed to inspect selected source file: {e}"))?
        .len();
    if let Some(expected_size) = request.size {
        if actual_size != expected_size {
            return Err("selected source file size changed before queueing".to_string());
        }
    }

    let files_dir = watch_folder.join("files");
    let manifests_dir = watch_folder.join("manifests");
    std::fs::create_dir_all(&files_dir)
        .map_err(|e| format!("failed to create local ingestion files directory: {e}"))?;
    std::fs::create_dir_all(&manifests_dir)
        .map_err(|e| format!("failed to create local ingestion manifests directory: {e}"))?;

    let paired_local_file_name = build_paired_file_name(&request.upload_id, &source_file_name);
    let paired_file_path = files_dir.join(&paired_local_file_name);
    std::fs::copy(&source_path, &paired_file_path).map_err(|e| {
        format!("failed to copy selected source file into local ingestion queue: {e}")
    })?;

    let manifest = LocalIngestionManifest {
        job_id: request.job_id,
        upload_id: request.upload_id,
        processor_run_id: request.processor_run_id,
        document_type: request.document_type,
        source_file_name,
        source_path: request.source_path,
        local_file_name: paired_local_file_name.clone(),
        size_bytes: actual_size,
        created_at_utc: request.created_at_utc,
    };
    let manifest_json = serde_json::to_vec_pretty(&manifest)
        .map_err(|e| format!("failed to serialize local ingestion manifest: {e}"))?;

    let manifest_file_name = format!(
        "{}.json",
        sanitize_file_component(&manifest.processor_run_id)
    );
    let manifest_path = manifests_dir.join(&manifest_file_name);
    let tmp_manifest_path = manifests_dir.join(format!("{manifest_file_name}.tmp"));
    std::fs::write(&tmp_manifest_path, manifest_json)
        .map_err(|e| format!("failed to write local ingestion manifest: {e}"))?;
    std::fs::rename(&tmp_manifest_path, &manifest_path)
        .map_err(|e| format!("failed to publish local ingestion manifest: {e}"))?;

    Ok(LocalIngestionWorkItemResult {
        watch_folder: watch_folder.to_string_lossy().into_owned(),
        manifest_file_name,
        paired_local_file_name,
    })
}

fn sanitize_file_component(value: &str) -> String {
    let sanitized: String = value
        .chars()
        .map(|ch| {
            if ch.is_ascii_alphanumeric() || matches!(ch, '.' | '-' | '_') {
                ch
            } else {
                '_'
            }
        })
        .collect();

    let trimmed = sanitized.trim_matches('_');
    if trimmed.is_empty() {
        "source".to_string()
    } else {
        trimmed.to_string()
    }
}

fn validate_manifest_id(label: &str, value: &str) -> Result<(), String> {
    if value.trim().is_empty() {
        return Err(format!("{label} is required"));
    }

    let valid = value
        .chars()
        .all(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '-' | '_'));
    if !valid {
        return Err(format!("{label} contains unsupported characters"));
    }

    Ok(())
}

fn build_paired_file_name(upload_id: &str, source_file_name: &str) -> String {
    format!(
        "{}-{}",
        sanitize_file_component(upload_id),
        sanitize_file_component(source_file_name)
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn build_paired_file_name_removes_path_separators() {
        assert_eq!(
            build_paired_file_name("upload-1", "../manual path.pdf"),
            "upload-1-.._manual_path.pdf"
        );
    }

    #[test]
    fn validate_manifest_id_rejects_path_like_values() {
        assert!(validate_manifest_id("uploadId", "upload-1").is_ok());
        assert!(validate_manifest_id("uploadId", "../upload-1").is_err());
        assert!(validate_manifest_id("uploadId", "").is_err());
    }
}
