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
        private NaokaConfig _naokaConfig;

        public void Setup(NaokaConfig config)
        {
            _naokaConfig = config;
        }
        
        /// <summary>
        /// SendRatelimiterValues sends event 34 to the target actorNr in order to configure the client rate-limiter.
        /// </summary>
        /// <param name="actorNr">The actorNr of the target player.</param>
        /// <param name="ratelimitValues">A dictionary of the event code, and its rate-limit per second.</param>
        /// <param name="ratelimiterActive">Whether the rate-limiter is active.</param>
        public void SendRatelimiterValues(int actorNr, Dictionary<byte, int> ratelimitValues, bool ratelimiterActive)
        {
            var data = Util.EventDataWrapper(0, new Dictionary<byte, object>()
            {
                {0, ratelimitValues},
                {2, ratelimiterActive},
            });
            
            _naokaConfig.Host.BroadcastEvent(
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
            var jwtKeys = _naokaConfig.ActorsInternalProps[actorNr].JwtProperties;
            var authoritativeUserDict = Util.ParseJwtPropertiesUser(jwtKeys.User);
            var authoritativeAvatarDict = Util.ParseJwtPropertiesAvatar(jwtKeys.AvatarDict);
            var authoritativeFAvatarDict = Util.ParseJwtPropertiesAvatar(jwtKeys.FavatarDict);
            
            newProperties = new Hashtable();
            var temporaryPropertiesHt = new Hashtable()
            {
                {"inVRMode", false},
                {"showSocialRank", true},
                {"steamUserId", "0"},
                {"modTag", null},
                {"isInvisible", false},
                {"canModerateInstance", false}
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

                temporaryPropertiesHt["modTag"] = currentProperties["modTag"];
            }

            if (currentProperties.Contains("isInvisible") && (bool)currentProperties["isInvisible"])
            {
                if (!(jwtKeys.User.Tags.Contains("admin_moderator") || jwtKeys.User.DeveloperType == "internal"))
                {
                    error = "Can't set isInvisible";
                    return;
                }

                temporaryPropertiesHt["isInvisible"] = currentProperties["isInvisible"];
            }

            if (currentProperties.Contains("avatarEyeHeight"))
                temporaryPropertiesHt["avatarEyeHeight"] = currentProperties["avatarEyeHeight"];

            if (currentProperties.Contains("showSocialRank"))
                temporaryPropertiesHt["showSocialRank"] = (bool)currentProperties["showSocialRank"];

            if (currentProperties.Contains("inVRMode"))
                temporaryPropertiesHt["inVRMode"] = (bool) currentProperties["inVRMode"];
            
            temporaryPropertiesHt["user"] = authoritativeUserDict;
            temporaryPropertiesHt["avatarDict"] = authoritativeAvatarDict;
            temporaryPropertiesHt["favatarDict"] = authoritativeFAvatarDict;

            newProperties = temporaryPropertiesHt;
            error = "";
        }

        public void SendProperties(Hashtable props, int ActorNr)
        {
            _naokaConfig.Host.BroadcastEvent(0, ActorNr, 0, 42, Util.EventDataWrapper(ActorNr, props), 0);
        }

        /// <summary>
        ///     Used by UserRecordUpdate (EvCode 40) to pull partial actor properties from the API.
        /// </summary>
        /// <param name="actorNr">The id of the actor to update the properties of.</param>
        /// <param name="userId">The actor's api user id.</param>
        public void PullPartialActorProperties(int actorNr, string userId)
        {
            var currentProperties = _naokaConfig.Host.GameActors.First(x => x.ActorNr == actorNr).Properties.GetProperties();
            var currentIp = _naokaConfig.ActorsInternalProps[actorNr].JwtProperties.Ip;
            
            var requestUri = $"{_naokaConfig.ApiConfig["ApiUrl"]}/api/1/photon/user?secret={_naokaConfig.ApiConfig["PhotonSecret"]}&userId={userId}";
            var apiResponse = new HttpClient().GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;

            var newProperties = JsonConvert.DeserializeObject<PhotonValidateJoinJWTResponse>(apiResponse);
            if (newProperties != null && newProperties.Ip == "notset")
                newProperties.Ip = currentIp;

            _naokaConfig.ActorsInternalProps[actorNr].JwtProperties = newProperties;
            PrepareProperties(actorNr, currentProperties, out var newPropertiesToSet, out var error);

            if (!string.IsNullOrWhiteSpace(error))
            {
                return;
            }
            _naokaConfig.Host.SetProperties(actorNr, newPropertiesToSet, null, true);
            SendProperties(newPropertiesToSet, actorNr);
        }


        public void AnnouncePhysBonesPermissions()
        {
            var roomPermissions = new List<int[]>();
            foreach (var actor in _naokaConfig.ActorsInternalProps)
            {
                var actorAllowedUsers = actor.Value.ActorsAllowedToInteract;
                actorAllowedUsers.Add(actor.Value
                    .ActorNr); // The last element of the array is the actor associated with it.
                roomPermissions.Add(actorAllowedUsers.ToArray());
            }

            _naokaConfig.Host.BroadcastEvent(0, 0, 0, 60, Util.EventDataWrapper(0, roomPermissions.ToArray()), 0);
        }

        /// <summary>
        ///     Validation function for JWTs used during Room creation and joining.
        /// </summary>
        /// <param name="token">The JWT token provided by the client.</param>
        /// <param name="onCreate">Whether to return additional information (world author, instance creator, capacity)</param>
        /// <returns></returns>
        public PhotonValidateJoinJWTResponse ValidateJoinJwt(string token, bool onCreate = false)
        {
            var requestUri = $"{_naokaConfig.ApiConfig["ApiUrl"]}/api/1/photon/validateJoin?secret={_naokaConfig.ApiConfig["PhotonSecret"]}&roomId={_naokaConfig.RuntimeConfig["gameId"]}&jwt={token}";
            if (onCreate) requestUri += "&onCreate=true";
            
            var apiResponse = new HttpClient().GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
            return JsonConvert.DeserializeObject<PhotonValidateJoinJWTResponse>(apiResponse);
        }

        /// <summary>
        ///     This method runs a timer for the client's built-in rate-limiter update tick.
        /// </summary>
        public async void RunEvent35Timer()
        {
            while (true)
            {
                foreach (var a in _naokaConfig.ActorsInternalProps)
                { // TODO: Look into Event 35's triggers.
                    _naokaConfig.Host.BroadcastEvent(
                        new List<int> {a.Key},
                        0,
                        35,
                        Util.EventDataWrapper(0, null),
                        0
                    );
                }

                await Task.Delay(1000);
            }
        }
    }
}
