
import { createContext, useContext } from 'react';
import { useQuery } from '@tanstack/react-query';

interface User {
    user: string;
    token?: string;
    authenticated: boolean;
}

interface AuthContextType {
    user: User | null;
    isLoading: boolean;
    login: () => void;
    logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
    // Check auth status from BFF
    const { data, isLoading } = useQuery({
        queryKey: ['auth-me'],
        queryFn: async () => {
            const res = await fetch('/auth/me');
            if (!res.ok) throw new Error('Not authenticated');
            return res.json() as Promise<User>;
        },
        retry: false,
    });

    const login = () => {
        window.location.href = '/auth/login';
    };

    const logout = async () => {
        await fetch('/auth/logout', { method: 'POST' });
        window.location.href = '/';
    };

    const user = data || null;

    return (
        <AuthContext.Provider value={{ user, isLoading, login, logout }}>
            {children}
        </AuthContext.Provider>
    );
}

export function useAuth() {
    const context = useContext(AuthContext);
    if (context === undefined) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
}
