
import { useState, type DragEvent } from 'react';
import { Upload, File as FileIcon, Trash2, CheckCircle, Clock } from 'lucide-react';
import { cn } from '../lib/utils';

interface FileUpload {
    id: string;
    name: string;
    type: 'PDF' | 'CSV';
    status: 'indexed' | 'processing' | 'error';
    date: string;
    size: string;
    errorMessage?: string;
}

interface IngestionUploadResponse {
    uploadId: string;
    fileName: string;
    documentType: string;
    status: string;
}

interface ProblemDetails {
    title?: string;
    detail?: string;
    traceId?: string;
    referenceId?: string;
    extensions?: {
        traceId?: string;
        referenceId?: string;
    };
}

function getDocumentType(file: File) {
    const name = file.name.toLowerCase();
    if (name.endsWith('.pdf')) return 'manual-pdf';
    if (name.endsWith('.csv')) return 'spec-dataset';
    throw new Error('Only PDF manuals and CSV specification files are supported.');
}

function getStartConfiguration(documentType: string) {
    return documentType === 'manual-pdf'
        ? {
            extractGraphRelationships: true,
            ocrEnabled: true,
        }
        : undefined;
}

function formatProblemDetails(problem: ProblemDetails | undefined, fallback: string) {
    const message = problem?.detail ?? problem?.title ?? fallback;
    const traceId = problem?.traceId ?? problem?.extensions?.traceId;
    const referenceId = problem?.referenceId ?? problem?.extensions?.referenceId;
    const suffixParts = [
        traceId ? `trace: ${traceId}` : null,
        referenceId ? `reference: ${referenceId}` : null,
    ].filter(Boolean);

    return suffixParts.length > 0
        ? `${message} (${suffixParts.join(', ')})`
        : message;
}

async function readProblem(response: Response) {
    try {
        return await response.json() as ProblemDetails;
    } catch {
        return undefined;
    }
}

function uploadStepError(step: string, error: unknown) {
    return new Error(`${step}: ${error instanceof Error ? error.message : String(error)}`);
}

export default function SettingsPage() {
    const [uploads, setUploads] = useState<FileUpload[]>([
        { id: '1', name: '2024_YZF_R1_Manual.pdf', type: 'PDF', status: 'indexed', date: '2024-12-20', size: '4.2 MB' },
        { id: '2', name: 'parts_catalog_v2.csv', type: 'CSV', status: 'processing', date: '2024-12-21', size: '1.8 MB' },
    ]);
    const [dragActive, setDragActive] = useState(false);

    const handleDrag = (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        if (e.type === "dragenter" || e.type === "dragover") {
            setDragActive(true);
        } else if (e.type === "dragleave") {
            setDragActive(false);
        }
    };

    const handleDrop = async (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        setDragActive(false);
        if (e.dataTransfer.files && e.dataTransfer.files[0]) {
            const file = e.dataTransfer.files[0];
            let documentType: string;
            try {
                documentType = getDocumentType(file);
            } catch (error) {
                const tempId = Date.now().toString();
                setUploads(prev => [{
                    id: tempId,
                    name: file.name,
                    type: file.name.toLowerCase().endsWith('.csv') ? 'CSV' : 'PDF',
                    status: 'error',
                    date: new Date().toISOString().split('T')[0],
                    size: (file.size / 1024 / 1024).toFixed(1) + ' MB',
                    errorMessage: error instanceof Error ? error.message : String(error),
                }, ...prev]);
                return;
            }
            const formData = new FormData();
            formData.append('file', file);

            // Optimistic UI update
            const tempId = Date.now().toString();
            const optimisticFile: FileUpload = {
                id: tempId,
                name: file.name,
                type: file.name.endsWith('.csv') ? 'CSV' : 'PDF',
                status: 'processing',
                date: new Date().toISOString().split('T')[0],
                size: (file.size / 1024 / 1024).toFixed(1) + ' MB'
            };
            setUploads(prev => [optimisticFile, ...prev]);

            try {
                const uploadResponse = await fetch(`/api/ingestion/jobs/upload?documentType=${encodeURIComponent(documentType)}`, {
                    method: 'POST',
                    body: formData,
                });

                if (!uploadResponse.ok) {
                    throw uploadStepError(
                        `Upload failed while storing the ${documentType === 'manual-pdf' ? 'PDF manual' : 'CSV specification'} source`,
                        formatProblemDetails(
                            await readProblem(uploadResponse),
                            'The source file could not be uploaded.'
                        )
                    );
                }

                const uploadResult = await uploadResponse.json() as IngestionUploadResponse;
                const startResponse = await fetch('/api/ingestion/jobs', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({
                        uploadId: uploadResult.uploadId,
                        documentType: uploadResult.documentType || documentType,
                        configuration: getStartConfiguration(documentType),
                    }),
                });

                if (!startResponse.ok) {
                    throw uploadStepError(
                        `Source upload succeeded, but starting ${documentType === 'manual-pdf' ? 'PDF manual' : 'CSV specification'} processing failed`,
                        formatProblemDetails(
                            await readProblem(startResponse),
                            'The ingestion job could not be started.'
                        )
                    );
                }

                setUploads(prev => prev.map(u =>
                    u.id === tempId ? { ...u, status: 'processing' } : u
                ));
            } catch (error) {
                console.error('Upload error:', error);
                setUploads(prev => prev.map(u =>
                    u.id === tempId
                        ? {
                            ...u,
                            status: 'error',
                            errorMessage: error instanceof Error ? error.message : String(error),
                        }
                        : u
                ));
            }
        }
    };

    return (
        <div className="flex flex-col h-full bg-[#1a1a1a] overflow-hidden">
            {/* Header */}
            <div className="h-14 border-b border-white/5 flex items-center px-6 bg-[#1f1f1f]">
                <h2 className="font-semibold text-gray-200">System Configuration</h2>
            </div>

            <div className="flex-1 overflow-y-auto p-8">
                <div className="max-w-5xl mx-auto space-y-8">

                    {/* Data Ingestion Section */}
                    <section>
                        <h3 className="text-lg font-medium text-gray-300 mb-4">Data Ingestion</h3>

                        {/* Drag Drop Zone */}
                        <div
                            className={cn(
                                "group relative border-2 border-dashed rounded-xl p-12 text-center transition-all duration-300 ease-in-out cursor-pointer",
                                dragActive
                                    ? "border-primary bg-primary/5"
                                    : "border-white/10 hover:border-primary/50 hover:bg-white/5"
                            )}
                            onDragEnter={handleDrag}
                            onDragLeave={handleDrag}
                            onDragOver={handleDrag}
                            onDrop={handleDrop}
                        >
                            <div className="flex flex-col items-center gap-4 pointer-events-none">
                                <div className={cn(
                                    "w-16 h-16 rounded-full flex items-center justify-center transition-colors",
                                    dragActive ? "bg-primary/20 text-primary" : "bg-white/5 text-gray-400 group-hover:text-primary"
                                )}>
                                    <Upload className="w-8 h-8" />
                                </div>
                                <div>
                                    <p className="text-lg font-medium text-gray-200">Drag & Drop files here</p>
                                    <p className="text-sm text-gray-500 mt-1">Supports PDF Manuals and CSV Specification files</p>
                                </div>
                            </div>
                        </div>
                    </section>

                    {/* Data Table */}
                    <section>
                        <h3 className="text-lg font-medium text-gray-300 mb-4">Ingested Data</h3>
                        <div className="border border-white/10 rounded-xl overflow-hidden bg-[#222222]">
                            <table className="w-full text-left text-sm">
                                <thead className="bg-white/5 text-gray-400">
                                    <tr>
                                        <th className="px-6 py-3 font-medium">Filename</th>
                                        <th className="px-6 py-3 font-medium">Type</th>
                                        <th className="px-6 py-3 font-medium">Size</th>
                                        <th className="px-6 py-3 font-medium">Uploaded</th>
                                        <th className="px-6 py-3 font-medium">Status</th>
                                        <th className="px-6 py-3 font-medium text-right">Actions</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-white/5 text-gray-300">
                                    {uploads.map((file) => (
                                        <tr key={file.id} className="hover:bg-white/5 transition-colors">
                                            <td className="px-6 py-4 flex items-center gap-3">
                                                <FileIcon className="w-4 h-4 text-gray-500" />
                                                <span className="font-medium text-gray-200">{file.name}</span>
                                            </td>
                                            <td className="px-6 py-4">
                                                <span className="px-2 py-0.5 rounded text-xs font-medium bg-white/10 text-gray-400 border border-white/10">
                                                    {file.type}
                                                </span>
                                            </td>
                                            <td className="px-6 py-4 text-gray-500">{file.size}</td>
                                            <td className="px-6 py-4 text-gray-500">{file.date}</td>
                                            <td className="px-6 py-4">
                                                {file.status === 'indexed' && (
                                                    <span className="flex items-center gap-1.5 text-green-500 text-xs font-medium px-2 py-0.5 rounded-full bg-green-500/10 border border-green-500/20 w-fit">
                                                        <CheckCircle className="w-3 h-3" /> Indexed
                                                    </span>
                                                )}
                                                {file.status === 'processing' && (
                                                    <span className="flex items-center gap-1.5 text-orange-500 text-xs font-medium px-2 py-0.5 rounded-full bg-orange-500/10 border border-orange-500/20 w-fit">
                                                        <Clock className="w-3 h-3 animate-pulse" /> Processing
                                                    </span>
                                                )}
                                                {file.status === 'error' && (
                                                    <span
                                                        className="flex items-center gap-1.5 text-red-400 text-xs font-medium px-2 py-0.5 rounded-full bg-red-500/10 border border-red-500/20 w-fit"
                                                        title={file.errorMessage}
                                                    >
                                                        Error
                                                    </span>
                                                )}
                                            </td>
                                            <td className="px-6 py-4 text-right">
                                                <button className="text-gray-500 hover:text-red-400 transition-colors p-1 rounded-md hover:bg-white/5">
                                                    <Trash2 className="w-4 h-4" />
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </section>
                </div>
            </div>
        </div>
    );
}
