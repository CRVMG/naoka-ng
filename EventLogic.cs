using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NaokaGo
{
    /// <summary>
    ///     <c>EventLogic</c> contains all the logic required to work with raised events.
    /// </summary>
    public class EventLogic
    {
        public NaokaConfig naokaConfig;

        /// <summary>
        /// SendRatelimiterValues sends event 34 to the target actorNr in order to configure the client rate-limiter.
        /// </summary>
        /// <param name="actorNr">The actorNr of the target player.</param>
        /// <param name="ratelimitValues">A dictionary of the event code, and its rate-limit per second.</param>
        /// <param name="ratelimitBoolean">Unknown.</param>
        public void SendRatelimiterValues(int actorNr, Dictionary<byte, int> ratelimitValues, bool ratelimitBoolean)
        {
            var data = _EventDataWrapper(0, new Dictionary<byte, object>()
            {
                {0, ratelimitValues},
                {2, ratelimitBoolean},
            });
            
            naokaConfig.Host.BroadcastEvent(
                new List<int> { actorNr },
                0,
                34,
                data,
                0
            );
        }
        
        /// <summary>
        /// PrepareProperties is used to prepare an actor's property hashtable.
        /// </summary>
        /// <param name="actorNr">The actorNr present in the request.</param>
        /// <param name="currentProperties">The properties the actor is *sending*.</param>
        /// <param name="newProperties">The output variable that will be used to return the new hashtable of properties.</param>
        /// <param name="error">The output variable that will be used to return an error.</param>
        public void PrepareProperties(int actorNr, Hashtable currentProperties, out Hashtable newProperties, out string error)
        { 
            var jwtKeys =
                (PhotonValidateJoinJWTResponse) naokaConfig.ActorsInternalProps[actorNr]["jwtProperties"];
            var authoritativeUserDict = Util.ParseJwtPropertiesUser(jwtKeys.User);
            var authoritativeAvatarDict = Util.ParseJwtPropertiesAvatar(jwtKeys.AvatarDict);
            var authoritativeFAvatarDict = Util.ParseJwtPropertiesAvatar(jwtKeys.FavatarDict);
            
            newProperties = new Hashtable() { };
            
            var temporaryPropertiesHT = new Hashtable()
            {
                {"inVRMode", false},
                {"showSocialRank", true},
                {"steamUserId", "0"},
                {"modTag", null},
                {"isInvisible", false}
            };

            if (currentProperties.Contains("user"))
            {
                var customUserDict = (Dictionary<string, object>) currentProperties["user"];
                if (customUserDict.ContainsKey("displayName"))
                {
                    if ((string) customUserDict["displayName"] != jwtKeys.User.DisplayName)
                    {
                        if (!(jwtKeys.User.Tags.Contains("admin_moderator") ||
                              jwtKeys.User.DeveloperType == "internal"))
                        {
                            error = "Can't set user->displayName";
                            return;
                        }

                        authoritativeUserDict["displayName"] = customUserDict["displayName"];
                    }
                }
            }

            if (currentProperties.Contains("modTag") && currentProperties["modTag"] != null)
            {
                if (!(jwtKeys.User.Tags.Contains("admin_moderator") || jwtKeys.User.DeveloperType == "internal"))
                {
                    error = "Can't set modTag";
                    return;
                }

                temporaryPropertiesHT["modTag"] = currentProperties["modTag"];
            }

            if (currentProperties.Contains("isInvisible") && (bool)currentProperties["isInvisible"] != false)
            {
                if (!(jwtKeys.User.Tags.Contains("admin_moderator") || jwtKeys.User.DeveloperType == "internal"))
                {
                    error = "Can't set isInvisible";
                    return;
                }

                temporaryPropertiesHT["isInvisible"] = currentProperties["isInvisible"];
            }

            if (currentProperties.Contains("avatarEyeHeight"))
                temporaryPropertiesHT["avatarEyeHeight"] = currentProperties["avatarEyeHeight"];

            if (currentProperties.Contains("showSocialRank"))
                temporaryPropertiesHT["showSocialRank"] = (bool)currentProperties["showSocialRank"];

            if (currentProperties.Contains("inVRMode"))
                temporaryPropertiesHT["inVRMode"] = (bool) currentProperties["inVRMode"];
            
            temporaryPropertiesHT["user"] = authoritativeUserDict;
            temporaryPropertiesHT["avatarDict"] = authoritativeAvatarDict;
            temporaryPropertiesHT["favatarDict"] = authoritativeFAvatarDict;

            newProperties = temporaryPropertiesHT;
            error = "";
        }
        public void SendModerationRequestToAllForActor(int actorId)
        {
            foreach (var actor in naokaConfig.ActorsInternalProps)
            {
                if (actor.Key != actorId)
                {
                    var u = ((string) naokaConfig.ActorsInternalProps[actorId]["userId"]).Substring(4, 6);
                    var data = _EventDataWrapper(0, new Dictionary<byte, object>()
                    {
                        {0, (byte)20},
                        {3, new string[]{ u }}
                    });
                    naokaConfig.Host.BroadcastEvent(
                        new List<int> { actor.Key },
                        0,
                        33,
                        data,
                        0
                    );
                }
            }
        }
        
        public void SendModerationRequestToActor(int actorId)
        {
            List<string> listOfUsers = new List<string>();
            foreach (var u in naokaConfig.ActorsInternalProps)
            {
                if (u.Key != actorId)
                {
                    var uid = ((string) u.Value["userId"]).Substring(4, 6);
                    listOfUsers.Add(uid);
                }
            }

            var data = _EventDataWrapper(0, new Dictionary<byte, object>()
            {
                {0, (byte)20},
                {3, listOfUsers.ToArray()}
            });
            naokaConfig.Host.BroadcastEvent(
                new List<int> { actorId },
                0,
                33,
                data,
                0
            );
        }

        public void SendModerationReply(int actorId, int targetActor, bool isBlocked, bool isMuted)
        {
            var data = _EventDataWrapper(0, new Dictionary<byte, object>()
            {
                {0, (byte)21},
                {1, actorId},
                {10, isBlocked},
                {11, isMuted}
            });
            naokaConfig.Host.BroadcastEvent(
                new List<int> { targetActor },
                0,
                33,
                data,
                0
            );
        }
        
        /// <summary>
        ///     Wrapper for use with <c>IPluginHost.BroadcastEvent</c>; Wraps the data in a format that PUN expects.
        /// </summary>
        /// <param name="actorNr">The id of the sending actor.</param>
        /// <param name="data">The custom data to wrap.</param>
        /// <returns></returns>
        public Dictionary<byte, object> _EventDataWrapper(int actorNr, object data)
        {
            return new Dictionary<byte, object>
            {
                { 245, data },
                { 254, actorNr }
            };
        }

        /// <summary>
        ///     Authoritative disconnection function; Can be used for various types of kicks.
        /// </summary>
        /// <param name="actorNr">The id of the actor to disconnect.</param>
        /// <param name="message">The message to provide as a reason.</param>
        public void SendExecutiveMessage(int actorNr, string message)
        {
            naokaConfig.Host.BroadcastEvent(
                new List<int> { actorNr },
                0,
                2,
                _EventDataWrapper(actorNr, message),
                0
            );

            // Forcibly kick from Room.
            naokaConfig.Host.RemoveActor(actorNr, message);
        }

        /// <summary>
        ///     Used by UserRecordUpdate (EvCode 40) to pull partial actor properties from the API.
        /// </summary>
        /// <param name="actorNr">The id of the actor to update the properties of.</param>
        /// <param name="userId">The actor's api user id.</param>
        public void PullPartialActorProperties(int actorNr, string userId)
        {
            var currentProperties = naokaConfig.Host.GameActors.First(x => x.ActorNr == actorNr).Properties.GetProperties();
            var currentIp = ((PhotonValidateJoinJWTResponse) naokaConfig.ActorsInternalProps[actorNr]["jwtProperties"])
                .Ip;
            
            var requestUri = $"{naokaConfig.ApiConfig["ApiUrl"]}/api/1/photon/user?secret={naokaConfig.ApiConfig["PhotonSecret"]}&userId={userId}";
            var apiResponse = new HttpClient().GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;

            var newProperties = JsonConvert.DeserializeObject<PhotonValidateJoinJWTResponse>(apiResponse);
            if (newProperties != null && newProperties.Ip == "notset")
                newProperties.Ip = currentIp;

            naokaConfig.ActorsInternalProps[actorNr]["jwtProperties"] = newProperties;
            PrepareProperties(actorNr, currentProperties, out var newPropertiesToSet, out var error);

            if (error != "")
            {
                return;
            }
            naokaConfig.Host.SetProperties(actorNr, newPropertiesToSet, null, true);
        }

        /// <summary>
        ///     Validation function for JWTs used during Room creation and joining.
        /// </summary>
        /// <param name="token">The JWT token provided by the client.</param>
        /// <returns></returns>
        public PhotonValidateJoinJWTResponse ValidateJoinJWT(string token)
        {
            var requestUri = $"{naokaConfig.ApiConfig["ApiUrl"]}/api/1/photon/validateJoin?secret={naokaConfig.ApiConfig["PhotonSecret"]}&roomId={naokaConfig.RuntimeConfig["gameId"]}&jwt={token}";
            var apiResponse = new HttpClient().GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
            return JsonConvert.DeserializeObject<PhotonValidateJoinJWTResponse>(apiResponse);
        }

        public async void RunEvent8Timer()
        {
            while (true)
            {
                var data = new List<int>() { };
                foreach (var a in naokaConfig.ActorsInternalProps) data.Add(a.Key);
                foreach (var a in naokaConfig.ActorsInternalProps)
                {
                    naokaConfig.Host.BroadcastEvent(
                        new List<int> {a.Key},
                        0,
                        8,
                        _EventDataWrapper(0, data.ToArray()),
                        0
                    );
                }

                await Task.Delay(1000);
            }
        }

        public async void RunEvent35Timer()
        {
            while (true)
            {
                foreach (var a in naokaConfig.ActorsInternalProps)
                { // TODO: Look into Event 35's triggers, as well as its return. Pretty sure this should be an array.
                    naokaConfig.Host.BroadcastEvent(
                        new List<int> {a.Key},
                        0,
                        35,
                        _EventDataWrapper(0, null),
                        0
                    );
                }

                await Task.Delay(1000);
            }
        }
    }
}
