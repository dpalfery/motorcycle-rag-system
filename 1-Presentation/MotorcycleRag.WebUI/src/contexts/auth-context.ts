import { createContext } from 'react';

interface User {
    user: string;
    token?: string;
    authenticated: boolean;
    sessionAuthenticated?: boolean;
    accessApproved?: boolean;
    approvalStatus?: string;
    approvalMessage?: string;
    managedUserId?: string;
    email?: string;
    planName?: string;
}

interface AuthContextType {
    user: User | null;
    isLoading: boolean;
    login: () => void;
    logout: () => void;
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined);
