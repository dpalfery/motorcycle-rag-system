import type {
    AccessRequestSubmissionResult,
    CreateAccessRequestRequest,
    PublicAccessRequestResponse,
} from '../types/accessRequests';

async function readErrorMessage(response: Response): Promise<string> {
    try {
        const payload = await response.json() as { error?: string; title?: string };
        return payload.error ?? payload.title ?? 'Unable to submit access request.';
    } catch {
        return 'Unable to submit access request.';
    }
}

export async function submitAccessRequest(request: CreateAccessRequestRequest): Promise<AccessRequestSubmissionResult> {
    const response = await fetch('/api/access-requests', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
    });

    if (response.status === 202 || response.status === 409) {
        const payload = await response.json() as PublicAccessRequestResponse;
        return {
            request: payload,
            duplicate: response.status === 409,
        };
    }

    throw new Error(await readErrorMessage(response));
}