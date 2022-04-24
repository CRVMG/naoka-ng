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
            if (error != "")
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
            if (error != "")
            {
                info.Fail(error);
            }

            info.Request.ActorProperties = newProperties;
            info.Continue();
            
            _Moderation.SendRequestToActor(info.ActorNr);
            _Moderation.SendRequestToAllForActor(info.ActorNr);
            _EventLogic.SendRatelimiterValues(info.ActorNr, (Dictionary<byte,int>)naokaConfig.RuntimeConfig["configuredRateLimits"], (bool)naokaConfig.RuntimeConfig["ratelimiterActive"]);
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
            if (propertiesError != "")
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
            switch (info.Request.EvCode)
            {
                case 0: // Unused event code. Trigger an alert if someone attempts to use this.
                    naokaConfig.Logger.Warn($"[Naoka]: EvCode 0 (Unused) called by {info.ActorNr} ({naokaConfig.ActorsInternalProps[info.ActorNr].Id}), This is very suspicious.");
                    info.Fail("Unauthorized.");
                    return;

                case 1: // uSpeak
                    info.Continue();
                    break;

                case 2: // ExecutiveMessage
                    naokaConfig.Logger.Warn($"[Naoka]: EvCode 2 (ExecutiveMessage) called by {info.ActorNr} ({naokaConfig.ActorsInternalProps[info.ActorNr].Id}), This is very suspicious.");
                    info.Fail("Unauthorized.");
                    return;

                case 3: // SendPastEvents; Only sent to MasterClient upon join.
                case 4: // SyncEvents
                case 5: // InitialSyncFinished
                case 6: // ProcessEvent
                case 7: // Serialization
                    info.Continue();
                    break;

                case 8: // ReceiveInterval (Interest List)
                    // At this time, the interest list is not implemented.
                    // If someone wants to implement it, feel free to do so, and please make a merge request.

                    var parameters = new Dictionary<byte, object>();
                    int[] playerViews = naokaConfig.Host.GameActors.Select(actor => actor.ActorNr).ToArray();
                    parameters.Add(245, playerViews);
                    parameters.Add(254, 0);
                    SendParameters sendParams = default;
                    naokaConfig.Host.BroadcastEvent(
                        new List<int> {info.ActorNr},
                        0,
                        8,
                        parameters,
                        0, sendParams
                    );

                    info.Cancel();
                    break;

                case 9: // Udon, AV3Sync, BigData/ChairSync.
                case 15:
                    info.Continue();
                    break;

                case 33: // ExecutiveAction
                    Dictionary<byte,object> eventData = (Dictionary<byte,object>)info.Request.Parameters[245];

                    switch((byte) eventData[ExecutiveActionPacket.Type])
                    {
                        case ExecutiveActionTypes.Request_PlayerMods:
                        {
                            // This is a reply to the server's request.
                            // It contains a String[][]:
                            //   - []string: blockedUsers
                            //   - []string: mutedUsers
                            // The strings are the first six characters of the user's ID, exclusive of `usr_`

                            var blockedUsers = ((string[][]) eventData[ExecutiveActionPacket.Main_Property])[0];
                            var mutedUsers = ((string[][]) eventData[ExecutiveActionPacket.Main_Property])[1];

                            foreach (var actor in naokaConfig.ActorsInternalProps)
                            {
                                if (actor.Value.ActorNr != info.ActorNr)
                                {
                                    var u = (actor.Value.Id).Substring(4, 6);
                                    var isBlocked = blockedUsers.Contains(u);
                                    var isMuted = mutedUsers.Contains(u);

                                    _Moderation.SendReply(info.ActorNr, actor.Key, isBlocked,
                                        isMuted);
                                }
                            }

                            break;
                        }
                        case ExecutiveActionTypes.Kick:
                        {
                            var userId = naokaConfig.ActorsInternalProps[info.ActorNr].Id;
                            var isStaff = naokaConfig.ActorsInternalProps[info.ActorNr].JwtProperties.User.Tags.Contains("admin_moderator");
                            if (!isStaff || (string)naokaConfig.RuntimeConfig["worldAuthor"] != userId || (string)naokaConfig.RuntimeConfig["instanceCreator"] != userId)
                            {
                                // ATTN (api): In public instances, the instance creator **has** to be empty.
                                info.Fail("Not allowed to kick.");
                                return;
                            }
                            
                            var target = naokaConfig.ActorsInternalProps.FirstOrDefault(actor => actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                            if (target.Key == 0)
                            {
                                naokaConfig.Logger.Info($"Could not find target user ({eventData[ExecutiveActionPacket.Target_User]}) for ExecutiveAction Kick sent by {info.ActorNr} ({naokaConfig.ActorsInternalProps[info.ActorNr].Id})");
                                return;
                            }
                            
                            naokaConfig.Logger.Info($"Kicking {target.Key} ({target.Value.Id}) for ExecutiveAction Kick sent by {info.ActorNr} ({naokaConfig.ActorsInternalProps[info.ActorNr].Id})");
                            _Moderation.SendExecutiveMessage(target.Key, (string)eventData[ExecutiveActionPacket.Main_Property]);

                            return;
                        }
                        case ExecutiveActionTypes.Warn:
                        {
                            var userId = naokaConfig.ActorsInternalProps[info.ActorNr].Id;
                            var isStaff = naokaConfig.ActorsInternalProps[info.ActorNr].JwtProperties.User.Tags.Contains("admin_moderator");
                            if (!isStaff || (string)naokaConfig.RuntimeConfig["worldAuthor"] != userId || (string)naokaConfig.RuntimeConfig["instanceCreator"] != userId)
                            {
                                // ATTN (api): In public instances, the instance creator **has** to be empty.
                                info.Fail("Not allowed to warn.");
                                return;
                            }
                            
                            var target = naokaConfig.ActorsInternalProps.FirstOrDefault(actor => actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                            _Moderation.SendWarn(target.Key, (string)eventData[ExecutiveActionPacket.Heading], (string)eventData[ExecutiveActionPacket.Message]);
                            return;
                        }
                        case ExecutiveActionTypes.Mic_Off:
                        {
                            var userId = naokaConfig.ActorsInternalProps[info.ActorNr].Id;
                            var isStaff = naokaConfig.ActorsInternalProps[info.ActorNr].JwtProperties.User.Tags.Contains("admin_moderator");
                            if (!isStaff || (string)naokaConfig.RuntimeConfig["worldAuthor"] != userId || (string)naokaConfig.RuntimeConfig["instanceCreator"] != userId)
                            {
                                // ATTN (api): In public instances, the instance creator **has** to be empty.
                                info.Fail("Not allowed to turn the mic of other players off.");
                                return;
                            }
                            
                            var target = naokaConfig.ActorsInternalProps.FirstOrDefault(actor => actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                            _Moderation.SendMicOff(target.Key);
                            return;
                        }
                    }

                    info.Cancel();
                    break;

                case 40: // UserRecordUpdate
                    _EventLogic.PullPartialActorProperties(info.ActorNr, naokaConfig.ActorsInternalProps[info.ActorNr].Id);

                    info.Cancel();
                    break;

                case 42: // Custom implementation of SetProperties
                    _EventLogic.PrepareProperties(info.ActorNr, (Hashtable)info.Request.Data, out var temporaryPropertiesHt, out var propertiesError);
                    if (propertiesError != "")
                    {
                        info.Fail(propertiesError);
                        return;
                    }
                    
                    // TODO: Disable older property setting. This behavior has been moved to 42.
                    naokaConfig.Host.SetProperties(info.ActorNr, temporaryPropertiesHt, null, true);
                    _EventLogic.SendProperties(temporaryPropertiesHt, info.ActorNr);
                    info.Cancel();
                    break;
                case 60: // PhysBones Permissions
                    info.Cancel();
                    return;
                default: // Unknown event code; Log and cancel.
                    if (info.Request.EvCode < 200)
                    {
                        naokaConfig.Logger.Warn($"[Naoka]: Unknown Event code `{info.Request.EvCode}`.");
                        naokaConfig.Logger.Warn($"{JsonConvert.SerializeObject(info.Request.Data, Formatting.Indented)}");
                        info.Cancel();
                    }
                    break;
            }

            if (info.Request.EvCode >= 200)
            {
                switch (info.Request.EvCode)
                {
                    case 202:
                        if ((string)((Hashtable)info.Request.Parameters[245])[(byte)0] != "VRCPlayer")
                        {
                            info.Fail("Only VRCPlayer can be spawned.");
                            return;
                        }
                        if ((string)((Hashtable)info.Request.Parameters[245])[(byte)0] == "VRCPlayer" && naokaConfig.ActorsInternalProps[info.ActorNr].Instantiated) {
                            info.Fail("Already Instantiated");
                            return;
                        }

                        if ((string) ((Hashtable) info.Request.Parameters[245])[(byte)0] == "VRCPlayer")
                            naokaConfig.ActorsInternalProps[info.ActorNr].Instantiated = true;
                        info.Request.Cache = CacheOperations.AddToRoomCache;
                        
                        // Force instantiation at 0,0,0 by removing the Vec3 and Quaternion types from the request.
                        ((Hashtable)info.Request.Parameters[245]).Remove((byte)1);
                        ((Hashtable)info.Request.Parameters[245]).Remove((byte)2);

                        break;
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
