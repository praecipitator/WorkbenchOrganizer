using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;

namespace WorkbenchOrganizer
{
    using KeywordList = List<IFormLinkGetter<IKeywordGetter>>;

    using LinkCache = Mutagen.Bethesda.Plugins.Cache.ILinkCache<IFallout4Mod, IFallout4ModGetter>;


    internal static class Mixins
    {
        //[^a-zA-Z0-9_-]+
        private static readonly Regex CLEANUP_EDID = new(@"[^a-zA-Z0-9_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // This is 99-12, because 12 is the length of DUPLICATE000. This way the CK's edid fixing 'feature' shouldn't break it.
        private static readonly uint MAX_EDID_LENGTH = 87;

        public static bool ContainsAny<T>(this IEnumerable<T> thisList, IEnumerable<T> otherList)
        {
            return otherList.Any(entry => thisList.Contains(entry));
        }

        public static bool HasAnyKeyword(this IKeywordedGetter<IKeywordGetter> thisItem, IEnumerable<IFormLinkGetter<IKeywordGetter>> otherList)
        {
            if (thisItem.Keywords == null)
            {
                return false;
            }

            return otherList.ContainsAny(thisItem.Keywords);
        }

        public static void TryToAddByEdid(this KeywordList list, LinkCache linkCache, params string[] edids)
        {
            foreach(var edid in edids)
            {
                linkCache.TryResolve<IKeywordGetter>(edid, out var foundKw);
                if(null == foundKw)
                {
                    return;
                }
                list.Add(foundKw.ToLinkGetter());
            }
        }

        public static string GetStringHash(this IFormLinkGetter<IKeywordGetter> kw)
        {
            return kw.GetHashCode().ToString();
        }

        public static string GetName(this IConstructibleObjectTargetGetter craftResult)
        {
            if(craftResult is INamedGetter named && named.Name != null && named.Name != "")
            {
                return named.Name;
            }

            // otherwise, hm
            return "";
        }

        /// <summary>
        /// Cleans a string for EDID generation
        /// </summary>
        /// <param name="inputString"></param>
        /// <returns></returns>
        public static string ToEdid(this string inputString)
        {
            var cleanedStr = CLEANUP_EDID.Replace(inputString, "");

            if(cleanedStr.Length > MAX_EDID_LENGTH)
            {
                var hash = (uint)cleanedStr.GetHashCode();

                var hashStr = $"{hash:X}";

                var targetLength = (int)(MAX_EDID_LENGTH - hashStr.Length - 1);

                // this seems to mean "from 0 to a length of targetLength"
                return inputString[..targetLength] + "_" + hashStr;
            }

            return inputString;
        }
        

        public static void SetScriptProperty(this ScriptEntry script, string propName, string propValue)
        {
            var prop = script.Properties.Find(prop => prop.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));
            if (prop is ScriptStringProperty stringProp)
            {
                stringProp.Data = propValue;
                return;
            }

            // definitely need a new one
            stringProp = new ScriptStringProperty
            {
                Name = propName,
                Data = propValue
            };

            if (null != prop)
            {
                // delete old
                script.Properties.Remove(prop);
            }            

            script.Properties.Add(stringProp);
        }

        public static void SetScriptProperty(this ScriptEntry script, string propName, int propValue)
        {
            var prop = script.Properties.Find(prop => prop.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));
            if (prop is ScriptIntProperty stringProp)
            {
                stringProp.Data = propValue;
                return;
            }

            // definitely need a new one
            stringProp = new ScriptIntProperty
            {
                Name = propName,
                Data = propValue
            };

            if (null != prop)
            {
                // delete old
                script.Properties.Remove(prop);
            }

            script.Properties.Add(stringProp);
        }

        //ScriptStructListProperty
        public static void SetScriptProperty(this ScriptEntry script, string propName, ScriptStructListProperty propValue)
        {
            var prop = script.Properties.Find(prop => prop.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));

            if (null != prop)
            {
                // delete old
                script.Properties.Remove(prop);
            }

            propValue.Name = propName;
            script.Properties.Add(propValue);
        }
    }
}
