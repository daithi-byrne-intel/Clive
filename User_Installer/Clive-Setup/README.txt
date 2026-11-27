CLIVE (COMBINED LEVELS VIEWER) - INSTALL GUIDE
==============================================

Clive shows LevelsSequences.lvl and BaseLevels.tcg as tables: one table per level set, with every
parameter resolved per selector, clamp values pulled in from the uservars, duplicates highlighted, and
editing that saves back to the .lvl and .tcg files.


WHAT YOU NEED
-------------
- Windows 10 or 11, 64-bit.
- The Torch extension installed in Visual Studio. The viewer uses the table grid that ships with Torch
  (it reads it from %LOCALAPPDATA%\Torch) and cannot show its tables without it. Checked with Torch 5.8.0.2.
- The Microsoft Edge WebView2 Runtime. It is already present on most machines.
- Nothing else: the .NET runtime the viewer needs is included in the "app" folder.


INSTALL
-------
1. Extract the whole package to any folder (do not run it from inside the zip).
2. Double-click Install.cmd.
   It copies the viewer to %LOCALAPPDATA%\Programs\Clive and adds the File Explorer
   right-click command. It needs no administrator rights and changes nothing for other users.
3. Note the path it prints, for example:
	 C:\Users\<you>\AppData\Local\Programs\Clive\Clive.exe
   You need it for the Visual Studio step below.

If scripts are blocked on your machine, install by hand: copy the "app" folder anywhere you like and use
the Clive.exe inside it in the Visual Studio step. Only the File Explorer
right-click command will be missing.


OPEN FROM FILE EXPLORER
-----------------------
Right-click Shared\Common\LevelsSequences.lvl or Shared\Common\BaseLevels.tcg in your repository and choose
"Open in Clive". On Windows 11 the command is under "Show more options".
"Open with > Choose another app" also lists Clive.
The installer does not change which program opens these files when you double-click them.


ADD THE VIEWER TO "OPEN WITH" IN VISUAL STUDIO
----------------------------------------------
Do this once for .lvl files and once for .tcg files. Visual Studio keeps one Open With list per file type.

1. In Solution Explorer, right-click Shared\Common\LevelsSequences.lvl and choose "Open With...".
2. Click "Add...".
3. In "Program", paste the path the installer printed (step 3 of Install).
4. If the dialog has an "Arguments" box, it should contain "%1" (the file path). Leave it as it is.
5. In "Friendly name", type: Clive
6. Click OK. "Clive" is now in the list.
7. Select it and click OK to open the viewer.
   Do NOT click "Set as Default" unless you want the viewer to replace the Torch text editor for that file
   type.
8. Repeat steps 1-7 on Shared\Common\BaseLevels.tcg.

From then on: right-click either file > Open With... > Clive > OK.
Either file opens the same paired view.


USING THE VIEWER
----------------
- The viewer works on the repository the file belongs to. It reads, from that repository:
	Shared\Common\LevelsSequences.lvl
	Shared\Common\BaseLevels.tcg
	Shared\Common\UservarDefinitions.usrv   (read only)
  Opening any other .lvl or .tcg file shows a message instead.
- Left: the level sets. Open a level set to see its test conditions (one per selector).
  Selecting the level set shows the expressions as written. Selecting a test condition shows the values
  resolved for that selector.
- To edit: double-click a cell that is not grey, type the value, then click Save.
  You do not need to press Enter or click elsewhere first: Save, closing the window and selecting another
  level set or test condition all apply the value you are still typing. If that value is not valid,
  nothing is saved and the status bar says why.
  Grey values are calculated or come from uservars; double-click one to see why it cannot be edited.
- Light yellow in the tree marks level sets and test conditions that produce the same values as another.
  Right-click one and choose "Compare with duplicates...".
- Save writes LevelsSequences.lvl and BaseLevels.tcg and leaves a .combinedlevels.bak copy of each next to
  them. If a file was changed elsewhere since the viewer loaded it (for example in Visual Studio), Save
  refuses; use "Reload from disk" first.


UPDATE OR REMOVE
----------------
- Update: close the viewer and run Install.cmd from the new package. It replaces the installed copy.
  The installer prints the version it installs. To see the installed version, right-click Clive.exe >
  Properties > Details > Product version.
- Remove: double-click Uninstall.cmd. To remove the Visual Studio entry: Open With... > select
  Clive > Remove.


IF SOMETHING GOES WRONG
-----------------------
- "Could not find the Torch tabular web components": install the Torch extension, or set the environment
  variable TORCH_WEB_ROOT to the Web folder of a Torch installation.
- The window opens but the table area stays empty: install the Microsoft Edge WebView2 Runtime.
- "... is not one of the files this viewer shows": open LevelsSequences.lvl or BaseLevels.tcg from
  Shared\Common of a repository that contains both.
