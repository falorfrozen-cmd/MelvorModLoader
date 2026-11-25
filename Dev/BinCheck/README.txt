BinCheck - after a game update: do the installed mods still fit the game?

Every member (method, field, property accessor) that a mod DLL calls in GamesByMalcs.Melvor2.dll must still be there
with the same signature. A changed signature is what broke One Style 0.4.0 on game 0.11.7 (25 November 2026: a
TypeLoadException that switched its frame handler off). Recompiling does not show such a break when the new parameter
is optional; this does, on the DLLs that are installed.

  dotnet build Dev\BinCheck -c Release -o %TEMP%\bincheck
  %TEMP%\bincheck\bincheck.exe "<game>\data_GamesByMalcs.Melvor2_windows_x86_64\GamesByMalcs.Melvor2.dll" "<game>\mods\OneStyle\OneStyle.dll" ...

It does not see changed behaviour (a new field in the save, a tooltip that now shows after a delay): run the tests too.
