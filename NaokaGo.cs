using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Photon.Hive.Plugin;

namespace NaokaGo
{
    /// <summary>
    ///     Static configuration "global" for cleaner access.
    /// </summary>
    public class NaokaConfig
    {
        public IPluginLogger Logger;
        public IPluginHost Host;
        public Dictionary<string, string> Config;
        public Dictionary<string, string> ApiConfig;
        public Dictionary<string, object> RuntimeConfig; // Runtime configuration, including rate-limits, max actors per ip, etc.

        public readonly Dictionary<int, CustomActor> ActorsInternalProps = new Dictionary<int, CustomActor>();
    }

    public class NaokaGo : PluginBase
    {
        private readonly NaokaConfig naokaConfig = new NaokaConfig();
        private readonly EventLogic _EventLogic = new EventLogic();
        private readonly Moderation _Moderation = new Moderation();

        public override string Name => "Naoka";

        public override bool SetupInstance(IPluginHost host, Dictionary<string, string> config, out string errorMsg)
        {
            _EventLogic.Setup(naokaConfig);
            _Moderation.Setup(naokaConfig);
            naokaConfig.Logger = host.CreateLogger(Name);
            naokaConfig.Host = host;
            naokaConfig.Config = config;
            naokaConfig.ApiConfig = new Dictionary<string, string>
            {
                {"ApiUrl", config["ApiUrl"]},
                {"PhotonSecret", config["PhotonApiSecret"]}
            };
            naokaConfig.RuntimeConfig = new Dictionary<string, object>()
            {
                {"configuredRateLimits", new Dictionary<byte, int>()},
                {"ratelimiterActive", false},
                {"maxAccsPerIp", 5},
                {"capacity", 0},
                {"worldAuthor", ""},
                {"instanceCreator", ""}
            };

            var requestUri = $"{naokaConfig.ApiConfig["ApiUrl"]}/api/1/photon/getConfig?secret={naokaConfig.ApiConfig["PhotonSecret"]}";
            var apiResponse = new HttpClient().GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
            var remoteConfig = JsonConvert.DeserializeObject<PhotonRuntimeRemoteConfig>(apiResponse);


            if (remoteConfig != null)
            {
                foreach (var kvp in remoteConfig.RateLimitList)
                {
                    ((Dictionary<byte, int>) naokaConfig.RuntimeConfig["configuredRateLimits"])[(byte) kvp.Key] =
                        kvp.Value;
                }

                naokaConfig.RuntimeConfig["ratelimiterActive"] = remoteConfig.RatelimiterActive;
                naokaConfig.RuntimeConfig["maxAccsPerIp"] = remoteConfig.MaxAccsPerIp;
            }
            
            Task.Run(_EventLogic.RunEvent35Timer);

            return base.SetupInstance(host, config, out errorMsg);
        }

        /// <summary>
        /// OnCreateGame is called when a new instance is created. It validates the joiner's JoinJWT.
        /// It additionally ensures that the world capacity, world author, instance creator, etc. are set. 
        /// </summary>
        /// <param name="info"></param>
        public override void OnCreateGame(ICreateGameCallInfo info)
        {
            // The following statement prevents specific Photon-related exploits that can be used to soft-lock a room.
            // Thanks Meep!
            if (info.Request.SuppressRoomEvents || info.Request.RoomFlags % 64 == 0)
            {
                info.Fail("Not allowed to create a suppressed room.");
                return;
            }

            info.Request.RoomFlags = 35;

            naokaConfig.RuntimeConfig["gameId"] = info.Request.GameId;
            
            PhotonValidateJoinJWTResponse jwtValidationResult =
                _EventLogic.ValidateJoinJwt((string) ((Hashtable) info.Request.Parameters[248])[(byte) 2], true);
            bool tokenValid = jwtValidationResult.Valid;

            if (!tokenValid)
            {
                info.Fail("Invalid JWT presented.");
                return;
            }
            
            naokaConfig.RuntimeConfig["capacity"] = jwtValidationResult.WorldCapacity;
            naokaConfig.RuntimeConfig["worldAuthor"] = jwtValidationResult.WorldAuthor;
            naokaConfig.RuntimeConfig["instanceCreator"] = jwtValidationResult.InstanceCreator;
            
            var user = new CustomActor
            {
                ActorNr = 1,
                Id = jwtValidationResult.User.Id,
                Ip = jwtValidationResult.Ip,
                JwtProperties = jwtValidationResult
            };
            naokaConfig.ActorsInternalProps.Add(1, user);
            
            _EventLogic.PrepareProperties(1, info.Request.ActorProperties, out var newProperties, out var error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                info.Fail(error);
            }

            info.Request.ActorProperties = newProperties;
            info.Continue();
            _Moderation.SendRequestToActor(1);
            _EventLogic.SendRatelimiterValues(1, (Dictionary<byte,int>)naokaConfig.RuntimeConfig["configuredRateLimits"], (bool)naokaConfig.RuntimeConfig["ratelimiterActive"]);
        }
        
        /// <summary>
        /// OnJoin is called when a new actor joins the game. It validates the joiner's JoinJWT.
        /// It additionally performs additional checks to ensure that the joiner is not already in the game,
        /// the game does not exceed the maximum number of players, etc.
        /// </summary>
        /// <param name="info"></param>
        public override void OnJoin(IJoinGameCallInfo info)
        {
            
            PhotonValidateJoinJWTResponse jwtValidationResult = _EventLogic.ValidateJoinJwt((string)((Hashtable)info.Request.Parameters[248])[(byte)2]);
            bool tokenValid = jwtValidationResult.Valid;

            if (!tokenValid)
            {
                info.Fail("Invalid JWT presented.");
                return;
            }

            var isStaff = jwtValidationResult.User.Tags.Contains("admin_moderator");

            string userId = jwtValidationResult.User.Id;
            string ipAddress = jwtValidationResult.Ip;

            if (naokaConfig.Host.GameActors.Count >= (int)naokaConfig.RuntimeConfig["capacity"] * 2 &&
                (!isStaff || (string)naokaConfig.RuntimeConfig["instanceCreator"] != userId || 
                 (string)naokaConfig.RuntimeConfig["worldAuthor"] != userId))
            {
                // This comment serves as a sanity check for the above if statement;
                // If the room is above double the capacity, and the joiner is not a staff member,
                // the world author, or the instance creator, then the joiner is not allowed to join.
                info.Fail("Game is full.");
                return;
            }
            
            int ipAddressCount = 0;
            foreach (var actorInternalProp in naokaConfig.ActorsInternalProps)
            {
                if (actorInternalProp.Value.Id == userId && !isStaff)
                {
                    info.Fail("User is already in this room.");
                    return;
                }

                if (actorInternalProp.Value.Ip == ipAddress)
                    ++ipAddressCount;

                if (ipAddressCount > (int) naokaConfig.RuntimeConfig["maxAccsPerIp"] && !isStaff)
                {
                    info.Fail("Max. account limit per instance reached. You've been bapped.");
                    return;
                }
            }
            
            var user = new CustomActor
            {
                ActorNr = info.ActorNr,
                Id = userId,
                Ip = ipAddress,
                JwtProperties = jwtValidationResult
            };
            naokaConfig.ActorsInternalProps.Add(user.ActorNr, user);

            _EventLogic.PrepareProperties(info.ActorNr, info.Request.ActorProperties, out var newProperties, out var error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                info.Fail(error);
            }

            info.Request.ActorProperties = newProperties;
            info.Continue();
            
            _Moderation.SendRequestToActor(info.ActorNr);
            _Moderation.SendRequestToAllForActor(info.ActorNr);
            _EventLogic.SendRatelimiterValues(info.ActorNr, (Dictionary<byte,int>)naokaConfig.RuntimeConfig["configuredRateLimits"], (bool)naokaConfig.RuntimeConfig["ratelimiterActive"]);
            _EventLogic.AnnouncePhysBonesPermissions();
        }
        
        /// <summary>
        /// OnLeave is a post-hook for leaving the game. It removes the actor from the internal list of actors,
        /// and reduces the number of players in the game (locally and in the api).
        /// </summary>
        /// <param name="info"></param>
        public override void OnLeave(ILeaveGameCallInfo info)
        {
            var requestUri = $"{naokaConfig.ApiConfig["ApiUrl"]}/api/1/photon/playerLeft?secret={naokaConfig.ApiConfig["PhotonSecret"]}&roomId={naokaConfig.RuntimeConfig["gameId"]}&userId={naokaConfig.ActorsInternalProps[info.ActorNr].Id}";
            var apiResponse = new HttpClient().GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
            
            naokaConfig.ActorsInternalProps.Remove(info.ActorNr);
            info.Continue();
        }
        
        /// <summary>
        /// OnCloseGame is called when the instance is about to be removed from the server.
        /// It handles letting the API know that the instance is no longer available.
        /// </summary>
        /// <param name="info"></param>
        public override void OnCloseGame(ICloseGameCallInfo info)
        {
            var requestUri = $"{naokaConfig.ApiConfig["ApiUrl"]}/api/1/photon/gameClosed?secret={naokaConfig.ApiConfig["PhotonSecret"]}&roomId={naokaConfig.RuntimeConfig["gameId"]}";
            var apiResponse = new HttpClient().GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
            info.Continue();
        }

        /// <summary>
        /// BeforeSetProperties is a pre-hook for the client's SetProperties call. It validates the properties being set,
        /// and if they are valid, it prepares them for a broadcast.
        /// </summary>
        /// <param name="info"></param>
        public override void BeforeSetProperties(IBeforeSetPropertiesCallInfo info)
        {
            if (info.Request.ActorNumber == 0 || info.Request.ActorNumber != info.ActorNr)
            {
                naokaConfig.Host.RemoveActor(info.ActorNr, "*TeamSpeak Voice*: You have been kicked from the server.");
                info.Fail("*TeamSpeak Voice*: You have been kicked from the server.");
                return;
            }
            
            info.Request.Broadcast = true;

            _EventLogic.PrepareProperties(info.ActorNr, info.Request.Properties, out var temporaryPropertiesHt, out var propertiesError);
            if (!string.IsNullOrWhiteSpace(propertiesError))
            {
                info.Fail(propertiesError);
                return;
            }
            
            info.Request.Properties = temporaryPropertiesHt;
            info.Request.Parameters[251] = temporaryPropertiesHt; // Better be safe than sorry.
            
            info.Continue();
        }
        
        public override void OnSetProperties(ISetPropertiesCallInfo info)
        {
            info.Continue();
        }

        /// <summary>
        /// OnRaiseEvent is called every time an event is raised. Events are the main way the client communicates with
        /// the server.
        /// </summary>
        /// <param name="info"></param>
        public override void OnRaiseEvent(IRaiseEventCallInfo info)
        {
            info.Request.Cache = CacheOperations.DoNotCache;
            switch (info.Request.EvCode)
            {
                case 0: // Unused event code. Trigger an alert if someone attempts to use this.
                {
                    naokaConfig.Logger.Warn(
                        $"[Naoka]: EvCode 0 (Unused) called by {info.ActorNr} ({naokaConfig.ActorsInternalProps[info.ActorNr].Id}), This is very suspicious.");
                    info.Fail("Unauthorized.");
                    return;
                }

                case 1: // uSpeak
                {
                    info.Continue();
                    break;
                }

                case 2: // ExecutiveMessage
                {
                    naokaConfig.Logger.Warn(
                        $"[Naoka]: EvCode 2 (ExecutiveMessage) called by {info.ActorNr} ({naokaConfig.ActorsInternalProps[info.ActorNr].Id}), This is very suspicious.");
                    info.Fail("Unauthorized.");
                    return;
                }

                case 3: // SendPastEvents; Only sent to MasterClient upon join.
                case 4: // SyncEvents
                case 5: // InitialSyncFinished
                case 6: // ProcessEvent
                case 7: // Serialization
                {
                    info.Continue();
                    break;
                }

                case 8: // ReceiveInterval (Interest List)
                {
                    // At this time, the interest list is not implemented.
                    // If someone wants to implement it, feel free to do so, and please make a merge request.

                    var parameters = new Dictionary<byte, object>();
                    int[] playerViews = naokaConfig.Host.GameActors.Select(actor => actor.ActorNr).ToArray();
                    parameters.Add(245, playerViews);
                    parameters.Add(254, 0);
                    SendParameters sendParams = default;
                    naokaConfig.Host.BroadcastEvent(
                        new List<int> { info.ActorNr },
                        0,
                        8,
                        parameters,
                        0, sendParams
                    );

                    info.Cancel();
                    break;
                }

                case 9: // Udon, AV3Sync, BigData/ChairSync.
                case 15:
                {
                    info.Continue();
                    break;
                }

                case 33: // ExecutiveAction
                {
                    if (!_Moderation.HandleModerationEvent(info))
                    { // If the event was not handled cleanly (e.g.: failed), return instead of breaking.
                        return;
                    }

                    break;
                }

                case 40: // UserRecordUpdate
                {
                    _EventLogic.PullPartialActorProperties(info.ActorNr,
                        naokaConfig.ActorsInternalProps[info.ActorNr].Id);

                    info.Cancel();
                    break;
                }

                case 42: // Custom implementation of SetProperties
                {
                    _EventLogic.PrepareProperties(info.ActorNr, (Hashtable)info.Request.Data,
                        out var temporaryPropertiesHt, out var propertiesError);
                    if (!string.IsNullOrWhiteSpace(propertiesError))
                    {
                        info.Fail(propertiesError);
                        return;
                    }

                    // TODO: Disable older property setting. This behavior has been moved to 42.
                    naokaConfig.Host.SetProperties(info.ActorNr, temporaryPropertiesHt, null, true);
                    _EventLogic.SendProperties(temporaryPropertiesHt, info.ActorNr);
                    info.Cancel();
                    break;
                }
                case 60: // PhysBones Permissions
                {
                    var usersAllowedRequest = (string[])info.Request.Parameters[245];
                    var usersAllowed = usersAllowedRequest.Select(user =>
                        naokaConfig.ActorsInternalProps.FirstOrDefault(actor =>
                            actor.Value.Id == user && actor.Value.ActorNr != info.ActorNr).Key).ToList();

                    if (usersAllowedRequest.Contains(naokaConfig.ActorsInternalProps[info.ActorNr].Id))
                        usersAllowed.Add(info.ActorNr);
                    
                    naokaConfig.ActorsInternalProps[info.ActorNr].ActorsAllowedToInteract = usersAllowed;

                    _EventLogic.AnnouncePhysBonesPermissions();
                    info.Cancel();
                    return;
                }
                default: // Unknown event code; Log and cancel.
                {
                    if (info.Request.EvCode < 200)
                    {
                        naokaConfig.Logger.Warn($"[Naoka]: Unknown Event code `{info.Request.EvCode}`.");
                        naokaConfig.Logger.Warn(
                            $"{JsonConvert.SerializeObject(info.Request.Data, Formatting.Indented)}");
                        info.Cancel();
                    }

                    break;
                }
            }

            if (info.Request.EvCode >= 200)
            {
                switch (info.Request.EvCode) // no default -> info.Continue called below skipcq: CS-W1009
                {
                    case 202:
                    {
                        int viewIdsStart = info.ActorNr * 100000;
                        int[] allowedViewIds = new[]
                        {
                            viewIdsStart + 1, // VRCPlayer
                            viewIdsStart + 2, // uSpeak
                            viewIdsStart + 3, // PlayableController
                            viewIdsStart + 4, // BigData
                        };

                        if ((int)((Hashtable)info.Request.Parameters[245])[(byte)7] != allowedViewIds[0])
                        {
                            naokaConfig.Logger.Warn($"{info.ActorNr} tried to connect with an invalid main view id: {(int)((Hashtable)info.Request.Parameters[245])[(byte)7]}. Expected: {allowedViewIds[0]}");
                            info.Fail("Invalid view id.");
                            naokaConfig.Host.RemoveActor(info.ActorNr, "Invalid view id.");
                            return;
                        }

                        foreach (var viewId in (int[])((Hashtable)info.Request.Parameters[245])[(byte)4])
                        {
                            if (!allowedViewIds.Contains(viewId))
                            {
                                naokaConfig.Logger.Warn($"User {info.ActorNr} ({naokaConfig.ActorsInternalProps[info.ActorNr].Id}) tried to connect with an invalid view id: {viewId}.\n" +
                                                        $"Allowed view ids: {string.Join(", ", allowedViewIds)}");
                                info.Fail("Invalid view id.");
                                naokaConfig.Host.RemoveActor(info.ActorNr, "Invalid view id.");
                                return;
                            }
                        }

                        if ((string)((Hashtable)info.Request.Parameters[245])[(byte)0] != "VRCPlayer")
                        {
                            info.Fail("Only VRCPlayer can be spawned.");
                            return;
                        }

                        if ((string)((Hashtable)info.Request.Parameters[245])[(byte)0] == "VRCPlayer" &&
                            naokaConfig.ActorsInternalProps[info.ActorNr].Instantiated)
                        {
                            info.Fail("Already Instantiated");
                            return;
                        }

                        if ((string)((Hashtable)info.Request.Parameters[245])[(byte)0] == "VRCPlayer")
                            naokaConfig.ActorsInternalProps[info.ActorNr].Instantiated = true;
                        info.Request.Cache = CacheOperations.AddToRoomCache;

                        // Force instantiation at 0,0,0 by removing the Vec3 and Quaternion types from the request.
                        ((Hashtable)info.Request.Parameters[245]).Remove((byte)1);
                        ((Hashtable)info.Request.Parameters[245]).Remove((byte)2);

                        break;
                    }
                }
                info.Continue();
            }
        }

        private void LogEventEight(int actorNr, byte[] data)
        {
            var posInEvent8 = 0;

            var actorInterests = new Dictionary<Int32, Int16>();
            while (posInEvent8 < data.Length)
            {
                var viewid = BitConverter.ToInt32(data, posInEvent8);
                posInEvent8 += 4;
                var interest = BitConverter.ToInt16(data, posInEvent8);
                actorInterests.Add(viewid, interest);
                posInEvent8 += 2;
            }

            foreach (var kvp in actorInterests)
            {
                naokaConfig.Logger.Info($"[Naoka]:    {actorNr} set interest of ViewID {kvp.Key} to {kvp.Value}");
            }
        }
    }

    public class NaokaGoFactory : PluginFactoryBase
    {
        public override IGamePlugin CreatePlugin(string pluginName)
        {
            return new NaokaGo();
        }
    }
}
