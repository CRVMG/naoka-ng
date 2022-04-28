using System.Collections.Generic;
using System.Text;

namespace NaokaGo
{
    public class CustomActor
    {
        public int ActorNr;
        public string Id;
        public string Ip;
        public PhotonValidateJoinJWTResponse JwtProperties;
        public bool Instantiated = false;
        public bool HasOverriddenUserProps = false;
        public Dictionary<string, object> OverriddenUserProperties = null;
        public List<int> ActorsAllowedToInteract = new List<int>();
    }
    
    /// <summary>
    ///     Enum for the flags provided in UserRecordUpdate. (EvCode 40)
    /// </summary>
    public class UserRecordUpdateFlags
    {
        public const short Avatar = 0x1;
        public const short Fallback_Avatar = 0x2;
        public const short User_Icon = 4;
        public const short Status = 8;
        public const short Allow_Avatar_Copying = 16;
        public const short Profile_Picture = 32;
        public const short Bio = 64;
    }

    /// <summary>
    ///     Packet schema for ExecutiveAction (EvCode 33).
    /// </summary>
    public class ExecutiveActionPacket
    {
        public const byte Type = 0;
        public const byte Target_User = 1;
        public const byte Message = 2;
        public const byte Main_Property = 3;
        public const byte Heading = 5;
        public const byte Location_Data = 6;
        public const byte World_Id = 8;
        public const byte Instance_Id = 9;
        public const byte Blocked_Users = 10;
        public const byte Muted_Users = 11;
    }

    /// <summary>
    ///     Valid types for ExecutiveAction[0] (EvCode 33, byte 0 - Type)
    /// </summary>
    public class ExecutiveActionTypes
    {
        public const byte Enforce_Moderation = 1;
        public const byte Alert = 2;
        public const byte Warn = 3;
        public const byte Kick = 4;
        public const byte Vote_Kick = 5;
        public const byte Public_Ban = 6;
        public const byte Ban = 7;
        public const byte Mic_Off = 8;
        public const byte Mic_Volume_Adjust = 9;
        public const byte Friend_Change = 10;
        public const byte Warp_To_Instance = 11;
        public const byte Teleport_User = 12;
        public const byte Query = 13;
        public const byte Request_PlayerMods = 20;
        public const byte Reply_PlayerMods = 21;
        public const byte Block_User = 22;
        public const byte Mute_User = 23;
    }

    public class PhotonValidateJoinJWTResponse
    {
        public string Time;
        public bool Valid;
        public string Ip;
        public PhotonPropUser User;
        public PhotonPropAvatarDict AvatarDict;
        public PhotonPropAvatarDict FavatarDict;
        
        // The following properties are only present if the call is made with `onCreate=true` in the query.
        public int WorldCapacity;
        public string WorldAuthor;
        public string InstanceCreator;
    }

    public class PhotonRuntimeRemoteConfig
    {
        public Dictionary<int, int> RateLimitList;
        public bool RatelimiterActive;
        public int MaxAccsPerIp;
    }

    public class PhotonPropUser
    {
        public string Id;
        public string DisplayName;
        public string DeveloperType;
        public string CurrentAvatarImageUrl;
        public string CurrentAvatarThumbnailImageUrl;
        public string UserIcon;
        public string Last_platform;
        public string Status;
        public string StatusDescription;
        public string Bio;
        public IList<string> Tags;
        public bool AllowAvatarCopying;
    }

    public class PhotonPropAvatarDict
    {
        public string id;
        public string assetUrl;
        public string authorId;
        public string authorName;
        public string updated_at;
        public string description;
        public bool featured;
        public string imageUrl;
        public string thumbnailImageUrl;
        public string name;
        public string releaseStatus;
        public int version;
        public IList<string> tags;
        public IList<UnityPackage> unityPackages;
    }

    public class UnityPackage
    {
        public string id;
        public string assetUrl;
        public Dictionary<string, object> assetUrlObject;
        public string created_at;
        public string platform;
        public string pluginUrl;
        public Dictionary<string, object> pluginUrlObject;
        public string unityVersion;
        public int unitySortNumber;
    }
}
