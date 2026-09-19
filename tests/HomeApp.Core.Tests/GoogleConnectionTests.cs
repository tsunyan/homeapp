using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using HomeApp.Core;
using HomeApp.Google;

namespace HomeApp.Core.Tests;

public sealed class GoogleConnectionTests
{
    [Fact]
    public async Task Restore_WithoutSavedAccount_DoesNotConnectOrWrite()
    {
        var store = new TestStore();
        var connection = new GoogleConnection(store);
        await connection.RestoreAsync(default);
        Assert.False(connection.IsConnected);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Restore_WithClientButNoToken_DoesNotSignInAutomatically()
    {
        var store = new TestStore();
        await store.StoreAsync("google-desktop-client", new ClientSecrets { ClientId = "test-client", ClientSecret = "test-value" });
        var connection = new GoogleConnection(store);
        await connection.RestoreAsync(default);
        Assert.False(connection.IsConnected);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task Restore_WithSavedToken_MakesBothProvidersAvailableWithoutBrowserOrNetwork()
    {
        var store = new TestStore();
        await store.StoreAsync("google-desktop-client", new ClientSecrets { ClientId = "test-client", ClientSecret = "test-value" });
        await store.StoreAsync("google-account", new TokenResponse { AccessToken = "test-access-token" });
        var connection = new GoogleConnection(store);
        await connection.RestoreAsync(default);
        Assert.True(connection.IsConnected);
        Assert.IsAssignableFrom<IMailProvider>(connection.Provider);
        Assert.IsAssignableFrom<ICalendarProvider>(connection.Provider);
        Assert.Equal(2, store.Writes);
    }

    [Fact]
    public async Task FirstConnection_RequiresDesktopClient_NoCredentialsAreWritten()
    {
        var store = new TestStore();
        var connection = new GoogleConnection(store);
        await Assert.ThrowsAsync<ConnectionException>(() => connection.ConnectAsync("", default));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task WebClient_IsRejectedBeforeBrowserOrTokenStorage()
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, """{"web":{"client_id":"test-client"}}""");
            var store = new TestStore();
            var connection = new GoogleConnection(store);
            await Assert.ThrowsAsync<ConnectionException>(() => connection.ConnectAsync(file, default));
            Assert.Equal(0, store.Writes);
        }
        finally { File.Delete(file); }
    }

    private sealed class TestStore : IDataStore
    {
        private readonly Dictionary<(Type, string), object> _values = [];
        public int Writes { get; private set; }
        public Task StoreAsync<T>(string key, T value) { Writes++; _values[(typeof(T), key)] = value!; return Task.CompletedTask; }
        public Task<T> GetAsync<T>(string key) => Task.FromResult(_values.TryGetValue((typeof(T), key), out var value) ? (T)value : default!);
        public Task DeleteAsync<T>(string key) { _values.Remove((typeof(T), key)); return Task.CompletedTask; }
        public Task ClearAsync() { _values.Clear(); return Task.CompletedTask; }
    }
}
