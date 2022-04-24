using System.Collections.Generic;

namespace NaokaGo
{
    public class Util
    {
        
        /// <summary>
        ///     Wrapper for use with <c>IPluginHost.BroadcastEvent</c>; Wraps the data in a format that PUN expects.
        /// </summary>
        /// <param name="actorNr">The id of the sending actor.</param>
        /// <param name="data">The custom data to wrap.</param>
        /// <returns></returns>
        public static Dictionary<byte, object> EventDataWrapper(int actorNr, object data)
        {
            return new Dictionary<byte, object>
            {
                { 245, data },
                { 254, actorNr }
            };
        }
        
        public static Dictionary<string, object> ParseJwtPropertiesUser(PhotonPropUser user)
        {
            return new Dictionary<string, object>()
            {
                {"id", user.Id},
                {"displayName", user.DisplayName},
                {"developerType", user.DeveloperType},
                {"currentAvatarImageUrl", user.CurrentAvatarImageUrl},
                {"currentAvatarThumbnailImageUrl", user.CurrentAvatarThumbnailImageUrl},
                {"userIcon", user.UserIcon},
                {"last_platform", user.Last_platform},
                {"status", user.Status},
                {"statusDescription", user.StatusDescription},
                {"bio", user.Bio},
                {"tags", user.Tags},
                {"allowAvatarCopying", user.AllowAvatarCopying}
            };
        }

        public static Dictionary<string, object> ParseJwtPropertiesAvatar(PhotonPropAvatarDict avatar)
        {
            return new Dictionary<string, object>()
            {
                {"id", avatar.id},
                {"assetUrl", avatar.assetUrl},
                {"authorId", avatar.authorId},
                {"authorName", avatar.authorName},
                {"updated_at", avatar.updated_at},
                {"description", avatar.description},
                {"featured", avatar.featured},
                {"imageUrl", avatar.imageUrl},
                {"thumbnailImageUrl", avatar.thumbnailImageUrl},
                {"name", avatar.name},
                {"releaseStatus", avatar.releaseStatus},
                {"version", avatar.version},
                {"tags", avatar.tags},
                {"unityPackages", GetUnityPackages(avatar.unityPackages)}
            };
        }
        
        public static List<Dictionary<string, object>> GetUnityPackages(IList<UnityPackage> unityPackageArray)
        {
            var unityPackages = new List<Dictionary<string, object>>(){};
            foreach (var unp in unityPackageArray)
            {
                unityPackages.Add(new Dictionary<string, object>
                {
                    {"id", unp.id},
                    {"assetUrl", unp.assetUrl},
                    {"assetUrlObject", new Dictionary<string, object>(){}},
                    {"created_at", unp.created_at},
                    {"platform", unp.platform},
                    {"pluginUrl", unp.pluginUrl},
                    {"pluginUrlObject", new Dictionary<string, object>(){}},
                    {"unityVersion", unp.unityVersion},
                    {"unitySortNumber", unp.unitySortNumber}
                });
            }

            return unityPackages;
        }
    }
}