MCDSaveEdit personal fork v1.0.1
https://github.com/compunaut/MCDSaveEdit

Extract the complete ZIP into a writable folder. Install the serviced Microsoft
.NET 10 Desktop Runtime matching the package architecture (x64 or x86), then run
MCDSaveEdit.exe. Keep every DLL and runtime configuration file beside the EXE.

Save editing works offline. Optional game images/localization require installed
game PAK files and the matching game-content AES key. Enter it in the Game Files
dialog (session only), or configure MCD_PAK_AES_KEY and restart. No game key is
included. Choose the limited-feature mode to edit saves without game assets.

Keep backup copies of your characters. This fork stages save writes, retains
unique .bak backups, and rejects detected external changes. Avoid editing the
same character simultaneously in the game and editor. Input saves are limited
to 64 MiB. Console/game-version compatibility beyond available fixtures and
Steam Deck/Wine remain unverified; test edited copies before replacing a save.

These binaries are not Authenticode-signed. GitHub build-provenance attestations
and immutable release attestations verify origin and asset integrity; they do
not replace a Windows code-signing certificate or guarantee absence of bugs.

The matching source archive and licenses are included in the GitHub release.
Retain all notices. See THIRD-PARTY-NOTICES.md and UPSTREAM-CREDITS.md.

Verify after downloading with a recent GitHub CLI:
  gh release verify v1.0.1 --repo compunaut/MCDSaveEdit
  gh release verify-asset v1.0.1 PACKAGE.zip --repo compunaut/MCDSaveEdit
  gh attestation verify PACKAGE.zip --repo compunaut/MCDSaveEdit
