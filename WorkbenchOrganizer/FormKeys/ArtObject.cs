// Autogenerated by https://github.com/Mutagen-Modding/Mutagen.Bethesda.FormKeys

using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda;

namespace MakeModsScrappable.FormKeys
{
    public static partial class OWM_Master
    {
        public static class ArtObject
        {
            private static FormLink<IArtObjectGetter> Construct(uint id) => new FormLink<IArtObjectGetter>(ModKey.MakeFormKey(id));
            public static FormLink<IArtObjectGetter> praWBG_CraftMenuIcon => Construct(0x80b);
        }
    }
}