import { createContext } from 'react';

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

export const AuthContext = createContext<AuthContextType | undefined>(undefined);
