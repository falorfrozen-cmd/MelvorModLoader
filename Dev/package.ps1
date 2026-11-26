# Developer tool: packs one mod for upload, from a clean Release build and an explicit list of what goes in.
#   powershell -File Dev\package.ps1 -Mod OneStyle
#   powershell -File Dev\package.ps1 -Mod BulkSell
#   powershell -File Dev\package.ps1 -Mod MelvorModLoader     (the loader itself: override.cfg, README.txt, its folder)
# The zip has the game folder's own layout (mods\<Mod>\...), so a player extracts it into the game folder.
# Nothing personal can get in: only the DLL, README.txt and the folders named in $extra are taken, from the SOURCE tree
# (never from the installed mods folder, where the player's own settings and progress files live).
# After writing the zip it is opened again and checked against the list; the script fails when anything differs.
param([Parameter(Mandatory)][string]$Mod)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Writes the zip entry by entry with forward slashes. (ZipFile.CreateFromDirectory of Windows PowerShell writes "mods\X\X.dll":
# Windows opens that, but a mod manager, 7-Zip on Linux or the Steam Deck makes one file with backslashes in its name.)
function New-Package([string]$stage, [string]$zip) {
    $stream = [IO.File]::Open($zip, [IO.FileMode]::Create)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in Get-ChildItem $stage -Recurse -File | Sort-Object FullName) {
            $name = $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose(); $stream.Dispose() }
}
$src  = Split-Path $PSScriptRoot -Parent
if ($Mod -eq 'MelvorModLoader') {
    # The loader: the game folder's layout again. override.cfg next to Melvor2.exe, the loader's own folder with the
    # .NET runtime the engine needs to start it, and the README from the repository's root.
    $proj = Join-Path $src 'Loader'
    [xml]$csproj = Get-Content (Join-Path $proj 'MelvorModLoader.csproj')
    $version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    $build = Join-Path $env:TEMP 'pkg-build-MelvorModLoader'
    $stage = Join-Path $env:TEMP 'pkg-stage-MelvorModLoader'
    foreach ($d in $build, $stage) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
    $out = & dotnet build $proj -c Release -o $build 2>&1
    if ($LASTEXITCODE -ne 0) { $out | Select-String 'error' | Select-Object -First 10 | ForEach-Object { $_.Line }; throw 'the build of the loader failed' }
    $warnings = @($out | Select-String -Pattern 'warning CS').Count
    $folder = 'data_MelvorModLoader_windows_x86_64'
    New-Item -ItemType Directory -Force (Join-Path $stage $folder) | Out-Null
    # no program and no debug file ships (the README says so): createdump.exe is part of the runtime but not needed
    $files = @(Get-ChildItem $build -File | Where-Object { $_.Extension -notin '.exe', '.pdb' })
    foreach ($f in $files) { Copy-Item $f.FullName (Join-Path $stage $folder) }
    [IO.File]::WriteAllText((Join-Path $stage 'override.cfg'), "[dotnet]`n`nproject/assembly_name=`"MelvorModLoader`"`n", (New-Object Text.UTF8Encoding $false))
    Copy-Item (Join-Path $src 'README.txt') $stage
    $readme = Get-Content (Join-Path $stage 'README.txt') -TotalCount 3
    if (-not ($readme -join ' ').Contains($version)) { throw "README.txt does not name version $version in its first lines" }
    $expected = @('override.cfg', 'README.txt') + @($files | ForEach-Object { "$folder/" + $_.Name })
    foreach ($must in 'MelvorModLoader.dll', 'MelvorModLoader.runtimeconfig.json', 'MelvorModLoader.deps.json', 'coreclr.dll', 'hostfxr.dll', 'System.Private.CoreLib.dll') {
        if ($expected -notcontains "$folder/$must") { throw "the loader build has no $must" }
    }
    $dist = Join-Path $src 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $zip = Join-Path $dist "MelvorModLoader-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    New-Package $stage $zip
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try { $entries = @($archive.Entries | ForEach-Object { $_.FullName }) }
    finally { $archive.Dispose() }
    $missing = @($expected | Where-Object { $entries -notcontains $_ })
    $unexpected = @($entries | Where-Object { $expected -notcontains $_ })
    $banned = @($entries | Where-Object { $_ -match '\.(exe|pdb|json\.tmp|sav|log)$' -or $_ -match 'rescue-pending|hotkeys\.json' })
    if ($missing.Count -or $unexpected.Count -or $banned.Count) {
        Remove-Item $zip
        throw "package check failed. missing: $($missing -join ', '); unexpected: $($unexpected -join ', '); must never ship: $($banned -join ', ')"
    }
    # what changed against the last package of the loader, by file name (a new runtime file is worth a look)
    $last = Get-ChildItem $dist -Filter 'MelvorModLoader-0*.zip' | Where-Object { $_.FullName -ne $zip } | Sort-Object Name | Select-Object -Last 1
    if ($last) {
        $a = [IO.Compression.ZipFile]::OpenRead($last.FullName)
        try { $before = @($a.Entries | ForEach-Object { $_.FullName }) } finally { $a.Dispose() }
        "against $($last.Name): added [$(@($entries | Where-Object { $before -notcontains $_ }) -join ', ')], gone [$(@($before | Where-Object { $entries -notcontains $_ }) -join ', ')]"
    }
    $dll = Join-Path $build 'MelvorModLoader.dll'
    "{0}  {1} file(s), {2:N0} bytes  (build warnings: {3})" -f $zip, $entries.Count, (Get-Item $zip).Length, $warnings
    "MelvorModLoader.dll  version {0}  sha256 {1}" -f [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion, (Get-FileHash $dll).Hash
    return
}
$proj = Join-Path $src "Mods\$Mod"
if (-not (Test-Path (Join-Path $proj "$Mod.csproj"))) { throw "no such mod: $Mod" }
# folders of the mod that ship with it (relative to Mods\<Mod>), and what each must hold
$extra = @{
    OneStyle = @(
        @{ Path = 'data'; Filter = '*.json'; Count = 5 },                 # affixes, base-levels, dungeon-gear, elites, uniques
        @{ Path = 'art\champion'; Filter = '*.webp'; Count = 179 },
        @{ Path = 'art\unique';   Filter = '*.webp'; Count = 179 },
        @{ Path = 'art\items';    Filter = '*.webp'; Count = 58 }
    )
    ActionQueue = @(
        @{ Path = 'lang'; Filter = '*.json'; Count = 1 }                  # en.json, the English for translators
    )
}
# files that must never be in a package (a player's own progress and settings, old DLLs, test output)
$never = 'language.txt', 'elites-progress.json', 'onestyle.json', 'bulksell.json', 'bulksell-auto.json', 'phone.json', 'settings.json', 'hotkeys.json'

[xml]$csproj = Get-Content (Join-Path $proj "$Mod.csproj")
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "no <Version> in $Mod.csproj" }

$build = Join-Path $env:TEMP "pkg-build-$Mod"
$stage = Join-Path $env:TEMP "pkg-stage-$Mod"
foreach ($d in $build, $stage) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
$out = & dotnet build $proj -c Release -o $build 2>&1
if ($LASTEXITCODE -ne 0) { $out | Select-String 'error' | Select-Object -First 10 | ForEach-Object { $_.Line }; throw "the build of $Mod failed" }
$warnings = @($out | Select-String -Pattern 'warning CS').Count

$root = Join-Path $stage "mods\$Mod"
New-Item -ItemType Directory -Force $root | Out-Null
Copy-Item (Join-Path $build "$Mod.dll") $root
Copy-Item (Join-Path $proj 'README.txt') $root
$expected = @("mods/$Mod/$Mod.dll", "mods/$Mod/README.txt")
foreach ($e in $extra[$Mod]) {
    $from = Join-Path $proj $e.Path
    $files = @(Get-ChildItem $from -File -Filter $e.Filter)
    if ($files.Count -ne $e.Count) { throw "$($e.Path): $($files.Count) file(s), expected $($e.Count)" }
    $to = Join-Path $root $e.Path
    New-Item -ItemType Directory -Force $to | Out-Null
    foreach ($f in $files) { Copy-Item $f.FullName $to; $expected += "mods/$Mod/" + ($e.Path -replace '\\', '/') + "/" + $f.Name }
}
# the README must name this version
$readme = Get-Content (Join-Path $root 'README.txt') -TotalCount 3
if (-not ($readme -join ' ').Contains($version)) { throw "README.txt does not name version $version in its first lines" }

$dist = Join-Path $src 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "$Mod-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
New-Package $stage $zip

# open it again: exactly the expected entries, forward slashes, nothing that must never ship
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try { $entries = @($archive.Entries | ForEach-Object { $_.FullName }) }
finally { $archive.Dispose() }
$missing = @($expected | Where-Object { $entries -notcontains $_ })
$unexpected = @($entries | Where-Object { $expected -notcontains $_ })
$banned = @($entries | Where-Object { $n = ($_ -split '/')[-1]; ($never -contains $n) -or ($n -like '*.old*') -or ($n -like '*.sav') -or ($n -like '*.pdb') })
if ($missing.Count -or $unexpected.Count -or $banned.Count) {
    Remove-Item $zip
    throw "package check failed. missing: $($missing -join ', '); unexpected: $($unexpected -join ', '); must never ship: $($banned -join ', ')"
}
$dll = Join-Path $build "$Mod.dll"
"{0}  {1} file(s), {2:N0} bytes  (build warnings: {3})" -f $zip, $entries.Count, (Get-Item $zip).Length, $warnings
"{0}.dll  version {1}  sha256 {2}" -f $Mod, [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion, (Get-FileHash $dll).Hash
