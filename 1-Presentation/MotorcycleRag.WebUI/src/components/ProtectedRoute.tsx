import { Navigate } from 'react-router-dom';
import { useAuth } from '../contexts/useAuth';

interface ProtectedRouteProps {
    children: React.ReactNode;
}

export default function ProtectedRoute({ children }: ProtectedRouteProps) {
    const { user, isLoading } = useAuth();
    
    if (isLoading) {
        return (
            <div className="h-screen w-full flex items-center justify-center bg-background">
                <div className="text-gray-400">I was sleeping! give me a minute to finish booting, Like John I boot slow</div>
            </div>
        );
    }
    
    if (!user || !user.authenticated) {
        return <Navigate to="/login" replace />;
    }
    
    return <>{children}</>;
}
