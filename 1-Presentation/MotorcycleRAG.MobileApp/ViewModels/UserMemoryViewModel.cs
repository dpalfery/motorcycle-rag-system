using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Services;

namespace MotorcycleRAG.MobileApp.ViewModels
{
    public partial class UserMemoryViewModel : ObservableObject
    {
        private readonly IUserMemoryService _userMemoryService;
        private readonly IStorageService _storageService;
        private readonly IApiClient _apiClient;

        public ObservableCollection<UserMemory> Memories { get; } = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private double _storageUsageMB;

        [ObservableProperty]
        private double _storageLimitMB = 100.0;

        [ObservableProperty]
        private double _storageUsagePercent;

        [ObservableProperty]
        private UserProfile _userProfile = new();

        [ObservableProperty]
        private double _dailyUsagePercent;

        public UserMemoryViewModel(IUserMemoryService userMemoryService, IStorageService storageService, IApiClient apiClient)
        {
            _userMemoryService = userMemoryService;
            _storageService = storageService;
            _apiClient = apiClient;
        }

        [RelayCommand]
        public async Task LoadMemoriesAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var memoriesTask = _userMemoryService.GetActiveMemoriesAsync();
                var usageTask = _storageService.GetUsageBytesAsync();
                var profileTask = _apiClient.GetUserProfileAsync();

                await Task.WhenAll(memoriesTask, usageTask, profileTask);

                var memories = await memoriesTask;
                Memories.Clear();
                foreach (var memory in memories)
                {
                    Memories.Add(memory);
                }

                var usageBytes = await usageTask;
                StorageUsageMB = Math.Round(usageBytes / (1024.0 * 1024.0), 2);
                StorageUsagePercent = StorageUsageMB / StorageLimitMB;

                UserProfile = await profileTask;
                if (UserProfile.DailyRequestLimit > 0)
                {
                    DailyUsagePercent = (double)UserProfile.RequestsUsedToday / UserProfile.DailyRequestLimit;
                }
                else
                {
                    DailyUsagePercent = 0;
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("Error", $"Failed to load profile data: {ex.Message}", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task DeleteMemoryAsync(UserMemory memory)
        {
            if (memory == null) return;

            bool confirm = await Shell.Current.DisplayAlertAsync("Confirm", $"Delete memory '{memory.Category}'?", "Yes", "No");
            if (!confirm) return;

            try
            {
                await _userMemoryService.DeleteMemoryAsync(memory.Id);
                Memories.Remove(memory);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("Error", $"Failed to delete memory: {ex.Message}", "OK");
            }
        }

        [RelayCommand]
        public async Task EditMemoryAsync(UserMemory memory)
        {
            if (memory == null) return;

            string result = await Shell.Current.DisplayPromptAsync("Edit Memory", $"Update value for {memory.Category}:", initialValue: memory.Value);
            if (string.IsNullOrWhiteSpace(result) || result == memory.Value) return;

            try
            {
                memory.Value = result;
                await _userMemoryService.UpdateMemoryAsync(memory);
                // Refresh list to ensure UI sync
                await LoadMemoriesAsync();
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("Error", $"Failed to update memory: {ex.Message}", "OK");
            }
        }
    }
}
