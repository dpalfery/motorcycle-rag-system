using System.Threading.Tasks;
using SQLite;

namespace MotorcycleRAG.MobileApp.Persistence;

public class DatabaseBootstrap
{
    private readonly SQLiteAsyncConnection _connection;

    public DatabaseBootstrap(SQLiteAsyncConnection connection)
    {
        _connection = connection;
    }

    public async Task InitializeAsync()
    {
        await _connection.CreateTableAsync<Entities.ConversationEntity>();
        await _connection.CreateTableAsync<Entities.MessageEntity>();
        await _connection.CreateTableAsync<Entities.CitationEntity>();
        await _connection.CreateTableAsync<Entities.UserMemoryEntity>();
    }
}
