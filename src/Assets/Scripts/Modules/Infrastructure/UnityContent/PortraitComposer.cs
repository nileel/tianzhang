using TianZhang.Content;

namespace TianZhang.Infrastructure.UnityContent
{
    /// <summary>Resolves portrait identity through the same catalog boundary as battle presentation.</summary>
    public static class PortraitComposer
    {
        public static bool TryResolveAppearance(
            ContentCatalogData catalog,
            string appearanceProfileId,
            out AppearanceProfileData profile)
        {
            profile = null;
            return catalog != null && catalog.TryGetAppearanceProfile(appearanceProfileId, out profile);
        }

        public static bool TryCompose(
            ContentCatalogData catalog,
            string appearanceProfileId,
            out AppearanceProfileData profile)
        {
            if (!TryResolveAppearance(catalog, appearanceProfileId, out profile) || profile.IsNone)
            {
                profile = null;
                return false;
            }

            return true;
        }
    }
}
