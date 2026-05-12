using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.QueryValidation;

/// <summary>
/// Scoped state for the current query validation decision.
/// </summary>
public sealed class QuestionValidationState
{
    private readonly List<QueryRecentMessage> _recentMessages = new();

    public string OriginalQuery { get; private set; } = string.Empty;

    public IReadOnlyList<QueryRecentMessage> RecentMessages => _recentMessages;

    public QuestionValidationResult? Result { get; private set; }

    public bool IsValidated => Result != null;

    public void Initialize(string query, IEnumerable<QueryRecentMessage>? recentMessages)
    {
        OriginalQuery = query ?? string.Empty;
        _recentMessages.Clear();
        Result = null;

        if (recentMessages == null)
        {
            return;
        }

        foreach (var message in recentMessages)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Content))
            {
                continue;
            }

            _recentMessages.Add(new QueryRecentMessage
            {
                Role = message.Role,
                Content = message.Content
            });
        }
    }

    public void Record(QuestionValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }
}
