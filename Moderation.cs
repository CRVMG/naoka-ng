using System.Collections.Generic;

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
        
        /// <summary>
        /// SendRequestToAllForActor sends a Request_PlayerMods for the player to all players in the room.
        /// </summary>
        /// <param name="actorId"></param>
        public void SendRequestToAllForActor(int actorId)
        {
            foreach (var actor in _naokaConfig.ActorsInternalProps)
            {
                if (actor.Key != actorId)
                {
                    var u = (_naokaConfig.ActorsInternalProps[actorId].Id).Substring(4, 6);
                    var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
                    {
                        {ExecutiveActionPacket.Type, ExecutiveActionTypes.Request_PlayerMods},
                        {ExecutiveActionPacket.Main_Property, new []{ u }}
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
        }
        
        /// <summary>
        /// SendRequestToActor sends a Request_PlayerMods for every player in the room to the player.
        /// </summary>
        /// <param name="actorId"></param>
        public void SendRequestToActor(int actorId)
        {
            List<string> listOfUsers = new List<string>();
            foreach (var u in _naokaConfig.ActorsInternalProps)
            {
                if (u.Key != actorId)
                {
                    var uid = u.Value.Id.Substring(4, 6);
                    listOfUsers.Add(uid);
                }
            }

            var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
            {
                {ExecutiveActionPacket.Type, ExecutiveActionTypes.Request_PlayerMods},
                {ExecutiveActionPacket.Main_Property, listOfUsers.ToArray()}
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
                {ExecutiveActionPacket.Type, ExecutiveActionTypes.Reply_PlayerMods},
                {ExecutiveActionPacket.Target_User, actorId},
                {ExecutiveActionPacket.Blocked_Users, isBlocked},
                {ExecutiveActionPacket.Muted_Users, isMuted}
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
                {ExecutiveActionPacket.Type, ExecutiveActionTypes.Warn},
                {ExecutiveActionPacket.Heading, heading},
                {ExecutiveActionPacket.Message, message}
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
                { ExecutiveActionPacket.Type, ExecutiveActionTypes.Mic_Off },
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