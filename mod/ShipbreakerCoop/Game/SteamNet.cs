using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ShipbreakerCoop.Core;
using Steamworks;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// ISteamNetworkingMessages transport on the game's own AppID (1161580). The game's SteamManager
    /// already runs SteamAPI.RunCallbacks() every frame, so Callback&lt;T&gt; works.
    /// Only peers that pass AcceptFilter (lobby members / the host) get a session.
    /// </summary>
    internal sealed class SteamTransport : ITransport
    {
        const int Channel = 0;
        const int SendUnreliable = 0, SendNoNagle = 1, SendReliable = 8;

        public Func<ulong, bool> AcceptFilter = _ => false;
        public string Name => "steam";
        readonly IntPtr[] mMsgs = new IntPtr[64];
        readonly Queue<KeyValuePair<ulong, byte[]>> mQueue = new Queue<KeyValuePair<ulong, byte[]>>();
        Callback<SteamNetworkingMessagesSessionRequest_t> mSessionRequest;
        Callback<SteamNetworkingMessagesSessionFailed_t> mSessionFailed;
        public int SendErrors { get; private set; }

        // The shipped (older) Steamworks.NET only has an instance Release() on a struct copy, which can't
        // free the native message. The game's steam_api64.dll exports the flat call (checked with ctypes).
        [DllImport("steam_api64", EntryPoint = "SteamAPI_SteamNetworkingMessage_t_Release", CallingConvention = CallingConvention.Cdecl)]
        static extern void NativeRelease(IntPtr msg);

        public SteamTransport()
        {
            mSessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
            mSessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
        }

        void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t r)
        {
            var who = r.m_identityRemote;
            ulong id = who.GetSteamID64();
            bool ok = AcceptFilter(id);
            Plugin.Log.LogInfo($"steam session request from {id}: {(ok ? "accepted" : "refused (not in our lobby)")}");
            if (ok) SteamNetworkingMessages.AcceptSessionWithUser(ref who);
        }

        void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t f)
        {
            Plugin.Log.LogWarning($"steam session failed: {f.m_info.m_eEndReason} {f.m_info.m_szEndDebug}");
        }

        public void Send(ulong peer, byte[] data, bool reliable)
        {
            var id = new SteamNetworkingIdentity();
            id.SetSteamID64(peer);
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                int flags = (reliable ? SendReliable : SendUnreliable) | SendNoNagle;
                var res = SteamNetworkingMessages.SendMessageToUser(ref id, h.AddrOfPinnedObject(), (uint)data.Length, flags, Channel);
                if (res != EResult.k_EResultOK) SendErrors++;
            }
            finally { h.Free(); }
        }

        public void Tick()
        {
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, mMsgs, mMsgs.Length);
            for (int i = 0; i < n; i++)
            {
                try
                {
                    var msg = (SteamNetworkingMessage_t)Marshal.PtrToStructure(mMsgs[i], typeof(SteamNetworkingMessage_t));
                    var bytes = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, bytes, 0, msg.m_cbSize);
                    mQueue.Enqueue(new KeyValuePair<ulong, byte[]>(msg.m_identityPeer.GetSteamID64(), bytes));
                }
                finally { NativeRelease(mMsgs[i]); }
            }
        }

        public bool TryReceive(out ulong peer, out byte[] data)
        {
            if (mQueue.Count == 0) { peer = 0; data = null; return false; }
            var kv = mQueue.Dequeue(); peer = kv.Key; data = kv.Value; return true;
        }

        public string Describe(ulong peer)
        {
            var name = SteamFriends.GetFriendPersonaName(new CSteamID(peer));
            return string.IsNullOrEmpty(name) || name == "[unknown]" ? peer.ToString() : $"{name} ({peer})";
        }

        public void Dispose()
        {
            mSessionRequest?.Dispose(); mSessionFailed?.Dispose();
            mSessionRequest = null; mSessionFailed = null;
        }
    }

    /// <summary>Friends-only Steam lobby: invites, Join Game, +connect_lobby. Lobby data key "sbcoop" = mod version.</summary>
    internal sealed class SteamLobby : IDisposable
    {
        public const string Key = "sbcoop";
        readonly string mVersion;
        CallResult<LobbyCreated_t> mCreated;
        CallResult<LobbyEnter_t> mEntered;
        Callback<GameLobbyJoinRequested_t> mJoinRequested;
        Callback<GameRichPresenceJoinRequested_t> mRichJoin;
        Callback<LobbyChatUpdate_t> mChatUpdate;
        ulong mHostId;

        public CSteamID Lobby { get; private set; } = CSteamID.Nil;
        public bool IsHost { get; private set; }
        public string Status = "";
        public event Action Hosted;
        public event Action<ulong> JoinedHost;      // host's SteamID
        public event Action<ulong> JoinRequested;   // lobby id from an invite while running
        public event Action HostLeft;               // client: the lobby owner left or dropped

        public SteamLobby(string version)
        {
            mVersion = version;
            mCreated = CallResult<LobbyCreated_t>.Create(OnCreated);
            mEntered = CallResult<LobbyEnter_t>.Create(OnEntered);
            mJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(e => JoinRequested?.Invoke(e.m_steamIDLobby.m_SteamID));
            mRichJoin = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichJoin);
            mChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnChatUpdate);
        }

        void OnChatUpdate(LobbyChatUpdate_t u)
        {
            const uint gone = 0x2 | 0x4 | 0x8 | 0x10; // left, disconnected, kicked, banned
            if (IsHost || Lobby == CSteamID.Nil || u.m_ulSteamIDLobby != Lobby.m_SteamID) return;
            if (u.m_ulSteamIDUserChanged == mHostId && (u.m_rgfChatMemberStateChange & gone) != 0)
            {
                Plugin.Log.LogInfo($"host left the lobby (state 0x{u.m_rgfChatMemberStateChange:x})");
                HostLeft?.Invoke();
            }
        }

        public static ulong MySteamId => SteamUser.GetSteamID().m_SteamID;
        public static string MyName => SteamFriends.GetPersonaName();

        public void Host()
        {
            IsHost = true; Status = "creating lobby...";
            mCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4));
        }

        public void Join(ulong lobbyId)
        {
            IsHost = false; Status = $"joining lobby {lobbyId}...";
            mEntered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }

        void OnCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Status = $"lobby create failed: {r.m_eResult}"; Plugin.Log.LogError(Status); return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, Key, mVersion);
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + Lobby.m_SteamID);
            SteamFriends.SetRichPresence("status", "Shipbreaking in co-op");
            Status = $"lobby {Lobby.m_SteamID}";
            Plugin.Log.LogInfo($"steam lobby created: {Lobby.m_SteamID}");
            Hosted?.Invoke();
        }

        void OnEntered(LobbyEnter_t r, bool ioFailure)
        {
            if (ioFailure || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            { Status = $"could not join lobby ({r.m_EChatRoomEnterResponse})"; Plugin.Log.LogError(Status); return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            var ver = SteamMatchmaking.GetLobbyData(Lobby, Key);
            if (string.IsNullOrEmpty(ver))
            {
                Status = "that lobby is not a Shipbreaker co-op game";
                Plugin.Log.LogError(Status); Leave(); return;
            }
            var owner = SteamMatchmaking.GetLobbyOwner(Lobby).m_SteamID;
            mHostId = owner;
            Status = $"in lobby {Lobby.m_SteamID}, host {owner}";
            Plugin.Log.LogInfo(Status + (ver != mVersion ? $" (host mod {ver}, ours {mVersion})" : ""));
            JoinedHost?.Invoke(owner);
        }

        void OnRichJoin(GameRichPresenceJoinRequested_t e)
        {
            var parts = (e.m_rgchConnect ?? "").Split(' ');
            if (parts.Length == 2 && parts[0] == "+connect_lobby" && ulong.TryParse(parts[1], out var id)) JoinRequested?.Invoke(id);
        }

        public bool IsMember(ulong steamId)
        {
            if (Lobby == CSteamID.Nil) return false;
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++) if (SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i).m_SteamID == steamId) return true;
            return false;
        }

        public void OpenInviteOverlay()
        {
            if (Lobby != CSteamID.Nil) SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
        }

        public void Leave()
        {
            if (Lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil;
            SteamFriends.ClearRichPresence();
        }

        public void Dispose()
        {
            Leave();
            mCreated?.Dispose(); mEntered?.Dispose(); mJoinRequested?.Dispose(); mRichJoin?.Dispose(); mChatUpdate?.Dispose();
        }
    }
}
