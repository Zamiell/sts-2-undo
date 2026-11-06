using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Platform;

namespace Undo;

/// <summary>
/// A singleplayer service that reports itself as a replay while recorded events are fed back in. In that mode the game
/// waits for recorded choices and turn-end signals rather than asking the player. The whole run holds this one service,
/// so flipping the flag switches every synchronizer back to normal play at once.
/// </summary>
internal sealed class UndoNetService : INetGameService
{
    private readonly NetSingleplayerGameService _inner = new();

    public bool Replaying { get; set; }

    public NetGameType Type => Replaying ? NetGameType.Replay : _inner.Type;
    public ulong NetId => _inner.NetId;
    public bool IsConnected => _inner.IsConnected;
    public bool IsGameLoading => _inner.IsGameLoading;
    public PlatformType Platform => _inner.Platform;
    public PeerVersionInfo LocalVersion => _inner.LocalVersion;

    public event Action<NetErrorInfo>? Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public void SendMessage<T>(T message, ulong playerId) where T : INetMessage => _inner.SendMessage(message, playerId);
    public void SendMessage<T>(T message) where T : INetMessage => _inner.SendMessage(message);
    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage => _inner.RegisterMessageHandler(handler);
    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage => _inner.UnregisterMessageHandler(handler);
    public void Update() => _inner.Update();
    public void Disconnect(NetError reason, bool now = false) => _inner.Disconnect(reason, now);
    public ConnectionStats? GetStatsForPeer(ulong peerId) => _inner.GetStatsForPeer(peerId);
    public void SetGameLoading(bool isLoading) => _inner.SetGameLoading(isLoading);
    public void SetBufferMessages(bool bufferMessages) => _inner.SetBufferMessages(bufferMessages);
    public string? GetRawLobbyIdentifier() => _inner.GetRawLobbyIdentifier();
}
