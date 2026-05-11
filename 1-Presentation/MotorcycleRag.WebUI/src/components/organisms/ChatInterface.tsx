
import { useState, useEffect, useRef } from 'react';
import { Send, Plus, Loader2 } from 'lucide-react';
import { MessageBubble } from '../molecules/MessageBubble';
import type { Message } from '../../types/chat';

export default function ChatInterface() {
    const [input, setInput] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const messagesEndRef = useRef<HTMLDivElement>(null);

    // Mock Messages
    const [messages, setMessages] = useState<Message[]>([
        {
            id: '1',
            role: 'assistant',
            content: 'Hello! I am your Motorcycle RAG Agent. I can help you find specifications, maintenance manuals, and troubleshoot issues. What are you looking for today?',
            timestamp: new Date(),
        }
    ]);

    const scrollToBottom = () => {
        messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
    };

    useEffect(() => {
        scrollToBottom();
    }, [messages]);

    const handleSend = async (e?: React.FormEvent) => {
        e?.preventDefault();
        if (!input.trim() || isLoading) return;

        const userMsg: Message = {
            id: Date.now().toString(),
            role: 'user',
            content: input,
            timestamp: new Date()
        };

        setMessages(prev => [...prev, userMsg]);
        setInput('');
        setIsLoading(true);

        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 120000); // Increased to 120s for RAG queries

        try {
            const response = await fetch('/api/motorcycles/query', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    query: userMsg.content,
                    preferences: {},
                    userId: "user", // TODO: Get from AuthContext
                    context: {}
                }),
                signal: controller.signal
            });

            clearTimeout(timeoutId);
            if (!response.ok) throw new Error('Failed to get response');

            const data = await response.json();

            const aiMsg: Message = {
                id: (Date.now() + 1).toString(),
                role: 'assistant',
                content: data.response, // Adjust based on actual API response field
                timestamp: new Date(),
                actions: data.sources?.length > 0 ? [
                    { label: 'SOURCES', type: 'link', value: '#', icon: 'specs' }
                ] : []
            };
            setMessages(prev => [...prev, aiMsg]);
        } catch (error) {
            clearTimeout(timeoutId);
            console.error('Chat error:', error);
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

    return (
        <div className="flex flex-col h-full bg-[#1a1a1a]">
            {/* Header */}
            <div className="h-14 border-b border-white/5 flex items-center px-6 justify-between bg-[#1f1f1f]">
                <h2 className="font-semibold text-gray-200">New Conversation</h2>
                <div className="flex gap-2 text-xs text-gray-500">
                    <span>Model: <span className="text-primary">DeepSeek-V4-Flash</span></span>
                </div>
            </div>

            {/* Messages Area */}
            <div className="flex-1 overflow-y-auto p-4 space-y-6 scrollbar-thin scrollbar-thumb-gray-700 scrollbar-track-transparent">
                {messages.map((msg) => (
                    <div key={msg.id} className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}>
                        <MessageBubble message={msg} />
                    </div>
                ))}

                {isLoading && (
                    <div className="flex justify-start">
                        <div className="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center shrink-0 mr-3">
                            <Loader2 className="w-4 h-4 text-primary animate-spin" />
                        </div>
                        <div className="bg-[#252525] p-3 rounded-2xl rounded-tl-none border-l-2 border-primary/50 text-gray-400 text-sm flex items-center gap-2">
                            <span>Analysis in progress...</span>
                        </div>
                    </div>
                )}
                <div ref={messagesEndRef} />
            </div>

            {/* Input Area */}
            <div className="p-4 bg-[#1f1f1f] border-t border-white/5">
                <form onSubmit={handleSend} className="max-w-4xl mx-auto relative flex items-center gap-2">
                    <button type="button" className="p-3 text-gray-400 hover:text-white transition-colors">
                        <Plus className="w-5 h-5" />
                    </button>

                    <div className="flex-1 relative">
                        <input
                            type="text"
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            placeholder="Type your motorcycle query..."
                            className="w-full bg-[#2a2a2a] text-white rounded-xl py-3.5 pl-4 pr-12 focus:outline-none focus:ring-1 focus:ring-primary/50 border border-transparent focus:border-primary/30 placeholder-gray-500 font-light"
                        />
                        <button
                            type="submit"
                            disabled={!input.trim() || isLoading}
                            className="absolute right-2 top-1/2 -translate-y-1/2 p-2 bg-primary/10 text-primary hover:bg-primary hover:text-white rounded-lg transition-all disabled:opacity-50 disabled:hover:bg-primary/10 disabled:hover:text-primary"
                        >
                            <Send className="w-4 h-4" />
                        </button>
                    </div>
                </form>
                <div className="text-center mt-2">
                    <p className="text-[10px] text-gray-600">AI can make mistakes. Verify important information.</p>
                </div>
            </div>
        </div>
    );
}
