
import { useAuth } from '../contexts/useAuth';
import { Bike } from 'lucide-react';

export default function LoginPage() {
    const { login } = useAuth();

    return (
        <div className="h-screen w-full flex items-center justify-center bg-background">
            <div className="w-full max-w-md p-8 bg-card border border-border rounded-lg shadow-2xl text-center">
                <div className="flex justify-center mb-6">
                    <Bike className="w-16 h-16 text-primary" />
                </div>
                <h1 className="text-2xl font-bold mb-2">Welcome to MotoRAG</h1>
                <p className="text-gray-400 mb-8">Please sign in to access the system.</p>

                <button
                    onClick={login}
                    className="w-full bg-primary hover:bg-primary/90 text-white font-bold py-3 px-4 rounded transition-all transform hover:scale-[1.02]"
                >
                    Sign In with SSO
                </button>
            </div>
        </div>
    );
}
