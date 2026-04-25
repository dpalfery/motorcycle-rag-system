export type IdentityProvider = 'Microsoft' | 'Google';

export type RequestDecisionState = 'Pending' | 'Approved' | 'Cancelled';

export type OnboardingExecutionState = 'NotStarted' | 'InProgress' | 'Completed' | 'Failed';

export type UserManagementRowState =
    | 'PendingApproval'
    | 'OnboardingInProgress'
    | 'OnboardingFailed'
    | 'Active'
    | 'Cancelled';

export type RequesterVisibleAccessRequestStatus =
    | 'PendingReview'
    | 'ApprovedReadyToSignIn'
    | 'OnboardingDelayed'
    | 'Cancelled';

export interface CreateAccessRequestRequest {
    email: string;
    provider: IdentityProvider;
}

export interface PublicAccessRequestResponse {
    requestId: string;
    email: string;
    provider: IdentityProvider;
    requesterVisibleStatus: RequesterVisibleAccessRequestStatus;
    statusMessage?: string;
    nextAction?: string;
    requestDecisionState: RequestDecisionState;
    onboardingExecutionState: OnboardingExecutionState;
    rowState: UserManagementRowState;
    requestedAtUtc: string;
    correlationId?: string;
    rowVersion?: string;
}

export interface AccessRequestSubmissionResult {
    request: PublicAccessRequestResponse;
    duplicate: boolean;
}