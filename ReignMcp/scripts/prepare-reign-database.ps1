[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Provision', 'Backup', 'Restore', 'Verify')]
    [string]$Mode = 'Plan',
    [string]$SourceDistro = 'DwemerAI4Skyrim3',
    [ValidateSet('Reign')][string]$TargetDistro = 'Reign',
    [ValidateRange(1024, 65535)][int]$TargetPort = 5433,
    [string]$RootfsTar,
    [string]$RootfsSha256,
    [string]$InstallDirectory = "$env:LOCALAPPDATA\Bannerlord Reign\wsl",
    [string]$ArtifactDirectory,
    [string]$BackupPath,
    [string]$BackupSha256,
    [Security.SecureString]$DatabasePassword,
    [string]$Confirmation = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Provisioning and cutover are separate from source validation. Default invocation
# is read-only; this script never switches a launcher, stops Skyrim, drops a source
# database, unregisters a distro, or overwrites an existing target database.
if ($SourceDistro -notmatch '^[A-Za-z0-9_.-]+$' -or $SourceDistro -eq $TargetDistro) {
    throw 'Source must be a distinct, simple WSL distribution name.'
}
$plan = [ordered]@{
    schema = 'reign_database_isolation_v1'; mode = $Mode
    sourceDistro = $SourceDistro; sourcePort = 5432
    targetDistro = $TargetDistro; targetPort = $TargetPort
    database = 'Reign'; owner = 'dwemer'; postgresMajor = 15
    cutoverPerformed = $false; originalDatabaseRetained = $true
}
if ($Mode -eq 'Plan') { $plan | ConvertTo-Json; return }
if ($Confirmation -cne "prepare Reign database: $Mode") { throw "Explicit mode confirmation is required: prepare Reign database: $Mode" }

function Invoke-ReignWsl([string]$Distro, [string[]]$Command, [string]$InputText = '') {
    if ($InputText) { $result = $InputText | & wsl.exe -d $Distro -u root -- @Command 2>&1 }
    else { $result = & wsl.exe -d $Distro -u root -- @Command 2>&1 }
    if ($LASTEXITCODE -ne 0) { throw "WSL operation failed in $Distro (exit $LASTEXITCODE). No cutover was performed." }
    return ($result -join "`n").Trim()
}
function Invoke-ReignSql([string]$Distro, [int]$Port, [string]$Database, [string]$Sql) {
    Invoke-ReignWsl $Distro @('runuser', '-u', 'postgres', '--', 'psql', '-X', '-q', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-p', "$Port", '-d', $Database) $Sql
}
function Get-ReignLinuxPath([string]$Distro, [string]$Path) {
    Invoke-ReignWsl $Distro @('wslpath', '-a', '-u', [IO.Path]::GetFullPath($Path))
}
function Assert-ReignHash([string]$Path, [string]$Expected) {
    if ($Expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Expected) {
        throw 'Artifact SHA-256 verification failed.'
    }
}

if ($Mode -eq 'Provision') {
    if (!$RootfsTar -or !$DatabasePassword) { throw 'Provision requires a verified clean Debian 12 rootfs and the Reign database password as SecureString.' }
    Assert-ReignHash $RootfsTar $RootfsSha256
    $distros = ((& wsl.exe --list --quiet) -replace "`0", '')
    if ($distros.Trim() -contains $TargetDistro) { throw 'Target distro already exists; refusing to overwrite it.' }
    if (Test-Path -LiteralPath $InstallDirectory) { throw 'WSL install directory must not exist.' }
    if (Get-NetTCPConnection -LocalPort $TargetPort -State Listen -ErrorAction SilentlyContinue) { throw 'Target Windows port is already in use.' }
    & wsl.exe --import $TargetDistro ([IO.Path]::GetFullPath($InstallDirectory)) ([IO.Path]::GetFullPath($RootfsTar)) --version 2
    if ($LASTEXITCODE -ne 0) { throw 'WSL import failed; no source state changed.' }
    $os = Invoke-ReignWsl $TargetDistro @('cat', '/etc/os-release')
    if ($os -notmatch 'ID=debian' -or $os -notmatch 'VERSION_ID="12"') { throw 'Imported rootfs must be clean Debian 12. Inspect it before continuing.' }
    $null = Invoke-ReignWsl $TargetDistro @('apt-get', 'update')
    $null = Invoke-ReignWsl $TargetDistro @('env', 'DEBIAN_FRONTEND=noninteractive', 'apt-get', 'install', '-y', 'postgresql-15', 'postgresql-client-15')
    $null = Invoke-ReignWsl $TargetDistro @('pg_conftool', '15', 'main', 'set', 'port', "$TargetPort")
    $null = Invoke-ReignWsl $TargetDistro @('service', 'postgresql', 'restart')
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($DatabasePassword)
    try {
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ($password -match '[\x00\r\n]') { throw 'Password contains unsupported control characters.' }
        $escaped = $password.Replace("'", "''")
        $null = Invoke-ReignSql $TargetDistro $TargetPort 'postgres' "CREATE ROLE dwemer LOGIN CREATEDB PASSWORD '$escaped';"
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        $password = $null; $escaped = $null
    }
    # Existing runtime identity checks require the Reign owner to remain dwemer.
    $plan['provisioned'] = $true
}
elseif ($Mode -eq 'Backup') {
    if (!$ArtifactDirectory) { throw 'Backup requires a new artifact directory.' }
    if (Test-Path -LiteralPath $ArtifactDirectory) { throw 'Artifact directory must not exist; refusing to overwrite a backup.' }
    $null = New-Item -ItemType Directory -Path $ArtifactDirectory
    $dump = Join-Path ([IO.Path]::GetFullPath($ArtifactDirectory)) 'Reign.dump'
    $linuxDump = Get-ReignLinuxPath $SourceDistro $dump
    $null = Invoke-ReignWsl $SourceDistro @('runuser', '-u', 'postgres', '--', 'pg_dump', '-p', '5432', '-d', 'Reign', '-Fc', '-f', $linuxDump)
    $plan['backupPath'] = $dump
    $plan['backupSha256'] = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $plan | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'backup-receipt.json') -Encoding UTF8
}
elseif ($Mode -eq 'Restore') {
    if (!$BackupPath) { throw 'Restore requires a backup and its recorded SHA-256.' }
    Assert-ReignHash $BackupPath $BackupSha256
    $existing = Invoke-ReignSql $TargetDistro $TargetPort 'postgres' "SELECT COUNT(*) FROM pg_database WHERE datname='Reign';"
    if ($existing -ne '0') { throw 'Target Reign database already exists; refusing to overwrite it.' }
    $null = Invoke-ReignSql $TargetDistro $TargetPort 'postgres' 'CREATE DATABASE "Reign" OWNER dwemer ENCODING ''UTF8'' TEMPLATE template0;'
    $linuxDump = Get-ReignLinuxPath $TargetDistro $BackupPath
    $null = Invoke-ReignWsl $TargetDistro @('runuser', '-u', 'postgres', '--', 'pg_restore', '-p', "$TargetPort", '-d', 'Reign', '--exit-on-error', '--single-transaction', '--no-owner', '--no-privileges', '--role=dwemer', $linuxDump)
    $plan['restored'] = $true
    $plan['sourceBackupSha256'] = $BackupSha256
}
else {
    $clientsSql = "SELECT COUNT(*) FROM pg_stat_activity WHERE datname='Reign' AND pid<>pg_backend_pid() AND backend_type='client backend';"
    foreach ($endpoint in @(@($SourceDistro,5432),@($TargetDistro,$TargetPort))) {
        if ((Invoke-ReignSql $endpoint[0] $endpoint[1] 'Reign' $clientsSql) -ne '0') {
            throw 'Verification requires the Reign lifetime group and other Reign database clients to be closed. Skyrim databases remain independent.'
        }
    }
    $owner = Invoke-ReignSql $TargetDistro $TargetPort 'Reign' "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database();"
    if ($owner -cne 'dwemer') { throw 'Target Reign database owner must be dwemer.' }
    # Compare definitions without database-local OIDs, dump timestamps or passwords.
    # This covers defaults, nullability, checks, keys, indexes, routines and sequences.
    $schemaSql = @'
WITH scope AS (SELECT oid,nspname FROM pg_namespace WHERE nspname IN ('public','reign_meta') OR nspname LIKE 'reign_campaign_%' OR nspname LIKE 'reign_save_%'),
definitions AS (
 SELECT 'column|'||n.nspname||'|'||c.relname||'|'||a.attnum||'|'||a.attname||'|'||format_type(a.atttypid,a.atttypmod)||'|'||a.attnotnull||'|'||a.attidentity||'|'||a.attgenerated||'|'||COALESCE(pg_get_expr(d.adbin,d.adrelid),'') AS definition
 FROM scope n JOIN pg_class c ON c.relnamespace=n.oid JOIN pg_attribute a ON a.attrelid=c.oid
 LEFT JOIN pg_attrdef d ON d.adrelid=c.oid AND d.adnum=a.attnum WHERE a.attnum>0 AND NOT a.attisdropped AND c.relkind IN ('r','p','v','m')
 UNION ALL SELECT 'constraint|'||n.nspname||'|'||c.relname||'|'||con.conname||'|'||pg_get_constraintdef(con.oid,true) FROM scope n JOIN pg_class c ON c.relnamespace=n.oid JOIN pg_constraint con ON con.conrelid=c.oid
 UNION ALL SELECT 'index|'||n.nspname||'|'||pg_get_indexdef(i.indexrelid) FROM scope n JOIN pg_class c ON c.relnamespace=n.oid JOIN pg_index i ON i.indrelid=c.oid
 UNION ALL SELECT 'routine|'||n.nspname||'|'||pg_get_functiondef(p.oid) FROM scope n JOIN pg_proc p ON p.pronamespace=n.oid WHERE p.prokind IN ('f','p')
 UNION ALL SELECT 'view|'||n.nspname||'|'||c.relname||'|'||pg_get_viewdef(c.oid,true) FROM scope n JOIN pg_class c ON c.relnamespace=n.oid WHERE c.relkind IN ('v','m')
 UNION ALL SELECT 'sequence|'||s.schemaname||'|'||s.sequencename||'|'||s.start_value||'|'||s.min_value||'|'||s.max_value||'|'||s.increment_by||'|'||s.cycle||'|'||s.cache_size FROM pg_sequences s JOIN scope n ON n.nspname=s.schemaname
)
SELECT md5(COALESCE(string_agg(definition,E'\n' ORDER BY definition),'')) FROM definitions;
'@
    $schemaHash = Invoke-ReignSql $SourceDistro 5432 'Reign' $schemaSql
    if ($schemaHash -cne (Invoke-ReignSql $TargetDistro $TargetPort 'Reign' $schemaSql)) { throw 'Source/target schema definitions differ.' }
    $wrongOwners = Invoke-ReignSql $TargetDistro $TargetPort 'Reign' "SELECT COUNT(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind IN ('r','p','S','v','m') AND (n.nspname='reign_meta' OR n.nspname LIKE 'reign_campaign_%' OR n.nspname LIKE 'reign_save_%') AND pg_get_userbyid(c.relowner)<>'dwemer';"
    if ($wrongOwners -ne '0') { throw 'Target Reign schema objects are not owned by dwemer.' }
    # Compare row content, not just counts. Run while Reign writes are stopped.
    # JSON rows are sorted for stable fingerprints and include persisted receipts.
    $tablesSql = "SELECT quote_ident(schemaname)||'.'||quote_ident(tablename) FROM pg_tables WHERE schemaname IN ('public','reign_meta') OR schemaname LIKE 'reign_campaign_%' OR schemaname LIKE 'reign_save_%' ORDER BY 1;"
    $sourceTables = Invoke-ReignSql $SourceDistro 5432 'Reign' $tablesSql
    $targetTables = Invoke-ReignSql $TargetDistro $TargetPort 'Reign' $tablesSql
    if ($sourceTables -cne $targetTables) { throw 'Source/target table inventories differ.' }
    $verified = 0
    foreach ($table in ($sourceTables -split "`n" | Where-Object { $_ })) {
        $sql = "SELECT COUNT(*)::text||':'||md5(COALESCE(string_agg(row_hash,'' ORDER BY row_hash),'')) FROM (SELECT md5(row_to_json(t)::text) AS row_hash FROM $table t) rows;"
        $source = Invoke-ReignSql $SourceDistro 5432 'Reign' $sql
        $target = Invoke-ReignSql $TargetDistro $TargetPort 'Reign' $sql
        if ($source -cne $target) { throw "Content verification failed for $table." }
        $verified++
    }
    $plan['verifiedTables'] = $verified
    $sequencesSql = "SELECT quote_ident(schemaname)||'.'||quote_ident(sequencename) FROM pg_sequences WHERE schemaname IN ('public','reign_meta') OR schemaname LIKE 'reign_campaign_%' OR schemaname LIKE 'reign_save_%' ORDER BY 1;"
    $sourceSequences = Invoke-ReignSql $SourceDistro 5432 'Reign' $sequencesSql
    if ($sourceSequences -cne (Invoke-ReignSql $TargetDistro $TargetPort 'Reign' $sequencesSql)) { throw 'Source/target sequence inventories differ.' }
    $verifiedSequences = 0
    foreach ($sequence in ($sourceSequences -split "`n" | Where-Object { $_ })) {
        $sql = "SELECT last_value::text||':'||is_called::text FROM $sequence;"
        if ((Invoke-ReignSql $SourceDistro 5432 'Reign' $sql) -cne (Invoke-ReignSql $TargetDistro $TargetPort 'Reign' $sql)) { throw "Sequence state differs for $sequence." }
        $verifiedSequences++
    }
    foreach ($endpoint in @(@($SourceDistro,5432),@($TargetDistro,$TargetPort))) {
        if ((Invoke-ReignSql $endpoint[0] $endpoint[1] 'Reign' $clientsSql) -ne '0') { throw 'A Reign database client appeared during verification; repeat with both endpoints frozen.' }
    }
    $plan['schemaDefinitionFingerprint'] = $schemaHash
    $plan['verifiedSequences'] = $verifiedSequences
    $plan['ownershipVerified'] = $true
    $plan['nativeCheckpointRestoreStillRequired'] = $true
}
$plan | ConvertTo-Json
