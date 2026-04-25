using CommunityToolkit.Mvvm.ComponentModel;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// Row view model for the unified admin user-management surface.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by parent view model")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Used by compiled bindings")]
internal partial class UserManagementRowViewModel : ObservableObject {
    private static readonly TierLabel[] TierOptions = Enum.GetValues<TierLabel>();
    private UserManagementRowDto _row;

    [ObservableProperty]
    private TierLabel selectedTier;

    [ObservableProperty]
    private string actionReason = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public UserManagementRowViewModel(UserManagementRowDto row)
    {
        _row = row ?? throw new ArgumentNullException(nameof(row));
        selectedTier = row.AssignedTier ?? TierLabel.Trial;
    }

    public IReadOnlyList<TierLabel> AvailableTiers => TierOptions;

    public string RowId => _row.RowId;

    public string RowType => _row.RowType;

    public string? AccessRequestId => _row.AccessRequestId;

    public string? ManagedUserId => _row.ManagedUserId;

    public string Email => _row.Email;

    public IdentityProvider Provider => _row.Provider;

    public TierLabel? AssignedTier => _row.AssignedTier;

    public RequestDecisionState RequestDecisionState => _row.RequestDecisionState;

    public OnboardingExecutionState OnboardingExecutionState => _row.OnboardingExecutionState;

    public ManagedUserAccessState ManagedUserAccessState => _row.ManagedUserAccessState;

    public UserManagementRowState RowState => _row.RowState;

    public string CorrelationId => _row.CorrelationId;

    public DateTime? RequestedAtUtc => _row.RequestedAtUtc;

    public DateTime? ApprovedAtUtc => _row.ApprovedAtUtc;

    public DateTime? CancelledAtUtc => _row.CancelledAtUtc;

    public string? LastFailureCode => _row.LastFailureCode;

    public string? LastFailureMessage => _row.LastFailureMessage;

    public string RowVersion => _row.RowVersion;

    public string ProviderLabel => Provider.ToString();

    public string TierLabelText => AssignedTier?.ToString() ?? "Unassigned";

    public string StatusText => RowState switch {
        UserManagementRowState.PendingApproval => "Pending approval",
        UserManagementRowState.OnboardingInProgress => "Onboarding in progress",
        UserManagementRowState.OnboardingFailed => "Onboarding failed",
        UserManagementRowState.Active => "Active",
        UserManagementRowState.Cancelled => "Cancelled",
        _ => RowState.ToString()
    };

    public string RowTypeLabel => string.Equals(RowType, "ManagedUser", StringComparison.OrdinalIgnoreCase)
        ? "Managed user"
        : "Access request";

    public string TimelineText {
        get {
            if (CancelledAtUtc.HasValue)
            {
                return $"Cancelled {CancelledAtUtc.Value.ToLocalTime():g}";
            }

            if (ApprovedAtUtc.HasValue)
            {
                return $"Approved {ApprovedAtUtc.Value.ToLocalTime():g}";
            }

            if (RequestedAtUtc.HasValue)
            {
                return $"Requested {RequestedAtUtc.Value.ToLocalTime():g}";
            }

            return "No timeline information";
        }
    }

    public bool HasFailure => !string.IsNullOrWhiteSpace(LastFailureCode) || !string.IsNullOrWhiteSpace(LastFailureMessage);

    public string FailureText => string.Join(
        " - ",
        new[] { LastFailureCode, LastFailureMessage }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    public bool CanApprove => HasAllowedAction("Approve");

    public bool CanRetry => HasAllowedAction("RetryOnboarding");

    public bool CanChangeTier => HasAllowedAction("ChangeTier");

    public bool CanCancel => HasAllowedAction("Cancel");

    public bool ShowTierPicker => CanApprove || CanChangeTier;

    public bool ShowReasonEditor => CanChangeTier || CanCancel;

    public bool IsActionEnabled => !IsBusy;

    public string ReasonPlaceholder => CanCancel
        ? "Reason for cancellation"
        : "Optional reason for tier change";

    internal void Apply(UserManagementRowDto row)
    {
        _row = row ?? throw new ArgumentNullException(nameof(row));

        if (row.AssignedTier.HasValue)
        {
            SelectedTier = row.AssignedTier.Value;
        }

        NotifyRowChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsActionEnabled));
    }

    private bool HasAllowedAction(string action)
    {
        return _row.AllowedActions.Any(allowedAction =>
            string.Equals(allowedAction, action, StringComparison.OrdinalIgnoreCase));
    }

    private void NotifyRowChanged()
    {
        OnPropertyChanged(nameof(RowId));
        OnPropertyChanged(nameof(RowType));
        OnPropertyChanged(nameof(AccessRequestId));
        OnPropertyChanged(nameof(ManagedUserId));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(Provider));
        OnPropertyChanged(nameof(AssignedTier));
        OnPropertyChanged(nameof(RequestDecisionState));
        OnPropertyChanged(nameof(OnboardingExecutionState));
        OnPropertyChanged(nameof(ManagedUserAccessState));
        OnPropertyChanged(nameof(RowState));
        OnPropertyChanged(nameof(CorrelationId));
        OnPropertyChanged(nameof(RequestedAtUtc));
        OnPropertyChanged(nameof(ApprovedAtUtc));
        OnPropertyChanged(nameof(CancelledAtUtc));
        OnPropertyChanged(nameof(LastFailureCode));
        OnPropertyChanged(nameof(LastFailureMessage));
        OnPropertyChanged(nameof(RowVersion));
        OnPropertyChanged(nameof(ProviderLabel));
        OnPropertyChanged(nameof(TierLabelText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RowTypeLabel));
        OnPropertyChanged(nameof(TimelineText));
        OnPropertyChanged(nameof(HasFailure));
        OnPropertyChanged(nameof(FailureText));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanChangeTier));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(ShowTierPicker));
        OnPropertyChanged(nameof(ShowReasonEditor));
        OnPropertyChanged(nameof(ReasonPlaceholder));
    }
}