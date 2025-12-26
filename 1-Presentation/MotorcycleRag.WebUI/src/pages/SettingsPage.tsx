
import { useState } from 'react';
import { Upload, File, Trash2, CheckCircle, Clock } from 'lucide-react';
import { cn } from '../lib/utils';

interface FileUpload {
    id: string;
    name: string;
    type: 'PDF' | 'CSV';
    status: 'indexed' | 'processing' | 'error';
    date: string;
    size: string;
}

export default function SettingsPage() {
    const [uploads, setUploads] = useState<FileUpload[]>([
        { id: '1', name: '2024_YZF_R1_Manual.pdf', type: 'PDF', status: 'indexed', date: '2024-12-20', size: '4.2 MB' },
        { id: '2', name: 'parts_catalog_v2.csv', type: 'CSV', status: 'processing', date: '2024-12-21', size: '1.8 MB' },
    ]);
    const [dragActive, setDragActive] = useState(false);

    const handleDrag = (e: React.DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        if (e.type === "dragenter" || e.type === "dragover") {
            setDragActive(true);
        } else if (e.type === "dragleave") {
            setDragActive(false);
        }
    };

    const handleDrop = async (e: React.DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        setDragActive(false);
        if (e.dataTransfer.files && e.dataTransfer.files[0]) {
            const file = e.dataTransfer.files[0];
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
                const response = await fetch('/api/datapipeline/upload?processImmediately=true', {
                    method: 'POST',
                    body: formData,
                });

                if (!response.ok) throw new Error('Upload failed');

                await response.json();

                // Update status to indexed if successful
                setUploads(prev => prev.map(u =>
                    u.id === tempId ? { ...u, status: 'indexed' } : u
                ));
            } catch (error) {
                console.error('Upload error:', error);
                setUploads(prev => prev.map(u =>
                    u.id === tempId ? { ...u, status: 'error' } : u
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
                                                <File className="w-4 h-4 text-gray-500" />
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
