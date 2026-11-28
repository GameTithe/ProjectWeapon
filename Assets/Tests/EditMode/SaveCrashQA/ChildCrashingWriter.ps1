$ErrorActionPreference = 'Stop'

$directory = [IO.Path]::GetFullPath($env:PROJECTWEAPON_CRASH_DIRECTORY)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ([IO.Path]::GetDirectoryName($directory) -ne $tempRoot -or
    [IO.Path]::GetFileName($directory) -notmatch '^ProjectWeapon\.KAN37\.[0-9a-f]{32}$' -or
    -not [IO.Directory]::Exists($directory)) {
    throw 'The writer requires an existing isolated KAN-37 temporary profile.'
}

$stage = $env:PROJECTWEAPON_CRASH_STAGE
$profile = [IO.Path]::Combine($directory, 'profile.json')
$tempFile = [IO.Path]::Combine($directory, 'profile.json.crash.tmp')
$candidate = [IO.File]::ReadAllBytes([IO.Path]::Combine($directory, 'expected.json'))
$lock = [IO.File]::Open($profile + '.lock', [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$partial = $null
try {
    if ($stage -eq 'PartialWrite') {
        $partial = [IO.File]::Open($tempFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $partial.Write($candidate, 0, [int][Math]::Floor($candidate.Length / 2))
        $partial.Flush($true)
    } elseif ($stage -eq 'BeforeReplace' -or $stage -eq 'AfterReplace') {
        # Windows PowerShell supplies .NET Framework. Load the project Editor's .NET Standard 2.1 facade first.
        [void][Reflection.Assembly]::LoadFrom($env:PROJECTWEAPON_CRASH_NETSTANDARD)
        $assembly = [Reflection.Assembly]::LoadFrom($env:PROJECTWEAPON_CRASH_SAVE_ASSEMBLY)
        $operationsType = $assembly.GetType('ProjectWeapon.Save.SnapshotFileOperations', $true)
        $operations = [Activator]::CreateInstance($operationsType, $true)
        $flags = [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic
        $operationsType.GetMethod('WriteAndFlush', $flags).Invoke($operations, [object[]]@($tempFile, $candidate))
        if ($stage -eq 'AfterReplace') {
            $operationsType.GetMethod('Activate', $flags).Invoke($operations, [object[]]@($tempFile, $profile, $true))
        }
    } elseif ($stage -ne 'BeforeWrite') {
        throw 'Unrecognized writer checkpoint.'
    }

    [Console]::WriteLine('Ready ' + $stage)
    [Console]::Out.Flush()
    Start-Sleep -Seconds 30
    throw 'The test parent did not terminate the writer.'
} finally {
    if ($null -ne $partial) { $partial.Dispose() }
    $lock.Dispose()
}
