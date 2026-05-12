import { useState } from 'react';
import { Outlet, NavLink } from 'react-router-dom';
import { MessageSquare, Settings, Bike, Activity, LogOut, Menu, X } from 'lucide-react';
import { useAuth } from '../contexts/useAuth';
import { cn } from '../lib/utils';

export default function MainLayout() {
    const { user, logout } = useAuth();
    const [sidebarOpen, setSidebarOpen] = useState(false);

    return (
        <div className="flex h-dvh bg-background text-foreground font-sans overflow-hidden">
            {/* Mobile overlay */}
            {sidebarOpen && (
                <div
                    className="fixed inset-0 bg-black/60 z-40 lg:hidden"
                    onClick={() => setSidebarOpen(false)}
                />
            )}

            {/* Sidebar */}
            <aside className={cn(
                "fixed inset-y-0 left-0 z-50 w-64 border-r border-border bg-card flex flex-col transition-transform duration-300 ease-in-out lg:static lg:translate-x-0",
                sidebarOpen ? "translate-x-0" : "-translate-x-full"
            )}>
                <div className="p-4 lg:p-6 flex items-center justify-between">
                    <div className="flex items-center gap-3">
                        <Bike className="w-7 h-7 lg:w-8 lg:h-8 text-primary" />
                        <span className="font-bold text-lg tracking-wider">MOTO<span className="text-primary">RAG</span></span>
                    </div>
                    <button
                        onClick={() => setSidebarOpen(false)}
                        className="lg:hidden p-1 text-gray-400 hover:text-white"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <nav className="flex-1 px-3 lg:px-4 space-y-1 mt-2 lg:mt-4">
                    <p className="text-xs font-semibold text-gray-500 mb-2 px-2 uppercase tracking-widest">Navigation</p>

                    <NavLink
                        to="/"
                        end
                        onClick={() => setSidebarOpen(false)}
                        className={({ isActive }) => cn(
                            "flex items-center gap-3 px-3 py-2.5 rounded-md transition-colors text-sm font-medium",
                            isActive ? "bg-primary/10 text-primary border-l-2 border-primary" : "text-gray-400 hover:text-white hover:bg-secondary"
                        )}
                    >
                        <MessageSquare className="w-4 h-4" />
                        Chat
                    </NavLink>

                    <NavLink
                        to="/settings"
                        onClick={() => setSidebarOpen(false)}
                        className={({ isActive }) => cn(
                            "flex items-center gap-3 px-3 py-2.5 rounded-md transition-colors text-sm font-medium",
                            isActive ? "bg-primary/10 text-primary border-l-2 border-primary" : "text-gray-400 hover:text-white hover:bg-secondary"
                        )}
                    >
                        <Settings className="w-4 h-4" />
                        Settings
                    </NavLink>

                    <NavLink to="/" className={({ isActive }) => cn(
                        "flex items-center gap-3 px-3 py-2.5 rounded-md transition-colors text-sm font-medium",
                        isActive ? "bg-primary/10 text-primary border-l-2 border-primary" : "text-gray-400 hover:text-white hover:bg-secondary"
                    )} onClick={() => setSidebarOpen(false)}>
                        <Activity className="w-4 h-4" />
                        System Health
                    </NavLink>
                </nav>

                <div className="p-3 lg:p-4 border-t border-border">
                    <div className="flex items-center gap-3 mb-3">
                        <div className="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center text-primary font-bold text-sm">
                            {user?.user?.[0]?.toUpperCase() || 'U'}
                        </div>
                        <div className="min-w-0 flex-1">
                            <p className="text-sm font-medium truncate">{user?.user || 'User'}</p>
                            <p className="text-xs text-gray-500 truncate">{user?.email || ''}</p>
                        </div>
                    </div>
                    <button
                        onClick={logout}
                        className="flex items-center gap-2 text-sm text-gray-400 hover:text-white transition-colors w-full px-2 py-1.5 rounded hover:bg-secondary"
                    >
                        <LogOut className="w-4 h-4" />
                        Sign Out
                    </button>
                </div>
            </aside>

            {/* Main content area */}
            <div className="flex-1 flex flex-col min-w-0">
                {/* Mobile top bar */}
                <div className="h-12 lg:hidden flex items-center px-3 border-b border-white/5 bg-[#1f1f1f]">
                    <button
                        onClick={() => setSidebarOpen(true)}
                        className="p-2 text-gray-400 hover:text-white transition-colors"
                    >
                        <Menu className="w-5 h-5" />
                    </button>
                    <div className="flex items-center gap-2 ml-2">
                        <Bike className="w-5 h-5 text-primary" />
                        <span className="font-bold text-sm tracking-wider">MOTO<span className="text-primary">RAG</span></span>
                    </div>
                </div>

                <Outlet />
            </div>
        </div>
    );
}
