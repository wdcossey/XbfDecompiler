# Copies files out of a Windows Phone emulator image (VHD) without changing it: mounts the image read-only, finds the
# MainOS partition (the one with Windows\System32\Windows.UI.Xaml.Phone.dll), copies the listed files and any files
# matching the patterns under System32 (language folders included, named "<folder>__<file>"), and dismounts.
#
# Run it on the reference VM, elevated, in Windows PowerShell 5.1:
#   powershell -File extract-image.ps1 -Out C:\temp\wp81-u1        # Update 1 image, the default file set
#   powershell -File extract-image.ps1 -Image <...>\flash.vhd -Out C:\temp\wp81-rtm
#   powershell -File extract-image.ps1 -Out C:\temp\shell -Files 'Windows\System32\ShellChrome.dll' -Patterns @()
# A running emulator that uses the image as its parent disk is not affected: the mount is read-only.
param(
    [string]$Image = 'C:\Program Files (x86)\Microsoft SDKs\Windows Phone\v8.1\Emulation\Images\flash.update1.vhd',
    [Parameter(Mandatory)] [string]$Out,
    [string[]]$Files = @(
        'Windows\System32\Windows.UI.Xaml.Phone.dll',
        'Windows\System32\Windows.UI.Xaml.dll',
        'Windows\System32\UIXMobileRes.dll',
        'Windows\System32\config\SOFTWARE'
    ),
    [string[]]$Patterns = @('Windows.UI.Xaml*.mui', 'UIXMobileRes*.mui')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null

Mount-DiskImage -ImagePath $Image -Access ReadOnly | Out-Null
try {
    $disk = Get-DiskImage -ImagePath $Image | Get-Disk
    $found = $false
    foreach ($p in ($disk | Get-Partition)) {
        $roots = @()
        if ($p.DriveLetter) { $roots += "$($p.DriveLetter):\" }
        $roots += @($p.AccessPaths | Where-Object { $_ -like '\\?\Volume*' })
        foreach ($root in $roots) {
            if (-not (Test-Path -LiteralPath (Join-Path $root 'Windows\System32\Windows.UI.Xaml.Phone.dll'))) { continue }
            $found = $true
            "MainOS partition $($p.PartitionNumber) at $root"
            foreach ($rel in $Files) {
                $src = Join-Path $root $rel
                if (Test-Path -LiteralPath $src) {
                    Copy-Item -LiteralPath $src -Destination (Join-Path $Out (Split-Path $rel -Leaf)) -Force
                    "copied $rel"
                } else { "missing $rel" }
            }
            $sys = Join-Path $root 'Windows\System32'
            foreach ($pattern in $Patterns) {
                Get-ChildItem -LiteralPath $sys -Recurse -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
                    $rel = $_.FullName.Substring($sys.Length + 1)
                    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Out ($rel -replace '\\', '__')) -Force
                    "copied System32\$rel"
                }
            }
            break
        }
        if ($found) { break }
    }
    if (-not $found) {
        'MainOS not found; partitions:'
        $disk | Get-Partition | Format-Table PartitionNumber, DriveLetter, Size, AccessPaths -AutoSize | Out-String -Width 300
    }
} finally {
    Dismount-DiskImage -ImagePath $Image | Out-Null
    'dismounted'
}
Get-ChildItem $Out -File | ForEach-Object { '{0,-60} {1,10} {2}' -f $_.Name, $_.Length, $_.VersionInfo.FileVersion }
