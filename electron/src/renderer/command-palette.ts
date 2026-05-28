/**
 * Command Palette — searchable command dialog for the VoxelForge CLI catalog.
 *
 * Lists every command from the C# CommandRegistry with its name, aliases, and
 * help text. When selected, prompts for arguments and executes via the Myra
 * CLI bridge (bridge:myra-command-execute → voxelforge.myra.execute).
 *
 * Source-parsed manifest is generated at test time from CommandRegistry.cs.
 * The runtime manifest is kept in sync via the menu-bridge-parity contract test.
 */

export interface CommandCatalogEntry {
  /** IConsoleCommand.Name — the canonical name used to invoke. */
  name: string;
  /** IConsoleCommand.Aliases — alternate names. */
  aliases: string[];
  /** IConsoleCommand.HelpText — usage / description. */
  helpText: string;
  /** Category grouping key. */
  category: string;
  /** True if this command is backed by the C# MyraCommandExecuteHandler. */
  backed: boolean;
  /** Expected argument count (0 = variadic / prompted). */
  argCount?: number;
}

/**
 * Full catalog of all Myra CLI commands from CommandRegistry.cs.
 *
 * Synchronised with the source-manifest test at
 * electron/tests/menu-bridge-parity.test.ts (describe "Command palette coverage").
 *
 * Every command's `backed` field matches whether it is registered in the
 * MyraCommandExecuteHandler's CommandRouter on the C# side.
 */
export const COMMAND_CATALOG: CommandCatalogEntry[] = [
  // ── Query ──
  { name: "describe", aliases: ["desc", "info"], helpText: "Describe model (palette, bounds, regions, materials).", category: "Query", backed: false },
  { name: "get", aliases: ["g"], helpText: "Get voxel at position. Usage: get <x> <y> <z>", category: "Query", backed: false },
  { name: "getcube", aliases: ["gc"], helpText: "Get voxels in a cube region. Usage: getcube <x1> <y1> <z1> <x2> <y2> <z2>", category: "Query", backed: false },
  { name: "getsphere", aliases: ["gs"], helpText: "Get voxels in a sphere. Usage: getsphere <cx> <cy> <cz> <radius>", category: "Query", backed: false },
  { name: "count", aliases: ["c"], helpText: "Count voxels (optionally by palette index). Usage: count [palette_index]", category: "Query", backed: false },

  // ── Editing ──
  { name: "set", aliases: ["s"], helpText: "Set voxel at position. Usage: set <x> <y> <z> <palette_index>", category: "Editing", backed: false },
  { name: "remove", aliases: ["rm", "del"], helpText: "Remove voxel at position. Usage: remove <x> <y> <z>", category: "Editing", backed: false },
  { name: "fill", aliases: ["f"], helpText: "Fill a box region. Usage: fill <x1> <y1> <z1> <x2> <y2> <z2> <palette_index>", category: "Editing", backed: false },
  { name: "clear", aliases: ["clr"], helpText: "Remove all voxels. Usage: clear", category: "Editing", backed: false },
  { name: "undo", aliases: ["u"], helpText: "Undo last action.", category: "Editing", backed: false },
  { name: "redo", aliases: ["r"], helpText: "Redo last undone action.", category: "Editing", backed: false },

  // ── Regions ──
  { name: "list-regions", aliases: ["lr", "regions"], helpText: "List all labelled regions. Usage: list-regions", category: "Regions", backed: false },
  { name: "label", aliases: ["lb"], helpText: "Label a voxel with a region name. Usage: label <name> <x> <y> <z>", category: "Regions", backed: false },

  // ── Palette ──
  { name: "palette", aliases: ["p", "pal"], helpText: "List, add, or modify palette entries. Usage: palette | palette add <index> <name> <R> <G> <B> [A]", category: "Palette", backed: false },
  { name: "palette-map", aliases: ["pmap"], helpText: "Color find/replace. Usage: palette-map <from> <to> [tolerance]", category: "Palette", backed: true },
  { name: "palette-reduce", aliases: ["preduce"], helpText: "Reduce palette to N colors. Usage: palette-reduce <count> [--preserve #RRGGBB,...]", category: "Palette", backed: true },

  // ── Baking ──
  { name: "ao-bake", aliases: ["ao"], helpText: "Bake ambient occlusion. Usage: ao-bake [intensity] [steps]", category: "Baking", backed: true },
  { name: "edge-darken", aliases: ["edged"], helpText: "Darken boundary voxels. Usage: edge-darken [strength] [steps] [tint]", category: "Baking", backed: true },
  { name: "light-bake", aliases: ["lbake"], helpText: "Bake directional light. Usage: light-bake <dx,dy,dz> [intensity] [steps] [tint]", category: "Baking", backed: true },

  // ── Screenshot ──
  { name: "screenshot", aliases: ["ss"], helpText: "Capture viewport. Usage: screenshot [filepath] | screenshot all [prefix] | screenshot angle <yaw> <pitch> [filepath]", category: "Screenshot", backed: false },

  // ── Config ──
  { name: "config", aliases: ["cfg"], helpText: "View or set config. Usage: config | config <key> <value> | config save", category: "Config", backed: false },
  { name: "grid", aliases: ["grd"], helpText: "Set grid size. Usage: grid <size>", category: "Config", backed: false },
  { name: "measure", aliases: ["meas"], helpText: "View or set measure scale. Usage: measure | measure <voxels_per_meter>", category: "Config", backed: false },

  // ── Project ──
  { name: "save", aliases: ["sv"], helpText: "Save project. Usage: save [path]", category: "Project", backed: false },
  { name: "load", aliases: ["ld"], helpText: "Load project. Usage: load <path>", category: "Project", backed: false },
  { name: "list-files", aliases: ["ls", "dir"], helpText: "List project files. Usage: list-files [pattern]", category: "Project", backed: true },

  // ── Reference Model ──
  { name: "refload", aliases: ["refld"], helpText: "Load a reference model file.", category: "Reference Model", backed: true },
  { name: "reflist", aliases: ["refls"], helpText: "List loaded reference models.", category: "Reference Model", backed: true },
  { name: "refremove", aliases: ["refrm"], helpText: "Remove a reference model by index.", category: "Reference Model", backed: true },
  { name: "refclear", aliases: ["refclr"], helpText: "Remove all reference models.", category: "Reference Model", backed: true },
  { name: "reftransform", aliases: ["reftf"], helpText: "Set position/rotation/scale of a reference model.", category: "Reference Model", backed: true },
  { name: "refmode", aliases: ["refmd"], helpText: "Set render mode (wireframe/solid/transparent).", category: "Reference Model", backed: true },
  { name: "refshow", aliases: ["refsh"], helpText: "Show a reference model.", category: "Reference Model", backed: true },
  { name: "refhide", aliases: ["refhd"], helpText: "Hide a reference model.", category: "Reference Model", backed: true },
  { name: "refscale", aliases: ["refsc"], helpText: "Scale a reference model.", category: "Reference Model", backed: true },
  { name: "refrotate", aliases: ["refrt"], helpText: "Rotate a reference model.", category: "Reference Model", backed: true },
  { name: "reforient", aliases: ["refor"], helpText: "Auto-orient a reference model.", category: "Reference Model", backed: true },
  { name: "refinfo", aliases: ["refinf"], helpText: "Inspect a reference model.", category: "Reference Model", backed: true },
  { name: "refanim", aliases: ["refan"], helpText: "List/play/stop/pause animation clips.", category: "Reference Model", backed: true },
  { name: "reftex", aliases: ["reftx"], helpText: "Assign a texture to a reference model.", category: "Reference Model", backed: true },
  { name: "reftex-emissive", aliases: ["refem"], helpText: "Assign emissive texture to a reference model.", category: "Reference Model", backed: true },
  { name: "refsave", aliases: ["refsv"], helpText: "Save reference metadata to .refmeta file.", category: "Reference Model", backed: true },
  { name: "refloadmeta", aliases: ["reflm"], helpText: "Load reference metadata from .refmeta file.", category: "Reference Model", backed: true },

  // ── Image Reference ──
  { name: "imgload", aliases: ["imgld"], helpText: "Load an image reference.", category: "Image Reference", backed: true },
  { name: "imglist", aliases: ["imgls"], helpText: "List image references.", category: "Image Reference", backed: true },
  { name: "imgremove", aliases: ["imgrm"], helpText: "Remove an image reference by index.", category: "Image Reference", backed: true },

  // ── Voxelize ──
  { name: "voxelize", aliases: ["vox"], helpText: "Voxelize a reference model. Usage: voxelize <index> <resolution> <mode>", category: "Voxelize", backed: true },
  { name: "voxcompare", aliases: ["voxcmp"], helpText: "Compare voxelizations at multiple resolutions. Usage: voxcompare <index> <resolutions> <mode>", category: "Voxelize", backed: true },

  // ── System / Meta ──
  { name: "help", aliases: ["h", "?"], helpText: "Show command help. Usage: help [command_name]", category: "System", backed: false },
  { name: "exec", aliases: ["x"], helpText: "Execute a script file. Usage: exec <filepath>", category: "System", backed: false },
];

/** Look up a catalog entry by name or alias. */
export function lookupCommand(input: string): CommandCatalogEntry | undefined {
  const lower = input.toLowerCase();
  return COMMAND_CATALOG.find(
    (e) => e.name === lower || e.aliases.includes(lower),
  );
}

/** Distinct sorted categories. */
export const COMMAND_CATEGORIES = [
  ...new Set(COMMAND_CATALOG.map((e) => e.category)),
].sort();
