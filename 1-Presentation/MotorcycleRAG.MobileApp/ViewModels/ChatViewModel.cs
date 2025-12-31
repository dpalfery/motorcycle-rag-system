using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Networking;
using MotorcycleRAG.MobileApp.Exceptions;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Services;

namespace MotorcycleRAG.MobileApp.ViewModels
{
    public partial class ChatViewModel : ObservableObject, IQueryAttributable
    {
        private readonly IConversationService _conversationService;

        private Guid _conversationId;
        public Guid ConversationId
        {
            get => _conversationId;
            set => SetProperty(ref _conversationId, value);
        }

        private string _questionText = string.Empty;
        public string QuestionText
        {
            get => _questionText;
            set
            {
                if (SetProperty(ref _questionText, value))
                {
                    SendQuestionCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private bool _isSending;
        public bool IsSending
        {
            get => _isSending;
            set => SetProperty(ref _isSending, value);
        }

        private string _sessionId = string.Empty;
        public string SessionId
        {
            get => _sessionId;
            set => SetProperty(ref _sessionId, value);
        }

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public ChatViewModel(IConversationService conversationService)
        {
            _conversationService = conversationService;
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.ContainsKey("conversationId")) // Note: Query parameters are case-sensitive usually, but Shell handles it. Let's check case.
            {
                var idObj = query["conversationId"];
                if (idObj is string idString && Guid.TryParse(idString, out var id))
                {
                    ConversationId = id;
                    SessionId = id.ToString();
                    LoadMessagesAsync().ConfigureAwait(false);
                }
            }
        }

        [RelayCommand]
        private async Task StartNewChatAsync()
        {
            ConversationId = Guid.Empty;
            SessionId = string.Empty;
            Messages.Clear();
            // Optionally navigate to a clean state or just reset
        }

        private async Task LoadMessagesAsync()
        {
            if (ConversationId == Guid.Empty) return;

            try
            {
                var session = await _conversationService.GetConversationAsync(ConversationId);
                if (session != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        Messages.Clear();
                        foreach (var message in session.Messages)
                        {
                            Messages.Add(message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Handle error
                System.Diagnostics.Debug.WriteLine($"Error loading messages: {ex.Message}");
            }
        }

        [RelayCommand(CanExecute = nameof(CanSendQuestion))]
        private async Task SendQuestionAsync()
        {
            if (string.IsNullOrWhiteSpace(QuestionText)) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlertAsync("Offline", "No internet connection. Please check your settings.", "OK");
                }
                return;
            }

            var question = QuestionText;
            QuestionText = string.Empty; // Clear input immediately
            IsSending = true;

            // Create temporary user message for UI
            var userMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = ConversationId.ToString(),
                Sender = MessageSender.User,
                Content = question,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Sending
            };

            Messages.Add(userMessage);

            try
            {
                if (ConversationId == Guid.Empty)
                {
                    var session = await _conversationService.CreateConversationAsync();
                    ConversationId = Guid.Parse(session.Id);
                    SessionId = session.Id;
                    userMessage.ConversationId = session.Id;
                }

                var responseMessage = await _conversationService.SendMessageAsync(ConversationId, question);

                // Update user message status
                userMessage.Status = MessageStatus.Sent;

                // Add assistant message
                Messages.Add(responseMessage);
            }
            catch (RateLimitException ex)
            {
                userMessage.Status = MessageStatus.Failed;
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlertAsync("Limit Reached", $"You have reached your daily limit. Resets at {ex.ResetAt.ToLocalTime()}", "OK");
                }
            }
            catch (Exception ex)
            {
                userMessage.Status = MessageStatus.Failed;
                // Show error
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlertAsync("Error", $"Failed to send message: {ex.Message}", "OK");
                }
            }
            finally
            {
                IsSending = false;
            }
        }

        private bool CanSendQuestion()
        {
            return !string.IsNullOrWhiteSpace(QuestionText) && !IsSending;
        }

        [RelayCommand]
        private async Task CitationTappedAsync(SourceCitation citation)
        {
            if (citation == null) return;

            if (citation.Type == SourceType.WebSource && !string.IsNullOrEmpty(citation.Url))
            {
                try
                {
                    await Browser.Default.OpenAsync(citation.Url, BrowserLaunchMode.SystemPreferred);
                }
                catch (Exception)
                {
                    await Shell.Current.DisplayAlertAsync("Error", "Could not open link.", "OK");
                }
            }
            else if (citation.Type == SourceType.PdfManual)
            {
                if (!string.IsNullOrEmpty(citation.Url))
                {
#pragma warning disable CS8601 // Possible null reference assignment.
                    var navigationParameter = new Dictionary<string, object>();
                    navigationParameter["url"] = citation.Url ?? string.Empty;
                    navigationParameter["page"] = citation.PageNumber;
#pragma warning restore CS8601 // Possible null reference assignment.

                    await Shell.Current.GoToAsync("PdfViewerPage", navigationParameter);
                }
                else
                {
                    await Shell.Current.DisplayAlertAsync("PDF Citation", $"Page {citation.PageNumber} of {citation.Title} (URL missing)", "OK");
                }
            }
        }
    }
}
