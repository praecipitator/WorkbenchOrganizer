using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.FormKeys.Fallout4;
using Mutagen.Bethesda.Plugins;
using MakeModsScrappable.FormKeys;
using Noggog;
using Mutagen.Bethesda.Plugins.Records;
//using MakeModsScrappable.;

namespace WorkbenchOrganizer
{
    using KeywordLink = IFormLinkGetter<IKeywordGetter>;

    using KeywordList = List<IFormLinkGetter<IKeywordGetter>>;

    using KeywordMapping = Dictionary<IFormLinkGetter<IKeywordGetter>, IFormLinkGetter<IKeywordGetter>>;

    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<IFallout4Mod, IFallout4ModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.Fallout4, "OWM-Patch.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<IFallout4Mod, IFallout4ModGetter> state)
        {
            var processor = new Processor(state);
            processor.Process();            
        }
    }
    

    internal class Processor
    {
        private static readonly KeywordLink KEYWORD_NULL = FormLink<IKeywordGetter>.Null;
        // timestamp (for version nr) will be subtracted by this
        // this is Fri Apr 15 2022 05:20:00 GMT+0000
        private static readonly int MIN_TIMESTAMP = 1650000000;
        // FNAMs (COBJ Categories) which can be replaced by my new KWs
        private static readonly KeywordList fnamsToReplace = new()
        {
            Fallout4.Keyword.WorkshopRecipeFilterCrafting,
            Fallout4.Keyword.WorkshopRecipeFilterQuest,
        };

        // contains all known WorkshopKeywords for item recipes which are definitely irrelevant
        private static readonly KeywordList ItemWorkbenchKeywordBlacklist = new()
        {
            Fallout4.Keyword.WorkshopWorkbenchTypeCrafting,
            Fallout4.Keyword.WorkshopWorkbenchTypeDecorations,
            Fallout4.Keyword.WorkshopWorkbenchTypeExterior,
            Fallout4.Keyword.WorkshopWorkbenchTypeFurniture,
            Fallout4.Keyword.WorkshopWorkbenchTypeInteriorOnly,
            Fallout4.Keyword.WorkshopWorkbenchTypePower,
            Fallout4.Keyword.WorkshopWorkbenchTypeSettlement,
            Fallout4.Keyword.WorkshopWorkbenchTypeWire,
            Fallout4.Keyword.VRWorkshopShared_Keyword_WorkshopWorkbenchTypeVR,
        };

        // cache of all known valid BNAMs (COBJ Workbench keywords), which were found on item recipes during processing
        private readonly KeywordList KnownValidWorkbenchKeywords = new();
        // todo add these which I know

        // cache of all of MINE category keywords. newly-generated will be added
        private readonly KeywordList MyCategoryKeywords = new()
        {
            OWM_Master.Keyword.praWBG_Armor,
            OWM_Master.Keyword.praWBG_Chemistry,
            OWM_Master.Keyword.praWBG_Cooking,
            OWM_Master.Keyword.praWBG_Misc,
            OWM_Master.Keyword.praWBG_Nukacola,
            OWM_Master.Keyword.praWBG_PowerArmor,
            OWM_Master.Keyword.praWBG_Robot,
            OWM_Master.Keyword.praWBG_Weapons,
        };

        // these are the keyword which must be put into the quest
        private readonly List<Keyword> NewKeywordsToAdd = new();

        // bnamKw -> categoryKw mapping
        private readonly KeywordMapping bnamToCategoryMapping = new()
        {
            { NukaWorld.Keyword.DLC04_WorkbenchSoda, OWM_Master.Keyword.praWBG_Nukacola },
            { Fallout4.Keyword.WorkbenchChemlab, OWM_Master.Keyword.praWBG_Chemistry },
            { Fallout4.Keyword.WorkbenchCooking, OWM_Master.Keyword.praWBG_Cooking },
        };

        // NEVER consider these as WorkbenchKeywords 
        private readonly KeywordList WorkbenchKeywordBlacklist = new()
        {
            Fallout4.Keyword.AnimsFurnitureBehaviorLinking,
            Fallout4.Keyword.AnimsFurnitureNoMirrorBehaviorLinking,
            Fallout4.Keyword.AnimsMinigunTurrentFurniture,
            Fallout4.Keyword.Workbench_General,
            Fallout4.Keyword.FurnitureForce3rdPerson,
            Fallout4.Keyword.FurnitureScaleActorToOne,
            Fallout4.Keyword.WorkshopWorkObject,
            Fallout4.Keyword.FurnitureClassWork,
        };

        // pairs of COBJ and corresponding result, which the patcher failed to categorize during the first iteration
        private readonly List<Tuple<IConstructibleObjectGetter, IConstructibleObjectTargetGetter>> backlog = new();


        private readonly IPatcherState<IFallout4Mod, IFallout4ModGetter> state;
        

        public Processor(IPatcherState<IFallout4Mod, IFallout4ModGetter> state)
        {
            this.state = state;


            WorkbenchKeywordBlacklist.TryToAddByEdid(state.LinkCache, 
                "SS2_ForceOwnership", "kgSIM_PreventAutoAssign"
            );
        }

        public void LoadAnimFurnKeywords()
        {
            // load them from races only. Hopefully it's enough
            foreach(var race in state.LoadOrder.PriorityOrder.Race().WinningOverrides())
            {
                foreach(var sg in race.Subgraphs)
                {
                    if(sg.TargetKeywords.Count > 0)
                    {
                        WorkbenchKeywordBlacklist.AddRange(sg.TargetKeywords);
                    }
                }
            }
        }

        public void Process()
        {
            var allCobjs = state.LoadOrder.PriorityOrder.ConstructibleObject().WinningOverrides();
            foreach (var cobj in allCobjs)
            {
                try
                {
                    ProcessCobj(cobj);
                }
                catch (Exception e)
                {
                    throw RecordException.Enrich(e, cobj);
                }
            }

            // second pass: process backlog
            foreach(var entry in backlog)
            {
                ProcessWorkbench(entry.Item1, entry.Item2, true);
            }

            // no point of adding the quest if we don't have any menus to add
            if(NewKeywordsToAdd.Count > 0)
            {
                CreateQuest();
            }
        }

        private void CreateQuest()
        {
            //var resolvedQuest = state.LinkCache.TryResolve<IQuestGetter>(OWM_Master.Quest.pra_SmmOrganizedMenuInstaller);
            var resolvedQuest = OWM_Master.Quest.pra_SmmOrganizedMenuInstaller.TryResolve(state.LinkCache);
            if(null == resolvedQuest)
            {
                throw new InvalidDataException("pra_SmmOrganizedMenuInstaller doesn't resolve!");
            }

            var patchFileName = state.PatchMod.ModKey.FileName.String;

            // 'pra_OWMPlugin_'+settings.patchFileName.replace(/\./g,'-');
            var newEdid = ("pra_OWMPlugin_" + patchFileName.Replace('.', '-')).ToEdid();
            
            var newQuest = state.PatchMod.Quests.DuplicateInAsNewRecord(resolvedQuest, newEdid);
            newQuest.EditorID = newEdid;

            newQuest.Data ??= new();

            newQuest.Data.Flags.SetFlag(Quest.Flag.RunOnce, false);

            // script
            var questScript = newQuest.VirtualMachineAdapter?.Scripts.Find(script => script.Name == "pra:OrganizedWorkbenchMenuMain");
            if(questScript == null)
            {
                // this is a bad bug
                throw new InvalidDataException("pra_SmmOrganizedMenuInstaller is missing it's script!");
            }

            //questScript.Properties.find

            //ScriptStringProperty
            questScript.SetScriptProperty("ModName", "Automatic Synthesis OWM patch");
            questScript.SetScriptProperty("Author", "WorkbenchOrganizer by Pra");
            questScript.SetScriptProperty("PluginName", patchFileName);


            // get current date in seconds for the version number.
            // Reduce it somewhat, to keep it well within int.
            // now in theory, int should have enough space until Jan 19 2038,
            // but since it's a date I might actually live to see, better be safe than sorry.
            // This should buy us time till 2090.
            var Timestamp = (int)(new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() - MIN_TIMESTAMP);
            questScript.SetScriptProperty("currentVersion", Timestamp);

            // finally the array of struct
            var MenusProp = new ScriptStructListProperty();

            foreach(var kw in NewKeywordsToAdd)
            {
                MenusProp.Structs.Add(CreateMenuStruct(kw.ToLink()));
            }

            questScript.SetScriptProperty("Menus", MenusProp);

        }

        private static ScriptEntryStructs CreateMenuStruct(IFormLink<IFallout4MajorRecordGetter> ModMenu)
        {
            var result = new ScriptEntryStructs();

            //var modMenuProp = ;
            

            result.Members.Add(new ScriptObjectProperty
            {
                Name = "ModMenu",
                Object = ModMenu
            });
            result.Members.Add(new ScriptObjectProperty
            {
                Name = "TargetMenu",
                Object = OWM_Master.FormList.praWorkshopMenuCraftingGrouped
            });

            return result;
        }

        private void ProcessCobj(IConstructibleObjectGetter cobj)
        {
            // get craft result
            var craftRes = cobj.CreatedObject.TryResolve(state.LinkCache);
            if(craftRes == null)
            {
                return;
            }

            if(IsWorkbench(cobj, craftRes))
            {
                ProcessWorkbench(cobj, craftRes);
                return;
            }

            // otherwise, this might be an item which is crafted at a relevant bench
            ProcessItem(cobj);

        }

        private void ProcessItem(IConstructibleObjectGetter cobj)
        {
            var wbKeyword = cobj.WorkbenchKeyword;
            if(wbKeyword.IsNull || ItemWorkbenchKeywordBlacklist.Contains(wbKeyword) || KnownValidWorkbenchKeywords.Contains(wbKeyword))
            {
                // irrelevant
                return;
            }

            KnownValidWorkbenchKeywords.Add(wbKeyword);
        }
        private void ProcessWorkbench(IConstructibleObjectGetter cobj, IConstructibleObjectTargetGetter result, bool fromBacklog = false)
        {
            if(cobj.WorkbenchKeyword.IsNull)
            {
                return;
            }

            if(null != cobj.Categories && cobj.Categories.Count > 0)
            {
                // for workbenches, I want to always process them, unless they happen to have one of my KWs already
                if(MyCategoryKeywords.Any(myKw => cobj.Categories.Contains(myKw)))
                {
                    return;
                }
            }


            // actually process
            
            var benchKw = FindWorkbenchKeyword(result);
            if(benchKw.IsNull)
            {
                if(!fromBacklog)
                {
                    backlog.Add(Tuple.Create(cobj, result));
                    return;
                }

                // otherwise assume misc
                benchKw = OWM_Master.Keyword.praWBG_Misc;
            }

            // if workbenchKeyword is one of my categories, use it right away
            if(MyCategoryKeywords.Contains(benchKw))
            {
                MoveBenchToCategory(cobj, benchKw);
                return;
            }

            // if we have mapping for this, use the mapping
            if(bnamToCategoryMapping.TryGetValue(benchKw, out var target))            
            {
                MoveBenchToCategory(cobj, target);
                return;
            }

            // now, the complicated part...
            var newCategory = CreateCategoryKeyword(benchKw, result);

            MoveBenchToCategory(cobj, newCategory);

            /*
            let newCategory = createCategoryKeyword(workbenchKeyword, craftResult);
        
            moveBenchToCategory(cobj, craftResult, newCategory);*/

        }



        private KeywordLink CreateCategoryKeyword(KeywordLink workbenchKeyword, IConstructibleObjectTargetGetter result)
        {
            var newKw = TryToCreateCategoryKeyword(workbenchKeyword, result);
            if(newKw.IsNull) {
                bnamToCategoryMapping[workbenchKeyword] = OWM_Master.Keyword.praWBG_Misc;
                return OWM_Master.Keyword.praWBG_Misc;
            }
            bnamToCategoryMapping[workbenchKeyword] = newKw;
            return newKw;
        }
        private KeywordLink TryToCreateCategoryKeyword(KeywordLink workbenchKeyword, IConstructibleObjectTargetGetter result)
        {
            var resolvedBnam = workbenchKeyword.TryResolve(state.LinkCache);
            String bnamEdid = resolvedBnam?.EditorID ?? workbenchKeyword.GetStringHash();

            var newSubmenuEdid = ("praWBG_" + bnamEdid).ToEdid();

            // try lookup by this edid
            if(state.LinkCache.TryResolve<IKeywordGetter>(newSubmenuEdid, out var foundKw))
            {
                return foundKw.ToLinkGetter();
            }

            // otherwise, properly generate
            var benchName = result.GetName();
            if(benchName == "")
            {
                return KEYWORD_NULL;
            }

            // finally creating
            Console.WriteLine("Creating new submenu for "+benchName);

            

            var newKeyword = state.PatchMod.Keywords.AddNew(bnamEdid);
            // set all the things
            newKeyword.EditorID = bnamEdid;
            newKeyword.Type = Keyword.TypeEnum.RecipeFilter;
            newKeyword.Name = benchName;

            MyCategoryKeywords.Add(newKeyword);
            NewKeywordsToAdd.Add(newKeyword);

            return newKeyword.ToLinkGetter();
        }

        private void MoveBenchToCategory(IConstructibleObjectGetter cobj, KeywordLink targetKeyword)
        {
            var newCobj = state.PatchMod.ConstructibleObjects.GetOrAddAsOverride(cobj);

            // this shouldn't actually happen. cobj's categories should have been checked and found to exist before
            newCobj.Categories ??= new();

            newCobj.Categories = newCobj.Categories.Select(kw => fnamsToReplace.Contains(kw) ? targetKeyword : kw).ToExtendedList();
        }

        private KeywordLink FindWorkbenchKeyword(IConstructibleObjectTargetGetter bench)
        {
            if (bench is IFurnitureGetter furnRes)
            {
                switch(furnRes.BenchType)
                {
                    case Furniture.BenchTypes.Weapons:
                        return OWM_Master.Keyword.praWBG_Weapons;
                    case Furniture.BenchTypes.Armor:
                        return OWM_Master.Keyword.praWBG_Armor;
                    case Furniture.BenchTypes.PowerArmor:
                        return OWM_Master.Keyword.praWBG_PowerArmor;
                    case Furniture.BenchTypes.RobotMod:
                        return OWM_Master.Keyword.praWBG_Robot;
                    case Furniture.BenchTypes.Alchemy:
                        return FindWorkbenchKeywordForCraftingBench(furnRes);
                }
            }

            // if this is not a furniture, just assume it's a misc workbench
            return OWM_Master.Keyword.praWBG_Misc;
        }

        /// <summary>
        /// Finds the BenchKeyword from a known furniture. 
        /// </summary>
        /// <param name="bench"></param>
        /// <returns>Null if found more than one candidate, misc if didn't find anything, or the KW if it found exactly one</returns>
        private KeywordLink FindWorkbenchKeywordForCraftingBench(IFurnitureGetter bench)
        {
            if(bench.Keywords == null)
            {
                return OWM_Master.Keyword.praWBG_Misc;
            }

            // iterate all KWs
            KeywordLink resultCandidate = OWM_Master.Keyword.praWBG_Misc;
            // keep track of how many potential candidates we found
            int numCandidates = 0;

            foreach (var kw in bench.Keywords)
            {
                if(kw.IsNull)
                {
                    continue;
                }
                if(KnownValidWorkbenchKeywords.Contains(kw))
                {
                    return kw;
                }
                // KW must not be on the blacklist
                if(WorkbenchKeywordBlacklist.Contains(kw))
                {
                    continue;
                }

                if(state.LinkCache.TryResolve(kw, out var resolvedKw))
                {
                    //var resolvedKw = kw.TryResolve(state.LinkCache);
                    if(resolvedKw.EditorID == null)
                    {
                        WorkbenchKeywordBlacklist.Add(kw);
                        continue;
                    }
                    if(resolvedKw.EditorID.Contains("AnimFurn", StringComparison.OrdinalIgnoreCase))
                    {
                        WorkbenchKeywordBlacklist.Add(kw);
                        continue;
                    }

                    // at this point, kw is a result candidate
                    // keep iterating, even if we have more than one. a known BNAM keyword might still occur
                    numCandidates += 1;

                    resultCandidate = kw;
                }
            }

            if(numCandidates != 1)
            {
                return KEYWORD_NULL;
            }

            if(!resultCandidate.IsNull)
            {
                KnownValidWorkbenchKeywords.Add(resultCandidate);
            }
            return resultCandidate;
        }


        /// <summary>
        /// Checks whenever a COBJ could be the recipe for a Workbench
        /// </summary>
        /// <param name="cobj"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        private static bool IsWorkbench(IConstructibleObjectGetter cobj, IConstructibleObjectTargetGetter result)
        {
            if(null == cobj.Categories)
            {
                return false;
            }

            // if this is something under the crafting menu, consider it
            if (cobj.Categories.Contains(Fallout4.Keyword.WorkshopRecipeFilterCrafting))
            {
                return true;
            }

            if (result is IFurnitureGetter furnRes)
            {
                // for the special menu, it has to be a FURN and the workbench keyword
                if (
                    cobj.Categories.Contains(Fallout4.Keyword.WorkshopRecipeFilterQuest) &&
                    furnRes.Keywords != null &&
                    furnRes.Keywords.Contains(Fallout4.Keyword.Workbench_General)
                )
                {
                    return true;
                }
            }


            return false;
        }

    }
}
