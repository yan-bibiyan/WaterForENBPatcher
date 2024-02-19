using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Mutagen.Bethesda.Plugins;
using WaterForENBPatcher.Utilities;
using System.Reactive.Linq;

namespace WaterForENBPatcher
{
    public class Program
    {
        const string WENBPatchName = "Water_For_ENB_Auto_Patcher.esp";
        private static Lazy<Settings.Settings> _settings = null!;
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch, new PatcherPreferences()
                {
                    ExclusionMods = new List<ModKey>()
                    {
                         new ModKey(WENBPatchName, ModType.Plugin),
                         new ModKey("Synthesis.esp", ModType.Plugin),
                         new ModKey("Requiem for the Indifferent.esp", ModType.Plugin),
                         new ModKey("Occlusion.esp", ModType.Plugin),
                         new ModKey("DynDOLOD.esm", ModType.Master),
                    }
                })
                .SetAutogeneratedSettings("settings", "settings.json", out _settings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "YourPatcher.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            List<ModKey> mods = new List<ModKey>();
            foreach (var mod in state.LoadOrder) {
                mods.Add(mod.Key);
                System.Console.WriteLine(mod.Key);
            }
            System.Console.WriteLine("Running version: 0.1");
            ISkyrimModGetter? WENB = state.LoadOrder.getModByFileName("Water for ENB.esm");
            ISkyrimModGetter? WENB1 = state.LoadOrder.getModByFileName(_settings.Value.WaterForEnbModName);

            if (WENB1 is not null) {
                (List<String> modNames1, List<ISkyrimModGetter> childMods1) = WENB is not null? 
                    state.LoadOrder.getModsFromMasterIncludingMaster("Water for ENB.esm", WENB) :
                    (new List<string>(), new List<ISkyrimModGetter>());

                (List<String> modNames2, List<ISkyrimModGetter> childMods2) = state.LoadOrder.getModsFromMasterIncludingMaster(_settings.Value.WaterForEnbModName, WENB1);
                //will have duplicates, but Union() doesn't work properly on lists of ISkyrimModGetter
                List<String> modNames = modNames1.Concat(modNames2).ToList();
                List<ISkyrimModGetter> childMods = childMods1.Concat(childMods2).ToList();
                patchWater(state, modNames, childMods);
            }
            else {
                System.Console.WriteLine("Water for ENB not found.");
            }
        }
        private static void patchWater(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, List<String> modNames, List<ISkyrimModGetter> mods) {
            List<String> modsToSkip = _settings.Value.ModsToSkip.Split(";").ToList();
            int modIndex = -1;
            int modCount = modNames.Count;
            foreach (var mod in mods) {
                modIndex++;
                System.Console.WriteLine(modIndex+1+"/"+modCount);
                if (modsToSkip.Contains(modNames[modIndex])) {
                    
                    System.Console.WriteLine("\nSkipping mod: " + modNames[modIndex]);
                    continue;
                }
                System.Console.WriteLine("\nPatching mod: " + modNames[modIndex]);
                //patch individual cells
                foreach (ICellBlockGetter cellBlockGetter in mod.Cells.Records) {
                    foreach (ICellSubBlockGetter cellSubBlock in cellBlockGetter.SubBlocks) {
                        foreach (ICellGetter cell in cellSubBlock.Cells) {
                            if (!cell.ToLink().TryResolveContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter>(state.LinkCache, out var winningCellContext)) continue;
                            ICell patchCell = winningCellContext.GetOrAddAsOverride(state.PatchMod);
                            if (cell.WaterEnvironmentMap is not null) {
                                patchCell.WaterEnvironmentMap = cell.WaterEnvironmentMap;
                            }
                        }
                    }
                }
                //patch worldspaces and all referenced cells
                foreach (var worldspace in mod.Worldspaces) {
                    if (!worldspace.ToLink().TryResolveContext<ISkyrimMod, ISkyrimModGetter, IWorldspace, IWorldspaceGetter>(state.LinkCache, out var winningWorldspaceContext)) continue;
                    //if (winningCellContext.Record.Equals(cell)) continue;
                    IWorldspace patchWorldspace = winningWorldspaceContext.GetOrAddAsOverride(state.PatchMod);
                    patchWorldspace.LodWater.SetTo(worldspace.LodWater);
                    foreach (var block in worldspace.SubCells) {
                        foreach (var subBlock in block.Items) {
                            foreach (var cell in subBlock.Items) {
                                System.Console.WriteLine("patching cell: "+cell.FormKey+" : "+cell.Name);
                                if (!cell.ToLink().TryResolveContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter>(state.LinkCache, out var winningCellContext)) continue;
                                //if (winningCellContext.Record.Equals(cell)) continue;
                                ICell patchCell = winningCellContext.GetOrAddAsOverride(state.PatchMod);

                                if (cell.Flags.HasFlag(Cell.Flag.HasWater)) {
                                    patchCell.Flags.SetFlag(Cell.Flag.HasWater, true);
                                }
                                if (!cell.Water.Equals(FormLinkNullableGetter<IWaterGetter>.Null)) {
                                    //patchCell.Water = cell.Water.AsSetter().AsNullable();
                                    patchCell.Water.SetTo(cell.Water);
                                    System.Console.WriteLine("patching water");
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
