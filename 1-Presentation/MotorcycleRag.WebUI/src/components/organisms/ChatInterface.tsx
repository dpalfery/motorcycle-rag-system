import { useState, useEffect, useRef } from 'react';
import { Send, Plus, Loader2 } from 'lucide-react';
import { MessageBubble } from '../molecules/MessageBubble';
import type { Action, Message } from '../../types/chat';

export default function ChatInterface() {
    const [input, setInput] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);

    const [messages, setMessages] = useState<Message[]>([
        {
            id: '1',
            role: 'assistant',
            content: 'Hello! I am your Motorcycle RAG Agent. I can help you find specifications, maintenance manuals, and troubleshoot issues. What are you looking for today?',
            timestamp: new Date(),
        }
    ]);

    const [currentModel, setCurrentModel] = useState<string>('DeepSeek-V4-Flash');

    const scrollToBottom = () => {
        messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
    };

    useEffect(() => {
        scrollToBottom();
    }, [messages]);

    const sendMessage = async (messageText: string) => {
        const trimmedInput = messageText.trim();
        if (!trimmedInput || isLoading) return;

        const recentMessages = messages.slice(-6).map((msg) => ({
            role: msg.role,
            content: msg.content.slice(0, 1000)
        }));

        const userMsg: Message = {
            id: Date.now().toString(),
            role: 'user',
            content: trimmedInput,
            timestamp: new Date()
        };

        setMessages(prev => [...prev, userMsg]);
        setInput('');
        setIsLoading(true);

        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 120000);

        try {
            const response = await fetch('/api/motorcycles/query', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    query: userMsg.content,
                    preferences: {},
                    userId: "user", // TODO: Get from AuthContext
                    context: {
                        recentMessages
                    }
                    context: {}
>>>>>>> 978e416 (feat: responsive mobile design for WebUI)
                }),
                signal: controller.signal
            });

            clearTimeout(timeoutId);
            if (!response.ok) throw new Error('Failed to get response');

            const data = await response.json();

            if (data.modelUsed) {
                setCurrentModel(data.modelUsed);
            }

            const aiMsg: Message = {
                id: (Date.now() + 1).toString(),
                role: 'assistant',
                content: data.response,
                timestamp: new Date(),
                actions: [
                    ...(data.suggestions?.map((suggestion: { label: string; query: string; reason?: string; subject?: string }) => ({
                        label: suggestion.label,
                        type: 'callback' as const,
                        value: suggestion.query,
                        icon: 'specs',
                        reason: suggestion.reason,
                        subject: suggestion.subject
                    })) ?? []),
                    ...(data.sources?.length > 0 ? [
                        { label: 'SOURCES', type: 'link' as const, value: '#', icon: 'specs' }
                    ] : [])
                ]
            };
            setMessages(prev => [...prev, aiMsg]);
        } catch (error) {
            clearTimeout(timeoutId);
            const isTimeout = error instanceof DOMException && error.name === 'AbortError';
            const errorMsg: Message = {
                id: (Date.now() + 1).toString(),
                role: 'assistant',
                content: isTimeout
                    ? "The request timed out. The server is taking too long to respond — please try again."
                    : "I'm sorry, I encountered an error connecting to the motorcycle database. Please try again later.",
                timestamp: new Date()
            };
            setMessages(prev => [...prev, errorMsg]);
        } finally {
            setIsLoading(false);
        }
    };

    const handleSend = async (e?: React.FormEvent) => {
        e?.preventDefault();
        await sendMessage(input);
    };

    const handleAction = async (action: Action) => {
        if (action.type === 'callback') {
            await sendMessage(action.value);
        }
    };

    return (
        <div className="flex flex-col h-full bg-[#1a1a1a]">
            {/* Header */}
            <div className="h-11 lg:h-14 border-b border-white/5 flex items-center px-3 lg:px-6 justify-between bg-[#1f1f1f] shrink-0">
                <h2 className="font-semibold text-sm lg:text-base text-gray-200">New Conversation</h2>
                <div className="flex gap-2 text-[10px] lg:text-xs text-gray-500">
                    <span>Model: <span className="text-primary">{currentModel}</span></span>
                </div>
            </div>

            {/* Messages Area */}
            <div className="flex-1 overflow-y-auto p-3 lg:p-4 space-y-4 lg:space-y-6 overscroll-contain">
                {messages.map((msg) => (
                    <div key={msg.id} className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}>
                        <MessageBubble message={msg} onAction={handleAction} />
                    </div>
                ))}

                {isLoading && (
                    <div className="flex justify-start">
                        <div className="w-7 h-7 lg:w-8 lg:h-8 rounded-full bg-primary/20 flex items-center justify-center shrink-0 mr-2 lg:mr-3">
                            <Loader2 className="w-3.5 h-3.5 lg:w-4 lg:h-4 text-primary animate-spin" />
                        </div>
                        <div className="bg-[#252525] p-2.5 lg:p-3 rounded-2xl rounded-tl-none border-l-2 border-primary/50 text-gray-400 text-xs lg:text-sm flex items-center gap-2">
                            <span>Processing your query...</span>
                        </div>
                    </div>
                )}
                <div ref={messagesEndRef} />
            </div>

            {/* Input Area */}
            <div className="p-2 lg:p-4 bg-[#1f1f1f] border-t border-white/5 shrink-0">
                <form onSubmit={handleSend} className="w-full lg:max-w-4xl lg:mx-auto relative flex items-center gap-1.5 lg:gap-2">
                    <button type="button" className="p-2.5 lg:p-3 text-gray-400 hover:text-white transition-colors shrink-0">
                        <Plus className="w-5 h-5" />
                    </button>

                    <div className="flex-1 relative">
                        <input
                            ref={inputRef}
                            type="text"
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            placeholder="Type your motorcycle query..."
                            className="w-full bg-[#2a2a2a] text-white text-sm lg:text-base rounded-xl py-3 lg:py-3.5 pl-3.5 lg:pl-4 pr-11 lg:pr-12 focus:outline-none focus:ring-1 focus:ring-primary/50 border border-transparent focus:border-primary/30 placeholder-gray-500"
                        />
                        <button
                            type="submit"
                            disabled={!input.trim() || isLoading}
                            className="absolute right-1.5 lg:right-2 top-1/2 -translate-y-1/2 p-2 lg:p-2.5 bg-primary/10 text-primary hover:bg-primary hover:text-white rounded-lg transition-all disabled:opacity-50 disabled:hover:bg-primary/10 disabled:hover:text-primary touch-manipulation"
                        >
                            <Send className="w-4 h-4" />
                        </button>
                    </div>
                </form>
                <div className="text-center mt-1.5 lg:mt-2">
                    <p className="text-[9px] lg:text-[10px] text-gray-600">AI can make mistakes. Verify important information.</p>
                </div>
            </div>
        </div>
    );
}
