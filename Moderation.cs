using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Photon.Hive.Plugin;

namespace NaokaGo
{
    public class Moderation
    {
        private NaokaConfig _naokaConfig;

        public void Setup(NaokaConfig config)
        {
            _naokaConfig = config;
        }

        /// <summary>
        /// HandleModerationEvent is called by OnRaiseEvent when the event code is `33`. It is meant to clean up the core switch statement present in `NaokaGo.cs`.
        /// </summary>
        /// <param name="info"></param>
        /// <returns>A boolean which indicates whether the execution ended cleanly.</returns>
        public bool HandleModerationEvent(IRaiseEventCallInfo info)
        {
            _naokaConfig.Logger.Warn($"Received Moderation event from {info.ActorNr}:\n" +
                                     $"{JsonConvert.SerializeObject(info.Request.Data, Formatting.Indented)}");
            var eventData = (Dictionary<byte, object>)info.Request.Parameters[245];

            switch ((byte)eventData[ExecutiveActionPacket.Type])
            {
                case ExecutiveActionTypes.Request_PlayerMods:
                {
                    // This is a reply to the server's request.
                    // It contains a String[][]:
                    //   - []string: blockedUsers
                    //   - []string: mutedUsers
                    // The strings are the first six characters of the user's ID, exclusive of `usr_`

                    var blockedUsers = ((string[][])eventData[ExecutiveActionPacket.Main_Property])[0];
                    var mutedUsers = ((string[][])eventData[ExecutiveActionPacket.Main_Property])[1];

                    foreach (var actor in _naokaConfig.ActorsInternalProps)
                        if (actor.Value.ActorNr != info.ActorNr)
                        {
                            var u = actor.Value.Id.Substring(4, 6);
                            var isBlocked = blockedUsers.Contains(u);
                            var isMuted = mutedUsers.Contains(u);

                            if (isBlocked || isMuted)
                            {
                                var moderation = _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.FirstOrDefault(moderatedActor => moderatedActor.ActorNr == actor.Key) ?? // skipcq: CS-R1048
                                                 new UserModeration
                                                 {
                                                     ActorNr = actor.Key,
                                                     Id = actor.Value.Id
                                                 };
                                moderation.IsMuted = isMuted;
                                moderation.IsBlocked = isBlocked;

                                _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.RemoveAll(moderatedActor => moderatedActor.ActorNr == actor.Key);
                                _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.Add(moderation);
                            }
                            
                            SendReply(info.ActorNr, actor.Key, isBlocked,
                                isMuted);
                        }

                    break;
                }
                case ExecutiveActionTypes.Kick:
                {
                    var userId = _naokaConfig.ActorsInternalProps[info.ActorNr].Id;
                    var isStaff = _naokaConfig.ActorsInternalProps[info.ActorNr].JwtProperties.User.Tags
                        .Contains("admin_moderator");
                    if (!isStaff && ((string)_naokaConfig.RuntimeConfig["worldAuthor"] != userId ||
                                     (string)_naokaConfig.RuntimeConfig["instanceCreator"] != userId))
                    {
                        // ATTN (api): In public instances, the instance creator **has** to be empty.
                        info.Fail("Not allowed to kick.");
                        return false;
                    }

                    var target = _naokaConfig.ActorsInternalProps.FirstOrDefault(actor =>
                        actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                    if (target.Key == 0)
                    {
                        _naokaConfig.Logger.Info(
                            $"Could not find target user ({eventData[ExecutiveActionPacket.Target_User]}) for ExecutiveAction Kick sent by {info.ActorNr} ({_naokaConfig.ActorsInternalProps[info.ActorNr].Id})");
                        info.Fail("Could not find target user.");
                        return false;
                    }

                    _naokaConfig.Logger.Info(
                        $"Kicking {target.Key} ({target.Value.Id}) for ExecutiveAction Kick sent by {info.ActorNr} ({_naokaConfig.ActorsInternalProps[info.ActorNr].Id})");
                    SendExecutiveMessage(target.Key,
                        (string)eventData[ExecutiveActionPacket.Main_Property]);

                    break;
                }
                case ExecutiveActionTypes.Warn:
                {
                    var userId = _naokaConfig.ActorsInternalProps[info.ActorNr].Id;
                    var isStaff = _naokaConfig.ActorsInternalProps[info.ActorNr].JwtProperties.User.Tags
                        .Contains("admin_moderator");
                    if (!isStaff && ((string)_naokaConfig.RuntimeConfig["worldAuthor"] != userId ||
                                     (string)_naokaConfig.RuntimeConfig["instanceCreator"] != userId))
                    {
                        // ATTN (api): In public instances, the instance creator **has** to be empty.
                        info.Fail("Not allowed to warn.");
                        return false;
                    }

                    var target = _naokaConfig.ActorsInternalProps.FirstOrDefault(actor =>
                        actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                    if (target.Key == 0)
                    {
                        _naokaConfig.Logger.Info(
                            $"Could not find target user ({eventData[ExecutiveActionPacket.Target_User]}) for ExecutiveAction Warn sent by {info.ActorNr} ({_naokaConfig.ActorsInternalProps[info.ActorNr].Id})");
                        info.Fail("Could not find target user.");
                        return false;
                    }

                    SendWarn(target.Key, (string)eventData[ExecutiveActionPacket.Heading],
                        (string)eventData[ExecutiveActionPacket.Message]);
                    break;
                }
                case ExecutiveActionTypes.Mic_Off:
                {
                    var userId = _naokaConfig.ActorsInternalProps[info.ActorNr].Id;
                    var isStaff = _naokaConfig.ActorsInternalProps[info.ActorNr].JwtProperties.User.Tags
                        .Contains("admin_moderator");
                    if (!isStaff && ((string)_naokaConfig.RuntimeConfig["worldAuthor"] != userId ||
                                     (string)_naokaConfig.RuntimeConfig["instanceCreator"] != userId))
                    {
                        // ATTN (api): In public instances, the instance creator **has** to be empty.
                        info.Fail("Not allowed to turn the mic of other players off.");
                        return false;
                    }

                    var target = _naokaConfig.ActorsInternalProps.FirstOrDefault(actor =>
                        actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                    if (target.Key == 0)
                    {
                        _naokaConfig.Logger.Info(
                            $"Could not find target user ({eventData[ExecutiveActionPacket.Target_User]}) for ExecutiveAction MicOff sent by {info.ActorNr} ({_naokaConfig.ActorsInternalProps[info.ActorNr].Id})");
                        info.Fail("Could not find target user.");
                        return false;
                    }

                    SendMicOff(target.Key);
                    break;
                }
                case ExecutiveActionTypes.Mute_User:
                {
                    var targetUser = _naokaConfig.ActorsInternalProps.FirstOrDefault(actor =>
                        actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                    
                    if (targetUser.Key == 0)
                    {
                        info.Fail("Could not find target user.");
                        return false;
                    }
                    
                    var moderation = _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.FirstOrDefault(actor => actor.ActorNr == targetUser.Key) ??  // skipcq: CS-R1048
                                     new UserModeration
                                     {
                                        ActorNr = targetUser.Key,
                                        Id = targetUser.Value.Id
                                     };

                    moderation.IsMuted = (bool)eventData[ExecutiveActionPacket.Main_Property];
                    _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.RemoveAll(actor => actor.ActorNr == targetUser.Key);
                    _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.Add(moderation);
                    
                    SendModerationAction(info.ActorNr, targetUser.Key);

                    break;
                }
                case ExecutiveActionTypes.Block_User:
                {
                    var targetUser = _naokaConfig.ActorsInternalProps.FirstOrDefault(actor =>
                        actor.Value.Id == eventData[ExecutiveActionPacket.Target_User].ToString());
                    
                    if (targetUser.Key == 0)
                    {
                        info.Fail("Could not find target user.");
                        return false;
                    }
                    
                    var moderation = _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.FirstOrDefault(actor => actor.ActorNr == targetUser.Key) ??  // skipcq: CS-R1048
                                     new UserModeration
                                     {
                                         ActorNr = targetUser.Key,
                                         Id = targetUser.Value.Id
                                     };

                    moderation.IsBlocked = (bool)eventData[ExecutiveActionPacket.Main_Property];
                    _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.RemoveAll(actor => actor.ActorNr == targetUser.Key);
                    _naokaConfig.ActorsInternalProps[info.ActorNr].UserModerations.Add(moderation);
                    
                    SendModerationAction(info.ActorNr, targetUser.Key);
                    break;
                }
            }

            info.Cancel();
            return true;
        }

        /// <summary>
        /// SendExecutiveMessage forcibly disconnects a user from the room.
        /// </summary>
        /// <param name="actorNr"></param>
        /// <param name="message"></param>
        public void SendExecutiveMessage(int actorNr, string message)
        {
            _naokaConfig.Host.BroadcastEvent(
                new List<int> { actorNr },
                0,
                2,
                Util.EventDataWrapper(actorNr, message),
                0
            );

            // Forcibly kick from Room.
            _naokaConfig.Host.RemoveActor(actorNr, message);
        }

        public void SendModerationAction(int source, int target)
        {
            var sourceModeration = _naokaConfig.ActorsInternalProps[source].UserModerations.FirstOrDefault(actor => actor.ActorNr == target) ??
                                   new UserModeration
            {
                ActorNr = target,
                Id = _naokaConfig.ActorsInternalProps[target].Id
            };

            var targetModeration = _naokaConfig.ActorsInternalProps[target].UserModerations.FirstOrDefault(actor => actor.ActorNr == source) ??
                                   new UserModeration
                                   {
                                       ActorNr = source,
                                       Id = _naokaConfig.ActorsInternalProps[source].Id
                                   };
            
            // Send the source's moderation to the target.
            _naokaConfig.Host.BroadcastEvent(
                new List<int> { target },
                0,
                33,
                Util.EventDataWrapper(0, new Dictionary<byte, object>
                {
                    { ExecutiveActionPacket.Type, ExecutiveActionTypes.Reply_PlayerMods },
                    { ExecutiveActionPacket.Target_User, source },
                    { ExecutiveActionPacket.Muted_Users, sourceModeration.IsMuted },
                    { ExecutiveActionPacket.Blocked_Users, sourceModeration.IsBlocked }
                }),
                0
            );
            
            // Send the target's moderation to the source.
            _naokaConfig.Host.BroadcastEvent(
                new List<int> { source },
                0,
                33,
                Util.EventDataWrapper(0, new Dictionary<byte, object>
                {
                    { ExecutiveActionPacket.Type, ExecutiveActionTypes.Reply_PlayerMods },
                    { ExecutiveActionPacket.Target_User, target },
                    { ExecutiveActionPacket.Muted_Users, targetModeration.IsMuted },
                    { ExecutiveActionPacket.Blocked_Users, targetModeration.IsBlocked }
                }),
                0
            );
        }

        /// <summary>
        /// SendRequestToAllForActor sends a Request_PlayerMods for the player to all players in the room.
        /// </summary>
        /// <param name="actorId"></param>
        public void SendRequestToAllForActor(int actorId)
        {
            foreach (var actor in _naokaConfig.ActorsInternalProps)
                if (actor.Key != actorId)
                {
                    var u = _naokaConfig.ActorsInternalProps[actorId].Id.Substring(4, 6);
                    var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
                    {
                        { ExecutiveActionPacket.Type, ExecutiveActionTypes.Request_PlayerMods },
                        { ExecutiveActionPacket.Main_Property, new[] { u } }
                    });
                    _naokaConfig.Host.BroadcastEvent(
                        new List<int> { actor.Key },
                        0,
                        33,
                        data,
                        0
                    );
                }
        }

        /// <summary>
        /// SendRequestToActor sends a Request_PlayerMods for every player in the room to the player.
        /// </summary>
        /// <param name="actorId"></param>
        public void SendRequestToActor(int actorId)
        {
            var listOfUsers = new List<string>();
            foreach (var u in _naokaConfig.ActorsInternalProps)
                if (u.Key != actorId)
                {
                    var uid = u.Value.Id.Substring(4, 6);
                    listOfUsers.Add(uid);
                }

            var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
            {
                { ExecutiveActionPacket.Type, ExecutiveActionTypes.Request_PlayerMods },
                { ExecutiveActionPacket.Main_Property, listOfUsers.ToArray() }
            });
            _naokaConfig.Host.BroadcastEvent(
                new List<int> { actorId },
                0,
                33,
                data,
                0
            );
        }

        /// <summary>
        /// SendReply sends a Reply_PlayerMods to the player.
        /// </summary>
        /// <param name="actorId"></param>
        /// <param name="targetActor"></param>
        /// <param name="isBlocked"></param>
        /// <param name="isMuted"></param>
        public void SendReply(int actorId, int targetActor, bool isBlocked, bool isMuted)
        {
            var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
            {
                { ExecutiveActionPacket.Type, ExecutiveActionTypes.Reply_PlayerMods },
                { ExecutiveActionPacket.Target_User, actorId },
                { ExecutiveActionPacket.Blocked_Users, isBlocked },
                { ExecutiveActionPacket.Muted_Users, isMuted }
            });
            _naokaConfig.Host.BroadcastEvent(
                new List<int> { targetActor },
                0,
                33,
                data,
                0
            );
        }

        /// <summary>
        /// SendWarn sends a warning to the target player.
        /// </summary>
        /// <param name="targetActor">The actor number of the target player.</param>
        /// <param name="heading">The header of the warning message.</param>
        /// <param name="message">The actual message to display in the warning.</param>
        public void SendWarn(int targetActor, string heading, string message)
        {
            var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
            {
                { ExecutiveActionPacket.Type, ExecutiveActionTypes.Warn },
                { ExecutiveActionPacket.Heading, heading },
                { ExecutiveActionPacket.Message, message }
            });
            _naokaConfig.Host.BroadcastEvent(
                new List<int> { targetActor },
                0,
                33,
                data,
                0
            );
        }

        /// <summary>
        /// SendMicOff sends a request to turn off the player's microphone & enable push-to-talk.
        /// </summary>
        /// <param name="targetActor">The actor number of the target player.</param>
        public void SendMicOff(int targetActor)
        {
            var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
            {
                { ExecutiveActionPacket.Type, ExecutiveActionTypes.Mic_Off }
            });
            _naokaConfig.Host.BroadcastEvent(
                new List<int> { targetActor },
                0,
                33,
                data,
                0
            );
        }
    }
}