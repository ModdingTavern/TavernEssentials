using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.Client.LowLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using HarmonyLib;

namespace TavernEssentials.Board
{
    internal sealed class ServersGroupsClient : IGroupsClient
    {
        internal readonly IGroupsClient Inner;
        readonly JoinedGroups _joined;
        readonly PersonalGroups _personal;

        internal ServersGroupsClient(IGroupsClient inner, LowLevelApiClient api)
        {
            Inner = inner;
            _joined = ServersPanel.NewJoinedGroups(api);
            _personal = ServersPanel.NewPersonalGroups(api);
        }

        public JoinedGroups Joined { get { return _joined; } }
        public PersonalGroups Personal { get { return _personal ?? Inner.Personal; } }

        public GroupDiscovery Discovery { get { return Inner.Discovery; } }
        public GroupCreator CreateGroup { get { return Inner.CreateGroup; } }

        public Task<bool> HasPermissionsInGroup(int groupIdentifier, int userIdentifier, GroupPermissions[] permissions)
        {
            if (ServersPanel.IsOurs(groupIdentifier)) return Task.FromResult(false);
            return Inner.HasPermissionsInGroup(groupIdentifier, userIdentifier, permissions);
        }

        public Task<IEnumerable<GroupPermissions>> GetUserPermissionsForGroup(int groupIdentifier, int userIdentifier)
        {
            if (ServersPanel.IsOurs(groupIdentifier)) return Task.FromResult<IEnumerable<GroupPermissions>>(new GroupPermissions[0]);
            return Inner.GetUserPermissionsForGroup(groupIdentifier, userIdentifier);
        }
    }

    internal sealed class MyServersPages : IPagedResult<JoinedGroupManager>
    {
        readonly LowLevelApiClient _api;
        IEnumerable<JoinedGroupManager> _last = new JoinedGroupManager[0];

        internal MyServersPages(LowLevelApiClient api)
        {
            _api = api;
        }

        public bool HasNext { get { return false; } }
        public IEnumerable<JoinedGroupManager> Results { get { return _last; } }

        public Task<IEnumerable<JoinedGroupManager>> GetAllAsync()
        {
            List<JoinedGroupManager> list = new List<JoinedGroupManager>();
            try
            {
                foreach (CommunityList.Entry e in MyServers.Joined(CommunityList.Current))
                {
                    JoinedGroupManager g = ServersPanel.Manager(e, _api);
                    if (g != null) list.Add(g);
                }
            }
            catch (Exception e)
            {
                Log.Warn("servers page: " + e.Message);
            }
            _last = list;
            return Task.FromResult<IEnumerable<JoinedGroupManager>>(list);
        }

        public Task<IEnumerable<JoinedGroupManager>> GetNextAsync()
        {
            return GetAllAsync();
        }
    }

    internal sealed class NoPages<T> : IPagedResult<T>
    {
        public bool HasNext { get { return false; } }
        public IEnumerable<T> Results { get { return new T[0]; } }
        public Task<IEnumerable<T>> GetAllAsync() { return Task.FromResult<IEnumerable<T>>(new T[0]); }
        public Task<IEnumerable<T>> GetNextAsync() { return GetAllAsync(); }
    }

    internal static class ServersPanel
    {
        const string ApiField = "apiClient";
        const string GroupsField = "<Groups>k__BackingField";
        const string PageField = "page";
        const string ListApiField = "api";
        const int MemberRole = GroupRoleInfo.DefaultRoleIdentifier;

        static FieldInfo _apiField;
        static ConstructorInfo _managerCtor;
        static ConstructorInfo _personalCtor;
        static bool _broken;
        static object _wrapped;

        internal static bool Broken { get { return _broken; } }

        internal static bool IsOurs(int groupIdentifier)
        {
            return groupIdentifier <= -2;
        }

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            MethodInfo target = AccessTools.Method(typeof(JoinedGroupManager), "LeaveGroup", new Type[0]);
            _managerCtor = AccessTools.Constructor(typeof(JoinedGroupManager),
                new Type[] { typeof(GroupInfoDetailed), typeof(GroupMemberInfo), typeof(LowLevelApiClient) });
            _personalCtor = AccessTools.Constructor(typeof(PersonalGroups), new Type[] { typeof(LowLevelApiClient) });
            if (_personalCtor == null) Log.Warn("inbox: PersonalGroups' constructor not found - its group invites still ask Alta.");
            if (target == null || _managerCtor == null)
            {
                Log.Warn("servers page seam missing: " + (target == null ? "JoinedGroupManager.LeaveGroup" : "JoinedGroupManager's constructor")
                    + " - the main menu's Servers page stays as it was.");
                return false;
            }
            try
            {
                h.Patch(target, new HarmonyMethod(typeof(ServersPanel).GetMethod("LeavePrefix", BindingFlags.Public | BindingFlags.Static)));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("servers page patch failed: " + e.Message);
                return false;
            }
        }

        public static bool LeavePrefix(JoinedGroupManager __instance, ref Task __result)
        {
            try
            {
                if (__instance == null || __instance.Group == null || !IsOurs(__instance.Group.Identifier)) return true;
                CommunityList.Snapshot snap = CommunityList.Current;
                CommunityList.Entry e = snap == null ? null : snap.ById(__instance.Group.Identifier);
                Log.Msg("My Servers: " + (e == null
                    ? "that server is no longer on the community list; delete its line in " + MyServers.Store.Path + " to take it off."
                    : MyServers.Leave(e)));
                __result = Task.FromResult(true);
                return false;
            }
            catch (Exception ex)
            {
                Log.Warn("leave server: " + ex.Message);
                __result = Task.FromResult(false);
                return false;
            }
        }

        internal static void Ensure()
        {
            if (_broken || _managerCtor == null) return;
            try
            {
                if (_apiField == null)
                {
                    _apiField = typeof(ApiAccess).GetField(ApiField, BindingFlags.NonPublic | BindingFlags.Static);
                    if (_apiField == null) { Fail("ApiAccess." + ApiField + " was not found"); return; }
                }
                IHighLevelApiClient client = _apiField.GetValue(null) as IHighLevelApiClient;
                if (client == null) return;

                FieldInfo slot = Field(client.GetType(), GroupsField);
                if (slot == null) { Fail(client.GetType().Name + " has no " + GroupsField); return; }
                IGroupsClient current = slot.GetValue(client) as IGroupsClient;
                if (current == null) return;
                if (current is ServersGroupsClient) { _wrapped = client; return; }

                slot.SetValue(client, new ServersGroupsClient(current, client.LowLevel));
                string again = _wrapped == null ? "" : _wrapped == client ? "the groups client was replaced; " : "the game built a new api client; ";
                _wrapped = client;
                Log.Msg(again + "the main menu's Servers page lists My Servers now, with Leave server (wrapped the game's "
                    + current.GetType().Name + ").");
            }
            catch (Exception e)
            {
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        internal static JoinedGroups NewJoinedGroups(LowLevelApiClient api)
        {
            JoinedGroups j = (JoinedGroups)FormatterServices.GetUninitializedObject(typeof(JoinedGroups));
            SetListField(j, PageField, new MyServersPages(api));
            SetListField(j, ListApiField, api);
            return j;
        }

        internal static PersonalGroups NewPersonalGroups(LowLevelApiClient api)
        {
            if (_personalCtor == null) return null;
            try
            {
                if (api == null) api = (LowLevelApiClient)FormatterServices.GetUninitializedObject(typeof(LowLevelApiClient));
                PersonalGroups p = (PersonalGroups)_personalCtor.Invoke(new object[] { api });
                SetListField(p.Invited, PageField, new NoPages<IncomingGroupInvite>());
                SetListField(p.Requested, PageField, new NoPages<OutgoingGroupJoinRequest>());
                return p;
            }
            catch (Exception e)
            {
                Log.Warn("inbox: group invites still ask Alta (" + e.GetType().Name + ": " + e.Message + ").");
                return null;
            }
        }

        internal static JoinedGroupManager Manager(CommunityList.Entry e, LowLevelApiClient api)
        {
            if (_managerCtor == null) return null;
            GroupRoleInfo role = new GroupRoleInfo();
            role.Identifier = MemberRole;
            role.Name = "Member";
            role.Permissions = new GroupPermissions[0];
            role.AllowedCommands = new string[0];

            GroupInfoDetailed g = new GroupInfoDetailed();
            g.Identifier = e.Info.Identifier;
            g.Name = e.Info.Name;
            g.Description = e.Info.Description;
            g.MemberCount = e.Info.CurrentPlayerCount;
            g.GroupType = e.HasPassword ? GroupType.Private : GroupType.Public;
            g.Tags = new List<string>();
            g.Servers = new GroupServerInfo[0];
            g.Roles = new GroupRoleInfo[] { role };
            g.CreatedAt = DateTime.UtcNow;

            GroupMemberInfo me = new GroupMemberInfo();
            me.GroupIdentifier = g.Identifier;
            me.RoleIdentifier = MemberRole;
            me.Type = GroupMemberType.Accepted;
            me.CreatedAt = DateTime.UtcNow;

            if (api == null) api = (LowLevelApiClient)FormatterServices.GetUninitializedObject(typeof(LowLevelApiClient));
            JoinedGroupManager m = (JoinedGroupManager)_managerCtor.Invoke(new object[] { g, me, api });
            SetListField(m.Requests, PageField, new NoPages<IncomingGroupJoinRequest>());
            SetListField(m.Invites, PageField, new NoPages<OutgoingGroupInvite>());
            SetListField(m.Bans, PageField, new NoPages<GroupBan>());
            SetListField(m.Info.Members, PageField, new NoPages<GroupMember>());
            return m;
        }

        static void SetListField(object list, string name, object value)
        {
            FieldInfo f = Field(list.GetType(), name);
            if (f == null) throw new MissingFieldException(list.GetType().Name, name);
            f.SetValue(list, value);
        }

        static FieldInfo Field(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        static void Fail(string why)
        {
            _broken = true;
            Log.Error("could not take over the game's groups client (" + why + "). The Servers page stays as it was; the My Servers board is not affected.");
        }
    }
}
