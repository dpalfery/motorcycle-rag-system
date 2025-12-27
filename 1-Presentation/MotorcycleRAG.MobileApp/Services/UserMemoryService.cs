using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Persistence.Repositories;

namespace MotorcycleRAG.MobileApp.Services
{
    public class UserMemoryService : IUserMemoryService
    {
        private readonly IUserMemoryRepository _repository;
        private readonly Dictionary<string, Regex> _extractionPatterns;

        public UserMemoryService(IUserMemoryRepository repository)
        {
            _repository = repository;
            _extractionPatterns = new Dictionary<string, Regex>
            {
                { "motorcycles_owned", new Regex(@"I own a (.+)", RegexOptions.IgnoreCase) },
                { "riding_style", new Regex(@"I prefer (.+) riding", RegexOptions.IgnoreCase) },
                { "expertise_level", new Regex(@"I am an? (.+) rider", RegexOptions.IgnoreCase) },
                { "maintenance_preference", new Regex(@"I do my own maintenance", RegexOptions.IgnoreCase) }
            };
        }

        public async Task<List<UserMemory>> GetActiveMemoriesAsync()
        {
            var entities = await _repository.GetActiveMemoriesAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task ExtractFromConversationAsync(string conversationId, string messageContent)
        {
            foreach (var pattern in _extractionPatterns)
            {
                var match = pattern.Value.Match(messageContent);
                if (match.Success)
                {
                    string value;
                    if (pattern.Key == "maintenance_preference")
                    {
                        value = "does own maintenance";
                    }
                    else
                    {
                        value = match.Groups[1].Value.Trim();
                    }

                    await AddOrUpdateMemoryAsync(pattern.Key, value, conversationId);
                }
            }
        }

        public async Task<UserMemory> AddMemoryAsync(string category, string value, string sourceConversationId)
        {
            return await AddOrUpdateMemoryAsync(category, value, sourceConversationId);
        }

        public async Task UpdateMemoryAsync(UserMemory memory)
        {
            var entity = await _repository.GetByCategoryAsync(memory.Category);
            if (entity != null && entity.Id == memory.Id)
            {
                entity.Value = memory.Value;
                entity.UpdatedAt = DateTime.UtcNow.ToString("O");
                await _repository.UpdateAsync(entity);
            }
        }

        public async Task DeactivateMemoryAsync(string id)
        {
            await _repository.DeactivateAsync(id);
        }

        public async Task DeleteMemoryAsync(string id)
        {
            await _repository.DeleteAsync(id);
        }

        public async Task ClearAllMemoriesAsync()
        {
            var memories = await _repository.GetActiveMemoriesAsync();
            foreach (var memory in memories)
            {
                await _repository.DeactivateAsync(memory.Id);
            }
        }

        private async Task<UserMemory> AddOrUpdateMemoryAsync(string category, string value, string sourceConversationId)
        {
            // Check for existing memory in this category
            var existing = await _repository.GetByCategoryAsync(category);
            if (existing != null)
            {
                // If value is different, deactivate old and create new
                if (existing.Value != value)
                {
                    await _repository.DeactivateAsync(existing.Id);
                }
                else
                {
                    // If value is same, just return existing (maybe update timestamp?)
                    return ToModel(existing);
                }
            }

            var newEntity = new UserMemoryEntity
            {
                Id = Guid.NewGuid().ToString(),
                Category = category,
                Value = value,
                SourceConversationId = sourceConversationId,
                ExtractedAt = DateTime.UtcNow.ToString("O"),
                IsActive = 1
            };

            await _repository.InsertAsync(newEntity);
            return ToModel(newEntity);
        }

        private static UserMemory ToModel(UserMemoryEntity entity)
        {
            return new UserMemory
            {
                Id = entity.Id,
                Category = entity.Category,
                Value = entity.Value,
                SourceConversationId = entity.SourceConversationId,
                ExtractedAt = DateTime.Parse(entity.ExtractedAt),
                UpdatedAt = entity.UpdatedAt != null ? DateTime.Parse(entity.UpdatedAt) : null,
                IsActive = entity.IsActive == 1
            };
        }
    }
}
