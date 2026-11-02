# Source provenance

The local fork starts from [CutFlame/MCDSaveEdit](https://github.com/CutFlame/MCDSaveEdit) commit `58fe34efa4cd458992f7dc5af7258450f2e4a1bf`.

The source originally referenced by git submodules is vendored into this fork so parser fixes do not require pushing to upstream:

- `DungeonTools/DungeonTools`: [CutFlame/DungeonTools](https://github.com/CutFlame/DungeonTools/tree/741372821cf7015eea67127a0e8082d600f11f7a), commit `741372821cf7015eea67127a0e8082d600f11f7a`. Original license retained at `DungeonTools/DungeonTools/LICENSE` (AGPL-3.0).
- `PakReader/PakReader`: [CutFlame/PakReader](https://github.com/CutFlame/PakReader/tree/b3d9b07db95c5739d85e909c75864c0f46e4b552), commit `b3d9b07db95c5739d85e909c75864c0f46e4b552`. Original license retained at `PakReader/PakReader/LICENSE` (MIT); retain the README's FModel/GPL attribution as well.

Only the parent wrapper projects are build entry points. Obsolete standalone submodule project manifests and legacy NuGet manifests were retired. The sources are modified by this fork; these directories are ordinary tracked source, not gitlinks. Native DLLs previously checked into Costura32/Costura64 are removed from the fork and replaced by pinned official NuGet runtime assets.

The root MIT license and upstream credits remain intact. This provenance record does not relicense vendored or derived components. Complete the distribution-license review before distributing a combined binary. The ignored `upstream/` directory is the untouched audit reference checkout, not a source dependency or release input.
