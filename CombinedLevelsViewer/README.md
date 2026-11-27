# Combined Levels Viewer

The viewer projects are included in `NovaLake_Gdie_512EU.sln` under the `CombinedLevelsViewer` solution folder. They can be built alongside the Torch projects without replacing Torch's `.lvl` or `.tcg` editor associations.

## Install and activate

1. Build `CombinedLevelsViewer.Extension` in the `Release` configuration.
2. Close every running Visual Studio instance.
3. Install the unsigned VSIX from:
   `CombinedLevelsViewer\src\CombinedLevelsViewer.Extension\bin\Release\net8.0-windows\CombinedLevelsViewer.Extension.vsix`
4. Wait for Visual Studio Installer to report that the update completed, then restart Visual Studio and open `NovaLake_Gdie_512EU.sln`.
5. Select **Extensions > Open Combined Levels Viewer**.

The command opens the combined table and paired source windows for:

- `Shared\Common\LevelsSequences.lvl`
- `Shared\Common\BaseLevels.tcg`

Adding the projects to the solution does not load the extension into the current Visual Studio process. Install the rebuilt VSIX and restart every Visual Studio instance after extension changes. If the command is not visible after restart, confirm that **Combined Levels Viewer** is enabled in **Extensions > Manage Extensions**.

## Torch tabular grid prototype

The application is called **Clive** (Combined Levels Viewer); its executable is `Clive.exe`. The project `CombinedLevelsViewer.TorchTabular` shows the same level tables in the tabular grid that ships with the Torch extension, without needing the VSIX. It is a prototype for building a levels view into Torch itself.

Run it from the repository root:

`dotnet run --project CombinedLevelsViewer\src\CombinedLevelsViewer.TorchTabular -c Release`

or pass the repository root as the first argument to `Clive.exe`. Pick a level set on the left; double-click a cell to edit it; **Save** writes both files.

The first argument can also be the path of `LevelsSequences.lvl` or `BaseLevels.tcg`, so the viewer can be registered in Visual Studio under **Open With > Add** for those files. Either file opens the same paired view.

How it is put together:

- `Web\Html\LevelsComponents` is a levels page for Torch's web grid. It is laid out like Torch's own `TorchTabularComponents` and `DFFComponents` folders and imports Torch's `BaseComponents` (bootstrapper, command runner, update and theme commands, cell-edit handler) by relative path, so it can be copied into Torch's `Web\Html` folder unchanged.
- `LevelsRecordsExtractor` turns a level table from `CombinedLevelsViewer.Core` into the row and cell records the Torch grid expects.
- `TorchGridBridge` and the window are a stand-in host. They load the grid assets from the installed Torch (`%LOCALAPPDATA%\Torch\<version>\Web`, or `TORCH_WEB_ROOT`), send `FillGridCommand` / `UpdateGridCommand`, and handle `CellEditEvent`. Inside Torch this role belongs to a designer editor factory.

Level sets and their selectors:

- The left-hand tree lists every level set. A level set drops down into its test conditions, one per selector of the linked specification set (for example `SBF` opens to `SBF_max_lvl`, `SBF_min_lvl`, `SBF_nom_lvl`, ...).
- Selecting the level set shows the expressions as written in the `.lvl` file; this is the editable view.
- Selecting a test condition shows the same table with every parameter (`p_...`, `c_...`) replaced by its value for that selector. The tooltip of a resolved cell shows the expression, each substitution, the result and what an edit would change.
- Values in the per-selector view can be edited, and **Save** writes them back. An edit goes to the definition that holds the value:
  - a literal in the level (for example `500mA`) is changed in `LevelsSequences.lvl`;
  - a parameter is followed through the specification set to the entry that holds a literal, and that entry is changed in `BaseLevels.tcg`. When the value lists one entry per selector, only the entry of the selected selector changes; when it holds a single value, the change applies to every selector, and the status bar says so.
  - Because the edit changes the parameter, every level and row that uses the same parameter in that specification set changes with it.
  - Values calculated by a formula or taken from a uservar cannot be set from the table; the tooltip and the status bar give the reason.
- To edit a cell, double-click it (or select it and press Enter or start typing), change the value and press Enter. You do not have to press Enter or click elsewhere before saving: **Save**, closing the window and selecting another level set or test condition all apply a value that is still being typed. If that value is not valid, nothing is saved (or the window stays open) and the status bar says why.
- A name is looked up in the specification set of the linked test-condition group first, then in the uservars. A specification value that lists one entry per selector yields the entry for the selected selector. Formulas using `+ - * /` and parentheses are calculated, with prefixed units (`mV`, `uA`, `mS`) converted to base units. Literals and values taken straight from a definition keep their spelling (`4.00A`, `500mA`).
- Names must match exactly (case-sensitive, never partial). A cell whose parameter has no exact definition keeps its expression and is shown in red; the status bar reports how many references in the whole file resolve.
- `PowerDomain` is left as written: its value is the name of a power domain from the pin definitions, not a parameter.

Duplicates:

- A level set is highlighted in light yellow when another level set produces the same values: either both have the same selectors with identical values, or every selector of one exists in the other with identical values. A test condition is highlighted when another test condition (in any level set) resolves to an identical table.
- The comparison is on resolved values for every cell, with prefixed units normalised (`800mV` equals `0.8V`). It reflects the values currently in the three files.
- The tooltip lists the duplicates. Right-click a highlighted node and choose **Compare with duplicates...** to open a side-by-side table: selector, resource, property, the value on each side and the chain of definitions each side uses. Values that are equal but reached through different definitions (for example two uservars that both hold `40A`) are highlighted there, because they would diverge if one definition changed.

Clamps pulled in from the uservars:

- A clamp in a level (`IClampHi`, `IClampLo`, `VClampHi`, `VClampLo`) that names a parameter, for example `c_vnnaon_initial_clamp_hi_final`, is resolved through the linked specification set in `BaseLevels.tcg` to the uservar it is assigned (`u_vnnaon_initial_clamp_hi`) and then to that uservar's value in `Shared\Common\UservarDefinitions.usrv`.
- Each clamp column gets a read-only `(uservar)` column next to it showing `uservar = value`. When a level is linked to several specification sets that resolve differently, the distinct results are listed and the tooltip shows one line per group. Clamps written as literal values (for example `500mA`) leave the column empty.
- Names must match exactly at every step: the comparison is case-sensitive and never partial. A clamp without an exact match is shown in red as `NO EXACT MATCH: ...`, and the status bar reports how many clamp parameters in the whole file match.
- The uservar file is read at start-up and on **Reload from disk**. It is never written.

Limits of the prototype:

- It needs the Torch extension installed and was checked against Torch 5.8.0.2. Torch's web components are not a published interface and can change between Torch versions.
- The grid is AG Grid Enterprise, loaded from the Torch installation under Torch's licence. Check with the Torch team before distributing this tool separately from Torch.
- Only cells that already have a value in the `.lvl` file can be edited. An edit that would make the file unparsable is rejected.
- The test-condition context panel is read-only.
- `--selftest <file>` renders the first level, pushes one unsaved edit through the grid, writes what the grid showed to `<file>` and exits.

## Package for other people

`installer\Build-Package.ps1` builds a folder and a zip that anyone can install from, with no .NET or build tools needed:

`powershell -ExecutionPolicy Bypass -File CombinedLevelsViewer\installer\Build-Package.ps1`

- Output: `dist\Clive-Setup\` and `dist\Clive-Setup.zip` (git-ignored, about 160 MB / 67 MB).
- Contents: the viewer (`app`, self-contained for 64-bit Windows), `Install.cmd`, `Uninstall.cmd` and `README.txt`.
- `Install.cmd` installs for the current user without administrator rights and adds **Open in Clive** to the File Explorer right-click menu of `.lvl` and `.tcg` files. `README.txt` has the steps for adding the viewer to **Open With** in Visual Studio.
- The package does not contain Torch's grid; each user needs the Torch extension installed.

## Tests

Building the solution compiles `CombinedLevelsViewer.Core.Tests`; it does not execute the tests. Run the test project explicitly through Test Explorer or `dotnet test`.
