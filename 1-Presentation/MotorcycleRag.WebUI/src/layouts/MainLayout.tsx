
import { Outlet, NavLink } from 'react-router';
import { MessageSquare, Settings, Bike, Activity, LogOut } from 'lucide-react';
import { useAuth } from '../contexts/useAuth';
import { cn } from '../lib/utils';

export default function MainLayout() {
    const { user, logout } = useAuth();

    return (
        <div className="flex h-screen bg-background text-foreground font-sans overflow-hidden">
            {/* Sidebar */}
            <aside className="w-64 border-r border-border bg-card flex flex-col">
                <div className="p-6 flex items-center gap-3">
                    <Bike className="w-8 h-8 text-primary" />
                    <span className="font-bold text-lg tracking-wider">MOTO<span className="text-primary">RAG</span></span>
                </div>

                <nav className="flex-1 px-4 space-y-2 mt-4">
                    <p className="text-xs font-semibold text-gray-500 mb-2 px-2 uppercase tracking-widest">Navigation</p>

                    <NavLink to="/" end className={({ isActive }) => cn(
                        "flex items-center gap-3 px-3 py-2 rounded-md transition-colors text-sm font-medium",
                        isActive ? "bg-primary/10 text-primary border-l-2 border-primary" : "text-gray-400 hover:text-white hover:bg-secondary"
                    )}>
                        <MessageSquare className="w-4 h-4" />
                        Chat
                    </NavLink>

                    <NavLink to="/settings" className={({ isActive }) => cn(
                        "flex items-center gap-3 px-3 py-2 rounded-md transition-colors text-sm font-medium",
                        isActive ? "bg-primary/10 text-primary border-l-2 border-primary" : "text-gray-400 hover:text-white hover:bg-secondary"
                    )}>
                        <Settings className="w-4 h-4" />
                        Settings
                    </NavLink>
                </nav>

                <div className="p-4 border-t border-border">
                    <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                            <Activity className="w-4 h-4 text-green-500" />
                            <span className="text-xs text-gray-400">System: ONLINE</span>
                        </div>
                        <button onClick={logout} className="text-gray-500 hover:text-white transition-colors" title="Logout">
                            <LogOut className="w-4 h-4" />
                        </button>
                    </div>
                    {user && (
                        <div className="mt-2 text-xs text-gray-600 truncate">
                            Logged in as {user.user}
                        </div>
                    )}
                </div>
            </aside>

            {/* Main Content */}
            <main className="flex-1 flex flex-col overflow-hidden relative">
                <Outlet />
            </main>
        </div>
    );
}
