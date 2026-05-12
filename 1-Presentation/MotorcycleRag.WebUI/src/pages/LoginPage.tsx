import { Navigate } from 'react-router-dom';
import { useState } from 'react';
import { useAuth } from '../contexts/useAuth';
import { Bike } from 'lucide-react';
import { submitAccessRequest } from '../lib/accessRequests';
import type { IdentityProvider, PublicAccessRequestResponse } from '../types/accessRequests';

export default function LoginPage() {
    const { user, isLoading, login } = useAuth();
    const [email, setEmail] = useState('');
    const [provider, setProvider] = useState<IdentityProvider>('Microsoft');
    const [requestState, setRequestState] = useState<PublicAccessRequestResponse | null>(null);
    const [requestError, setRequestError] = useState<string | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);

    if (isLoading) return <div className="h-screen w-full flex items-center justify-center bg-background"><div className="text-gray-400">I was sleeping! give me a minute to finish booting, Like John I boot slow</div></div>;
    if (user?.authenticated) return <Navigate to="/" replace />;

    const hasApprovalState = user?.sessionAuthenticated && user?.accessApproved === false;

    const handleSubmitRequest = async (event: React.FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setIsSubmitting(true);
        setRequestError(null);

        try {
            const result = await submitAccessRequest({
                email,
                provider,
            });

            setRequestState(result.request);
        } catch (error) {
            setRequestState(null);
            setRequestError(error instanceof Error ? error.message : 'Unable to submit your access request.');
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <div className="h-screen w-full flex items-center justify-center bg-background">
            <div className="w-full max-w-xl p-8 bg-card border border-border rounded-lg shadow-2xl text-center">
                <div className="flex justify-center mb-6">
                    <Bike className="w-16 h-16 text-primary" />
                </div>
                <h1 className="text-2xl font-bold mb-2">Welcome to MotoRAG</h1>
                <p className="text-gray-400 mb-8">Please sign in to access the system.</p>

                {hasApprovalState && (
                    <div className="mb-6 rounded border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-left text-sm text-amber-100">
                        <p className="font-semibold">{user?.approvalStatus ?? 'Approval Required'}</p>
                        <p className="text-amber-50/90">{user?.approvalMessage ?? 'This account cannot access the application yet.'}</p>
                    </div>
                )}

                <button
                    onClick={login}
                    className="w-full bg-primary hover:bg-primary/90 text-white font-bold py-3 px-4 rounded transition-all transform hover:scale-[1.02]"
                >
                    Sign In with SSO
                </button>

                <div className="my-8 border-t border-border" />

                <div className="text-left">
                    <h2 className="text-lg font-semibold text-white mb-2">Need access first?</h2>
                    <p className="text-sm text-gray-400 mb-4">
                        Submit your Microsoft or Google account email for admin approval. Re-submitting the same provider and email will show your current request status.
                    </p>

                    <form className="space-y-4" onSubmit={handleSubmitRequest}>
                        <div>
                            <label className="block text-sm font-medium text-gray-300 mb-2" htmlFor="access-request-email">
                                Email address
                            </label>
                            <input
                                id="access-request-email"
                                type="email"
                                value={email}
                                onChange={(event) => setEmail(event.target.value)}
                                className="w-full rounded border border-border bg-background px-3 py-2 text-white focus:border-primary focus:outline-none"
                                placeholder="you@example.com"
                                required
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-300 mb-2" htmlFor="access-request-provider">
                                Sign-in provider
                            </label>
                            <select
                                id="access-request-provider"
                                value={provider}
                                onChange={(event) => setProvider(event.target.value as IdentityProvider)}
                                className="w-full rounded border border-border bg-background px-3 py-2 text-white focus:border-primary focus:outline-none"
                            >
                                <option value="Microsoft">Microsoft</option>
                                <option value="Google">Google</option>
                            </select>
                        </div>

                        <button
                            type="submit"
                            disabled={isSubmitting}
                            className="w-full border border-primary text-primary hover:bg-primary hover:text-white font-semibold py-3 px-4 rounded transition-all disabled:opacity-60 disabled:cursor-not-allowed"
                        >
                            {isSubmitting ? 'Submitting Request...' : 'Request Access'}
                        </button>
                    </form>

                    {requestError && (
                        <div className="mt-4 rounded border border-red-500/40 bg-red-500/10 px-4 py-3 text-sm text-red-200">
                            {requestError}
                        </div>
                    )}

                    {requestState && (
                        <div className="mt-4 rounded border border-primary/40 bg-primary/10 px-4 py-3 text-sm text-left text-white space-y-1">
                            <p className="font-semibold">{requestState.requesterVisibleStatus}</p>
                            {requestState.statusMessage && <p className="text-gray-200">{requestState.statusMessage}</p>}
                            <p className="text-gray-300">Provider: {requestState.provider}</p>
                            <p className="text-gray-300">Email: {requestState.email}</p>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
