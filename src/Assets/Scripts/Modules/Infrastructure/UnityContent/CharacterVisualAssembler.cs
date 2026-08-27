using TianZhang.Content;

namespace TianZhang.Infrastructure.UnityContent
{
    /// <summary>Resolves battle presentation identity without loading assets or attaching scene objects.</summary>
    public static class CharacterVisualAssembler
    {
        public static bool TryResolveAppearance(
            ContentCatalogData catalog,
            string appearanceProfileId,
            out AppearanceProfileData profile)
        {
            profile = null;
            return catalog != null && catalog.TryGetAppearanceProfile(appearanceProfileId, out profile);
        }

        public static bool TryAssemble(
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
