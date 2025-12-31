using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Persistence.Repositories;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace MotorcycleRAG.MobileApp.ViewModels;

public partial class ConversationListViewModel : ObservableObject
{
    private readonly IConversationRepository _conversationRepository;
    private CancellationTokenSource? _searchCts;

    public ConversationListViewModel(IConversationRepository conversationRepository)
    {
        _conversationRepository = conversationRepository;
    }

    private ObservableCollection<ConversationEntity> _conversations = new();
    public ObservableCollection<ConversationEntity> Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                OnSearchQueryChanged(value);
            }
        }
    }

    private void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(500, token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            // Ensure we run on UI thread if updating collection
            await MainThread.InvokeOnMainThreadAsync(SearchAsync);
        }, token);
    }

    [RelayCommand]
    public async Task LoadConversationsAsync()
    {
        var items = await _conversationRepository.GetAllAsync();
        Conversations.Clear();
        foreach (var item in items)
        {
            Conversations.Add(item);
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await LoadConversationsAsync();
            return;
        }

        var items = await _conversationRepository.SearchAsync(SearchQuery);
        Conversations.Clear();
        foreach (var item in items)
        {
            Conversations.Add(item);
        }
    }

    [RelayCommand]
    public async Task DeleteConversationAsync(ConversationEntity conversation)
    {
        if (conversation == null) return;

        await _conversationRepository.DeleteAsync(conversation.Id);
        Conversations.Remove(conversation);
    }

    [RelayCommand]
    public async Task NewConversationAsync()
    {
        // Navigate to ChatPage with a new conversation context
        // For now, we just navigate to ChatPage. The ChatPage/ViewModel should handle creating a new session if no ID is passed.
        // Assuming route "ChatPage" is registered.
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("ChatPage");
        }
    }

    [RelayCommand]
    public async Task OpenConversationAsync(ConversationEntity conversation)
    {
        if (conversation == null) return;

        // Navigate to ChatPage with conversation ID
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync($"ChatPage?conversationId={conversation.Id}");
        }
    }

    [RelayCommand]
    public async Task GoToProfileAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("UserMemoryPage");
        }
    }
}
