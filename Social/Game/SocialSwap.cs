using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Responses.DTOs.Responses;

namespace TavernEssentials.Social
{
    internal sealed class WitnessSocialClient : ISocialClient
    {
        internal readonly ISocialClient Inner;

        internal WitnessSocialClient(ISocialClient inner)
        {
            Inner = inner;
        }

        public Task<FriendshipInfo> GetFriendship(int userIdentifier, int friendIdentifier)
        {
            return Task.FromResult<FriendshipInfo>(null);
        }

        public Task<FriendRequestResult> CreateFriendshipBetweenUsers(int userIdentifier, int friendIdentifier)
        {
            bool made = false;
            try { made = ServerSide.Witness(userIdentifier, friendIdentifier); }
            catch (Exception e) { Log.Error("witnessing a friend card failed: " + e); }

            FriendRequestResult r = new FriendRequestResult();
            if (made)
            {
                r.Result = FriendRequestedType.Befriended;
                r.Friendship = Accepted(friendIdentifier);
                r.ReverseFriendship = Accepted(userIdentifier);
            }
            else
            {
                r.Result = FriendRequestedType.RequestSent;
            }
            return Task.FromResult(r);
        }

        static FriendshipInfo Accepted(int identifier)
        {
            FriendshipInfo f = new FriendshipInfo();
            f.Identifier = identifier;
            f.CreatedAt = DateTime.UtcNow;
            f.IconIndex = 0;
            f.Type = FriendshipType.Accepted;
            return f;
        }

        public Task<FriendRequestedType> AddFriend(int friendIdentifier)
        {
            if (Inner == null) throw new InvalidOperationException("no social client");
            return Inner.AddFriend(friendIdentifier);
        }

        public Task RemoveFriend(int friendIdentifier)
        {
            if (Inner == null) throw new InvalidOperationException("no social client");
            return Inner.RemoveFriend(friendIdentifier);
        }

        public Task<IEnumerable<FriendshipInfo>> GetFriends()
        {
            return Inner == null ? Empty<FriendshipInfo>() : Inner.GetFriends();
        }

        public Task<IEnumerable<FriendshipInfo>> GetFriendsForUser(int userIdentifier)
        {
            return Empty<FriendshipInfo>();
        }

        public Task<IEnumerable<OculusFriendInfo>> GetOculusFriends(IEnumerable<ulong> oculusIds)
        {
            return Inner == null ? Empty<OculusFriendInfo>() : Inner.GetOculusFriends(oculusIds);
        }

        public Task<IEnumerable<FriendshipInfo>> GetFriendRequests()
        {
            return Inner == null ? Empty<FriendshipInfo>() : Inner.GetFriendRequests();
        }

        public Task<IEnumerable<FriendshipInfo>> GetSentFriendRequests()
        {
            return Inner == null ? Empty<FriendshipInfo>() : Inner.GetSentFriendRequests();
        }

        static Task<IEnumerable<T>> Empty<T>()
        {
            return Task.FromResult<IEnumerable<T>>(new T[0]);
        }
    }

    internal sealed class SocialSwap
    {
        internal delegate ISocialClient WrapFn(ISocialClient current);

        const string ApiField = "apiClient";
        const string BackingField = "<SocialClient>k__BackingField";

        internal static readonly SocialSwap Server = new SocialSwap(typeof(WitnessSocialClient),
            delegate(ISocialClient c) { return new WitnessSocialClient(c); },
            "friend cards are witnessed here now (wrapped the server's ",
            "could not take over the server's social client (", "). Friend cards stay as they were: no records.");

        internal static readonly SocialSwap Client = new SocialSwap(typeof(ListSocialClient),
            delegate(ISocialClient c) { return new ListSocialClient(c); },
            "the game's friends lists show your friends now (wrapped the game's ",
            "could not take over the game's social client (", "). The friends lists stay as the game has them offline.");

        static FieldInfo _api;

        readonly Type _ours;
        readonly WrapFn _wrap;
        readonly string _done, _failed, _failedEnd;
        bool _broken;
        object _wrapped;

        SocialSwap(Type ours, WrapFn wrap, string done, string failed, string failedEnd)
        {
            _ours = ours;
            _wrap = wrap;
            _done = done;
            _failed = failed;
            _failedEnd = failedEnd;
        }

        internal bool Broken { get { return _broken; } }

        internal void Ensure()
        {
            if (_broken) return;
            try
            {
                if (_api == null)
                {
                    _api = typeof(ApiAccess).GetField(ApiField, BindingFlags.NonPublic | BindingFlags.Static);
                    if (_api == null) { Fail("ApiAccess." + ApiField + " was not found"); return; }
                }
                object client = _api.GetValue(null);
                if (client == null) return;

                FieldInfo backing = Backing(client.GetType());
                if (backing == null) { Fail(client.GetType().Name + " has no " + BackingField); return; }
                ISocialClient current = backing.GetValue(client) as ISocialClient;
                if (current != null && _ours.IsInstanceOfType(current))
                {
                    _wrapped = client;
                    return;
                }
                backing.SetValue(client, _wrap(current));
                string again = _wrapped == null ? "" : _wrapped == client ? "the social client was replaced; " : "the game built a new API client; ";
                _wrapped = client;
                Log.Msg(again + _done + (current == null ? "empty social client slot" : current.GetType().Name) + ").");
            }
            catch (Exception e)
            {
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        internal bool Ready()
        {
            Ensure();
            if (_broken || _api == null) return false;
            try
            {
                object client = _api.GetValue(null);
                if (client == null) return false;
                FieldInfo backing = Backing(client.GetType());
                return backing != null && _ours.IsInstanceOfType(backing.GetValue(client));
            }
            catch (Exception)
            {
                return false;
            }
        }

        static FieldInfo Backing(Type t)
        {
            for (; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(BackingField, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        void Fail(string why)
        {
            _broken = true;
            Log.Error(_failed + why + _failedEnd);
        }
    }
}
