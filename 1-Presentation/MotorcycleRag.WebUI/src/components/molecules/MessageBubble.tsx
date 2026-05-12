
import { cn } from '../../lib/utils';
import type { Action, Message } from '../../types/chat';
import { User, Bot, FileText, FileBarChart } from 'lucide-react';
import ReactMarkdown from 'react-markdown';

interface MessageBubbleProps {
    message: Message;
    onAction?: (action: Action) => void;
}

export function MessageBubble({ message, onAction }: MessageBubbleProps) {
    const isUser = message.role === 'user';

    return (
        <div className={cn("flex gap-3 max-w-4xl w-full", isUser ? "flex-row-reverse self-end" : "flex-row")}>
            <div className={cn(
                "w-8 h-8 rounded-full flex items-center justify-center shrink-0",
                isUser ? "bg-secondary text-gray-300" : "bg-primary text-white"
            )}>
                {isUser ? <User className="w-5 h-5" /> : <Bot className="w-5 h-5" />}
            </div>

            <div className={cn(
                "relative group p-4 rounded-2xl text-sm leading-relaxed",
                isUser
                    ? "bg-secondary text-gray-200 rounded-tr-sm"
                    : "bg-[#252525] text-gray-100 border-l-2 border-primary rounded-tl-sm shadow-md"
            )}>
                <div className="text-sm">
                    <ReactMarkdown
                        components={{
                            p: ({ ...props }) => <p className="mb-2 last:mb-0" {...props} />,
                            ul: ({ ...props }) => <ul className="list-disc pl-4 mb-2" {...props} />,
                            ol: ({ ...props }) => <ol className="list-decimal pl-4 mb-2" {...props} />,
                            li: ({ ...props }) => <li className="mb-1" {...props} />,
                            h1: ({ ...props }) => <h1 className="text-lg font-bold mb-2" {...props} />,
                            h2: ({ ...props }) => <h2 className="text-md font-bold mb-2" {...props} />,
                            code: ({ ...props }) => <code className="bg-black/20 px-1 rounded" {...props} />
                        }}
                    >
                        {message.content}
                    </ReactMarkdown>
                </div>

                {/* Attachments / Actions */}
                {message.actions && (
                    <div className="flex flex-wrap gap-2 mt-3 pt-3 border-t border-white/10">
                        {message.actions.map((action, idx) => (
                            <button
                                key={idx}
                                type="button"
                                onClick={() => onAction?.(action)}
                                className="flex items-center gap-2 px-3 py-1.5 bg-primary/10 hover:bg-primary/20 text-primary rounded-md text-xs font-semibold transition-colors border border-primary/20"
                                title={action.reason}
                            >
                                {action.icon === 'manuals' && <FileText className="w-3.5 h-3.5" />}
                                {action.icon === 'specs' && <FileBarChart className="w-3.5 h-3.5" />}
                                {action.label}
                            </button>
                        ))}
                    </div>
                )}

                <span className="text-[10px] text-gray-500 absolute bottom-1 right-3 opacity-0 group-hover:opacity-100 transition-opacity">
                    {message.timestamp.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </span>
            </div>
        </div>
    );
}
