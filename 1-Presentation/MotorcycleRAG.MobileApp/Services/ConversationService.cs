using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Persistence.Mappers;
using MotorcycleRAG.MobileApp.Persistence.Repositories;

namespace MotorcycleRAG.MobileApp.Services
{
    public class ConversationService : IConversationService
    {
        private readonly IConversationRepository _conversationRepository;
        private readonly IMessageRepository _messageRepository;
        private readonly IApiClient _apiClient;
        private readonly IUserMemoryService _userMemoryService;

        public ConversationService(
            IConversationRepository conversationRepository,
            IMessageRepository messageRepository,
            IApiClient apiClient,
            IUserMemoryService userMemoryService)
        {
            _conversationRepository = conversationRepository;
            _messageRepository = messageRepository;
            _apiClient = apiClient;
            _userMemoryService = userMemoryService;
        }

        public async Task<ConversationSession> CreateConversationAsync()
        {
            var session = new ConversationSession
            {
                Id = Guid.NewGuid().ToString(),
                Title = "New Conversation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = ConversationStatus.Active,
                Messages = new List<ChatMessage>()
            };

            var entity = ConversationMapper.ToEntity(session);
            await _conversationRepository.InsertAsync(entity);

            return session;
        }

        public async Task<ConversationSession?> GetConversationAsync(Guid conversationId)
        {
            var entity = await _conversationRepository.GetByIdAsync(conversationId.ToString());
            if (entity == null)
                return null;

            var session = ConversationMapper.ToModel(entity);
            var messageEntities = await _messageRepository.GetByConversationIdAsync(conversationId.ToString());

            session.Messages = messageEntities
                .Select(MessageMapper.ToModel)
                .OrderBy(m => m.Timestamp)
                .ToList();

            return session;
        }

        public async Task<List<ConversationSession>> GetConversationsAsync()
        {
            var entities = await _conversationRepository.GetAllAsync();
            return entities
                .Select(ConversationMapper.ToModel)
                .OrderByDescending(c => c.UpdatedAt)
                .ToList();
        }

        public async Task DeleteConversationAsync(Guid conversationId)
        {
            await _messageRepository.DeleteByConversationIdAsync(conversationId.ToString());
            await _conversationRepository.DeleteAsync(conversationId.ToString());
        }

        public async Task<ChatMessage> SendMessageAsync(Guid conversationId, string messageText)
        {
            // 1. Create and save user message
            var userMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversationId.ToString(),
                Sender = MessageSender.User,
                Content = messageText,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Sent
            };

            await _messageRepository.InsertAsync(MessageMapper.ToEntity(userMessage));

            // 2. Update conversation timestamp
            var conversation = await _conversationRepository.GetByIdAsync(conversationId.ToString());
            if (conversation != null)
            {
                conversation.UpdatedAt = DateTime.UtcNow.ToString("O");

                // Auto-title if it's the default title
                if (conversation.Title == "New Conversation")
                {
                    conversation.Title = messageText.Length > 50
                        ? messageText.Substring(0, 47) + "..."
                        : messageText;
                }

                await _conversationRepository.UpdateAsync(conversation);
            }

            // 3. Call API
            // Extract user memory from current message
            await _userMemoryService.ExtractFromConversationAsync(conversationId.ToString(), messageText);

            // Get active memories
            var activeMemories = await _userMemoryService.GetActiveMemoriesAsync();
            var memoryDict = activeMemories.ToDictionary(m => m.Category, m => (object)m.Value);

            // Get previous queries for context
            var previousMessages = await _messageRepository.GetByConversationIdAsync(conversationId.ToString());
            var previousQueries = previousMessages
                .Where(m => m.Sender == (int)MessageSender.User)
                .OrderBy(m => m.Timestamp)
                .Select(m => m.Content)
                .TakeLast(5) // Limit context window
                .ToList();

            var request = new QueryRequest
            {
                Query = messageText,
                Context = new QueryContext
                {
                    SessionId = conversationId.ToString(),
                    PreviousQueries = previousQueries,
                    UserMemory = memoryDict
                }
            };

            var response = await _apiClient.QueryAsync(request);

            // 4. Create assistant message
            var assistantMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversationId.ToString(),
                Sender = MessageSender.System,
                Content = response.Response,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Sent,
                QueryId = response.QueryId,
                Citations = MapCitations(response.Sources)
            };

            // 5. Save assistant message
            await _messageRepository.InsertAsync(MessageMapper.ToEntity(assistantMessage));

            // TODO: Save citations to CitationRepository if needed (not in MVP scope for persistence yet, but good to have)

            return assistantMessage;
        }

        public async Task SaveMessageAsync(Guid conversationId, ChatMessage message)
        {
            await _messageRepository.InsertAsync(MessageMapper.ToEntity(message));
        }

        private List<SourceCitation> MapCitations(List<SearchResult> searchResults)
        {
            if (searchResults == null) return new List<SourceCitation>();

            return searchResults.Select(r => new SourceCitation
            {
                Id = Guid.NewGuid().ToString(),
                Title = r.Source?.SourceName ?? "Unknown Source",
                Url = r.Source?.SourceUrl,
                DocumentId = r.Source?.DocumentId,
                Type = DetermineSourceType(r.Source?.AgentType),
                IsTappable = !string.IsNullOrEmpty(r.Source?.SourceUrl) || !string.IsNullOrEmpty(r.Source?.DocumentId)
            }).ToList();
        }

        private SourceType DetermineSourceType(string? agentType)
        {
            if (string.IsNullOrEmpty(agentType)) return SourceType.WebSource;

            return agentType.ToLowerInvariant() switch
            {
                "pdf" => SourceType.PdfManual,
                "web" => SourceType.WebSource,
                _ => SourceType.WebSource
            };
        }
    }
}
