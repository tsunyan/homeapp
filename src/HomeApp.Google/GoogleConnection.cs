using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using HomeApp.Core;

namespace HomeApp.Google;

// A single Google account for now. Provider contracts do not depend on this choice.
public sealed class GoogleConnection(IDataStore store)
{
    private const string User = "google-account";
    private const string Client = "google-desktop-client";
    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/calendar.events.readonly"
    ];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private UserCredential? _credential;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    public GoogleContentProvider? Provider { get; private set; }
    public bool IsConnected => Provider is not null;

    public async Task RestoreAsync(CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct);
        try
        {
            var client = await store.GetAsync<ClientSecrets>(Client);
            var token = await store.GetAsync<TokenResponse>(User);
            if (client is not null && token is not null) SetCredential(CreateCredential(client, token));
        }
        finally { _tokenGate.Release(); }
    }

    private UserCredential CreateCredential(ClientSecrets client, TokenResponse token) => new(
        new GoogleAuthorizationCodeFlow(new() { ClientSecrets = client, Scopes = Scopes, DataStore = store }), User, token);

    public async Task ConnectAsync(string clientJsonPath, CancellationToken ct)
    {
        ClientSecrets? client;
        if (string.IsNullOrWhiteSpace(clientJsonPath)) client = await store.GetAsync<ClientSecrets>(Client);
        else
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(clientJsonPath.Trim().Trim('"'), ct));
            if (!json.RootElement.TryGetProperty("installed", out var installed))
                throw new ConnectionException("デスクトップアプリ用のOAuthクライアントJSONを選んでください。");
            client = new ClientSecrets
            {
                ClientId = installed.GetProperty("client_id").GetString(),
                ClientSecret = installed.GetProperty("client_secret").GetString()
            };
        }
        if (string.IsNullOrWhiteSpace(client?.ClientId) || string.IsNullOrWhiteSpace(client.ClientSecret))
            throw new ConnectionException("初回接続にはGoogle Cloudで作成したOAuthクライアントJSONが必要です。");

        // Authenticate in memory first: a cancelled login must not replace the saved account.
        var pending = new MemoryDataStore();
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new GoogleAuthorizationCodeFlow.Initializer { ClientSecrets = client }, Scopes, User,
            usePkce: true, ct, pending);
        if (credential.Token.Scope is { Length: > 0 } granted && Scopes.Any(s => !granted.Split(' ').Contains(s)))
        {
            credential.Flow.Dispose();
            throw new ConnectionException("Gmailとカレンダーの両方の読み取り権限を許可してください。");
        }
        // Switch to the encrypted store so automatic token refreshes are persisted there too.
        try
        {
            await _tokenGate.WaitAsync(ct);
            try
            {
                await store.StoreAsync(Client, client);
                await store.StoreAsync(User, credential.Token);
                SetCredential(CreateCredential(client, credential.Token));
            }
            finally { _tokenGate.Release(); }
        }
        finally { credential.Flow.Dispose(); }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct);
        var credential = _credential;
        SetCredential(null);
        // Stop refreshes before deleting the local token. Failure to revoke is reported separately.
        try
        {
            if (credential is not null) await credential.RevokeTokenAsync(ct);
        }
        finally
        {
            credential?.Flow.Dispose();
            try { await store.DeleteAsync<TokenResponse>(User); }
            finally { _tokenGate.Release(); }
        }
    }

    private void SetCredential(UserCredential? credential)
    {
        var previous = _credential;
        _credential = credential;
        Provider = credential is null ? null : new GoogleContentProvider(Http, async ct =>
        {
            await _tokenGate.WaitAsync(ct);
            try
            {
                if (!ReferenceEquals(_credential, credential)) throw new OperationCanceledException(ct);
                return await credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
            }
            catch (TokenResponseException) { throw new ConnectionException("Googleの認証が切れています。接続設定から再接続してください。"); }
            finally { _tokenGate.Release(); }
        });
        if (credential is not null) previous?.Flow.Dispose();
    }

    private sealed class MemoryDataStore : IDataStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task StoreAsync<T>(string key, T value) { _values[key] = value!; return Task.CompletedTask; }
        public Task DeleteAsync<T>(string key) { _values.Remove(key); return Task.CompletedTask; }
        public Task<T> GetAsync<T>(string key) => Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : default!);
        public Task ClearAsync() { _values.Clear(); return Task.CompletedTask; }
    }
}
