#requires -Version 7
<#
.SYNOPSIS
    FuelControl o'rnatuvchisining administratorsiz sinovi (yig'ilgan Output\FuelControl-Setup-<versiya>.exe ustida).

.DESCRIPTION
    O'rnatuvchi /CURRENTUSER va /NOSYSTEM=1 bilan ishga tushiriladi: Windows xizmati, xavfsizlik devori, ruxsatlar va
    cloudflared xizmati tegilmaydi (ularni qo'lda sinash: docs\ornatish.md, 16-bo'lim). Tekshiriladi:
      1. noto'g'ri qiymatlarda o'rnatuvchi hech narsa o'rnatmay chiqadi;
      2. birinchi o'rnatish: fayllar, appsettings.Production.json (port, yo'llar, JWT kaliti, admin paroli), versiya;
      3. o'rnatilgan haqiqiy API Production rejimida (127.0.0.1:<port>) ishga tushadi: /openapi, "/" (PWA), admin login, jurnal;
         toza baza: faqat admin (/me), yoqilg'i/aparat/smena/nasiya yo'q, aparatsiz smena ochib bo'lmaydi (demo ma'lumot yozilmaydi);
      4. yangilash: JWT kaliti saqlanadi, parol so'ralmaydi, appsettings.Local.json (CORS) saqlanadi, baza saqlanadi;
      5. faqat desktop; 6. tunnel komponenti (token bilan/tokensiz); 7. o'chirish: ma'lumotlar saqlanadi.
    Sinov vaqtinchalik papkada ishlaydi; muvaffaqiyatli tugasa u o'chiriladi. Xato bo'lsa o'rnatuvchi jurnallari qoladi.
    Bu mashinada FuelControl allaqachon o'rnatilgan bo'lsa (HKCU/HKLM), sinov to'xtaydi — o'rnatilishni buzmaslik uchun.

.PARAMETER Setup
    Sinaladigan o'rnatuvchi (standart: Output\FuelControl-Setup-<versiya>.exe, versiya Directory.Build.props dan).
.PARAMETER WorkDir
    Vaqtinchalik ish papkasi (nomida "fuelcontrol" bo'lishi shart).
.PARAMETER BasePort
    Sinov uchun birinchi port; BasePort..BasePort+3 band bo'lmasligi kerak.
.PARAMETER SkipTunnel
    O'rnatuvchi cloudflared'siz yig'ilgan bo'lsa (build.ps1 -SkipCloudflared), tunnel sinovini o'tkazib yuboradi.
.PARAMETER OldSetup
    Avvalgi versiya o'rnatuvchisi (joriy API shaklidagi reliz, >= 0.1.5; masalan, Output\FuelControl-Setup-0.1.5.exe). Berilsa, mijoz
    qurilmasidagi (jonli bazali) reja sinaladi: eski versiya o'rnatiladi va unda mijozdagidek ma'lumot yaratiladi (3 foydalanuvchi, 2 yoqilg'i
    va aparat - 5-aparatda krug 10 000; bak tuzatish va kirimi; operator yopgan smena: nasiya, xarajat, kamomat; avans; qo'lda zaxira nusxa;
    Telegram tokeni va chat ID (soxta token) bilan sinov xabari; yana bitta OCHIQ smena: unda nasiya, qarz qaytishi va narx o'zgarishi
    segmenti) -> o'chiriladi (ma'lumot papkasi SAQLANADI; hamma jadvalning barmoq izi va zaxira faylining xeshi olinadi) -> yangisi
    /ADMINPAROL'siz o'rnatiladi (eski parol, foydalanuvchilar va yaratilgan ma'lumot joyida; o'rnatish va API'ning birinchi ishga tushishi HECH
    BIR jadvalni o'zgartirmaydi, migratsiya yo'q; Telegram tokeni va holati, zaxira nusxasi bayt-bayt joyida) -> POST /auth/yangila o'rnatilgan
    API'da (yangi token, operator, yaroqsiz/tokensiz 401), appsettings.Local.json dagi Avtorizatsiya:Muddat -> eski ochiq smena yopiladi va fonda
    zaxira nusxa olinadi -> /ADMINPAROL berilsa ham e'tiborga olinmaydi, o'chirib qayta o'rnatganda (JWT kaliti yangilanadi) Telegram tokeni
    baribir saqlanadi -> ma'lumot papkasi o'chirilib toza o'rnatiladi.
.PARAMETER KeepFiles
    Muvaffaqiyatli tugaganda ham ish papkasini o'chirmaydi.

.EXAMPLE
    .\test-installer.ps1
#>
[CmdletBinding()]
param(
    [string] $Setup,
    [string] $WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'fuelcontrol-installer-test'),
    [int] $BasePort = 5090,
    [switch] $SkipTunnel,
    [string] $OldSetup,
    [switch] $KeepFiles
)

$ErrorActionPreference = 'Stop'
$Installer = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $Installer '..\..')).Path
[xml] $props = Get-Content -LiteralPath (Join-Path $Repo 'Directory.Build.props') -Raw
$Version = $props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
if (-not $Setup) { $Setup = Join-Path $Installer "Output\FuelControl-Setup-$Version.exe" }
if (-not (Test-Path -LiteralPath $Setup)) { throw "O'rnatuvchi topilmadi: $Setup (avval build.ps1)." }
$T = $WorkDir
if ((Split-Path $T -Leaf) -notmatch 'fuelcontrol') { throw "WorkDir nomida 'fuelcontrol' bo'lishi shart (tasodifan boshqa papkani o'chirmaslik uchun): $T" }

$script:fail = 0
$script:total = 0
$script:apiProc = $null
$env:ASPNETCORE_ENVIRONMENT = 'Production'; $env:DOTNET_ENVIRONMENT = 'Production'

function Fresh([string] $p) { if (Test-Path -LiteralPath $p) { [IO.Directory]::Delete($p, $true) }; [void](New-Item -ItemType Directory -Force -Path $p) }
function Check($cond, [string] $msg) {
    $script:total++
    $ok = if ($cond -is [array]) { @($cond).Count -gt 0 } else { [bool] $cond }
    if ($ok) { "  OK    $msg" } else { "  XATO  $msg"; $script:fail++ }
}
function Json([string] $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }

function Run-Setup([string[]] $Extra, [string] $LogName, [string] $Exe = $Setup) {
    $a = $Extra + @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOSYSTEM=1', "/LOG=$T\$LogName.log")
    (Start-Process -FilePath $Exe -ArgumentList $a -Wait -PassThru).ExitCode
}

function Start-Api([string] $app, [int] $port) {
    # Xavfsizlik devori oynasi chiqmasligi uchun sinovda 0.0.0.0 o'rniga 127.0.0.1 (kalit "Urls" shu fayldan o'qilishi baribir tekshiriladi).
    $cfg = "$app\Server\appsettings.Production.json"
    (Get-Content -LiteralPath $cfg -Raw).Replace('http://0.0.0.0:', 'http://127.0.0.1:') | Set-Content -LiteralPath $cfg -NoNewline -Encoding utf8NoBOM
    $script:apiProc = Start-Process -FilePath "$app\Server\FuelControl.Api.exe" -WorkingDirectory "$app\Server" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$T\api-out-$port.txt" -RedirectStandardError "$T\api-err-$port.txt"
    for ($i = 0; $i -lt 60; $i++) {
        try { if ((Invoke-WebRequest "http://127.0.0.1:$port/openapi/v1.json" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { return $true } } catch { }
        if ($script:apiProc.HasExited) { return $false }
        Start-Sleep -Seconds 1
    }
    return $false
}

# Faqat shu sinov ishga tushirgan jarayon to'xtatiladi (boshqa API'larga tegilmaydi).
function Stop-Api { if ($script:apiProc -and -not $script:apiProc.HasExited) { Stop-Process -Id $script:apiProc.Id -Force; $script:apiProc.WaitForExit(10000) | Out-Null }; $script:apiProc = $null }

# GET -> JSON obyektlar (massiv elementlari alohida, bo'sh massiv - hech narsa). Invoke-RestMethod bo'sh massivni $null qilib qaytaradi va
# @($null).Count = 1 bo'ladi, shuning uchun xom matn parse qilinadi.
function Get-Json([int] $port, [string] $token, [string] $path) {
    (Invoke-WebRequest "http://127.0.0.1:$port$path" -Headers @{ Authorization = "Bearer $token" } -UseBasicParsing).Content | ConvertFrom-Json
}

function Count-Json([int] $port, [string] $token, [string] $path) { @(Get-Json $port $token $path).Count }

function Post-Json([int] $port, [string] $token, [string] $path, $body) {
    Invoke-RestMethod "http://127.0.0.1:$port$path" -Method Post -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 6)))
}

# POST -> (HTTP kod, javob matni): 400/403 kabi kutilgan xatolarni tekshirish uchun (Post-Json bunda istisno tashlaydi).
function Post-Xato([int] $port, [string] $token, [string] $path, $body) {
    try { [void](Post-Json $port $token $path $body); [pscustomobject] @{ Kod = 200; Matn = '' } }
    catch { [pscustomobject] @{ Kod = [int] $_.Exception.Response.StatusCode; Matn = "$($_.ErrorDetails.Message)" } }
}

function Put-Json([int] $port, [string] $token, [string] $path, $body) {
    Invoke-RestMethod "http://127.0.0.1:$port$path" -Method Put -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 6)))
}

# PUT -> (HTTP kod, javob matni): 400/403/404/409 kabi kutilgan xatolarni tekshirish uchun.
function Put-Xato([int] $port, [string] $token, [string] $path, $body) {
    try { [void](Put-Json $port $token $path $body); [pscustomobject] @{ Kod = 200; Matn = '' } }
    catch { [pscustomobject] @{ Kod = [int] $_.Exception.Response.StatusCode; Matn = "$($_.ErrorDetails.Message)" } }
}

# Baza faylini o'qish (faqat SELECT/PRAGMA): Windows bilan keladigan winsqlite3.dll - qo'shimcha paket yoki dastur kerak emas.
# Har qator - ustunlar '|' bilan birlashtirilgan matn (NULL - bo'sh). Baza yopiq bo'lishi kerak (API to'xtatilgan).
$script:SqliteKodi = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class SqliteOqish
{
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] fayl, out IntPtr db, int bayroqlar, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bayt, out IntPtr stmt, IntPtr qoldiq);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int ustun);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);

    static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s + (char)0); }

    public static string[] Soro(string fayl, string sql)
    {
        IntPtr db;
        if (sqlite3_open_v2(Utf8(fayl), out db, 2, IntPtr.Zero) != 0) // 2 = SQLITE_OPEN_READWRITE (bazani yaratmaydi)
        {
            var xabar = db == IntPtr.Zero ? "xotira yetmadi" : Marshal.PtrToStringUTF8(sqlite3_errmsg(db));
            if (db != IntPtr.Zero) sqlite3_close(db);
            throw new InvalidOperationException("SQLite ochilmadi (" + fayl + "): " + xabar);
        }
        try
        {
            IntPtr stmt;
            if (sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero) != 0)
                throw new InvalidOperationException("SQLite: " + Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) + " [" + sql + "]");
            try
            {
                var qatorlar = new List<string>();
                int n = sqlite3_column_count(stmt), kod;
                while ((kod = sqlite3_step(stmt)) == 100) // SQLITE_ROW
                {
                    var ustunlar = new string[n];
                    for (int i = 0; i < n; i++)
                    {
                        var p = sqlite3_column_text(stmt, i);
                        ustunlar[i] = p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p);
                    }
                    qatorlar.Add(string.Join("|", ustunlar));
                }
                if (kod != 101) throw new InvalidOperationException("SQLite step kodi " + kod + ": " + Marshal.PtrToStringUTF8(sqlite3_errmsg(db))); // 101 = SQLITE_DONE
                return qatorlar.ToArray();
            }
            finally { sqlite3_finalize(stmt); }
        }
        finally { sqlite3_close(db); }
    }
}
'@

function Sql-Qatorlar([string] $fayl, [string] $sql) {
    if (-not ('SqliteOqish' -as [type])) { Add-Type -TypeDefinition $script:SqliteKodi }
    , @([SqliteOqish]::Soro($fayl, $sql))
}

# Jadvallarning ustun ro'yxati: jadval -> '"A", "B", ...' (tartib bo'yicha). Yangilashdan oldingi sxemani eslab qolish uchun.
function Baza-Ustunlari([string] $fayl) {
    $jadvallar = Sql-Qatorlar $fayl "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
    $h = @{}
    foreach ($nom in $jadvallar) {
        $ust = Sql-Qatorlar $fayl "PRAGMA table_info(`"$nom`")"
        $h[$nom] = (@($ust | ForEach-Object { '"' + $_.Split('|')[1] + '"' })) -join ', '
    }
    $h
}

# Baza tarkibining barmoq izi: har bir jadval -> "qatorlar soni|SHA256 (hamma qator, birinchi ustun bo'yicha tartiblangan, ustunlar '|' bilan)".
# "Ma'lumot saqlandi" - qator sonigina emas, har bir qiymat bo'yicha tekshiriladi (jonli bazani yangilashda muhim). Baza yopiq bo'lishi kerak.
# ustunlar (ixtiyoriy, Baza-Ustunlari natijasi): faqat shu ustunlar olinadi - yangilash yangi ustun qo'shsa ham eski ma'lumotni solishtirish uchun.
function Baza-Izi([string] $fayl, $ustunlar = $null) {
    $jadvallar = Sql-Qatorlar $fayl "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
    $iz = [ordered] @{}
    foreach ($nom in $jadvallar) {
        $tanlash = if ($ustunlar -and $ustunlar.ContainsKey($nom)) { $ustunlar[$nom] } else { '*' }
        $qatorlar = Sql-Qatorlar $fayl "SELECT $tanlash FROM `"$nom`" ORDER BY 1"
        $xesh = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($qatorlar -join "`n"))))
        $iz[$nom] = "$($qatorlar.Count)|$($xesh.Substring(0, 16))"
    }
    $iz
}

function Iz-Soni($iz, [string] $jadval) { [int] ($iz[$jadval].Split('|')[0]) }

# Ikki barmoq izidagi farq qiladigan (yoki birida yo'q) jadvallar nomlari; istisno - kutilgan o'zgarishlar.
function Iz-Farqi($a, $b, [string[]] $istisno = @()) {
    @(@($a.Keys) + @($b.Keys) | Select-Object -Unique | Where-Object { $istisno -notcontains $_ -and $a[$_] -ne $b[$_] })
}

function Login([int] $port, [string] $pwd, [string] $login = 'admin') {
    $body = @{ login = $login; parolYokiPin = $pwd } | ConvertTo-Json
    try { Invoke-RestMethod "http://127.0.0.1:$port/auth/login" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) } catch { $null }
}

# API jurnal fayllari (Serilog shared): API ishlab turganda ham o'qiladi.
function Log-Matni([string] $papka) {
    $m = ''
    foreach ($f in Get-ChildItem -LiteralPath $papka -Filter 'fuelcontrol-*.log' -ErrorAction SilentlyContinue) {
        $fs = [IO.File]::Open($f.FullName, 'Open', 'Read', 'ReadWrite')
        try { $m += (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
    }
    $m
}

function Uninstall-One([string] $dir) {
    if (Test-Path "$dir\unins000.exe") { Start-Process -FilePath "$dir\unins000.exe" -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait | Out-Null }
}

function Uninstall-All { foreach ($n in 1..6) { Uninstall-One "$T\app$n" } }

# ---------- xavfsizlik: haqiqiy o'rnatilgan FuelControl'ni buzmaslik; portlar band emas ----------
$iss = Get-Content -LiteralPath (Join-Path $Installer 'FuelControl.iss') -Raw
$appId = [regex]::Match($iss, '(?m)^AppId=\{\{([0-9A-Fa-f-]{36})\}').Groups[1].Value
if (-not $appId) { throw "FuelControl.iss ichida AppId topilmadi." }
foreach ($hive in 'HKCU', 'HKLM') {
    foreach ($view in 'Software\Microsoft\Windows\CurrentVersion\Uninstall', 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall') {
        $k = "${hive}:\$view\{$appId}_is1"
        if (Test-Path $k) { throw "Bu mashinada FuelControl allaqachon o'rnatilgan ($k). Sinov uning ro'yxatdan o'tishini buzgan bo'lardi: avval uni o'chiring yoki toza mashinada ishga tushiring." }
    }
}
$busy = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port
foreach ($pt in $BasePort..($BasePort + 3)) { if ($busy -contains $pt) { throw "Port $pt band. -BasePort bilan boshqasini tanlang." } }
$p1, $p2, $p3, $p4 = $BasePort..($BasePort + 3)

try {
    Write-Host "Sinov: $Setup (versiya $Version), ish papkasi: $T"
    Fresh $T
    $app1 = "$T\app1"; $data1 = "$T\data1"
    # Maxfiy parol: teskari chiziq, qavslar, |, vergul, %, apostrof va unicode (JSON/qator qochirish tekshiruvi).
    $pwd1 = "P@ss\w0rd{x}|y,z%1'" + [char]0xE9

    "== 1. Noto'g'ri qiymatlar (o'rnatuvchi hech narsa o'rnatmay chiqishi kerak) =="
    foreach ($case in @(
        @{ N = "port 99999";             A = @("/DIR=$T\bad1", "/DATADIR=$T\baddata", '/PORT=99999', '/ADMINPAROL=KuchliParol1', '/COMPONENTS=server,desktop') },
        @{ N = "qisqa parol";            A = @("/DIR=$T\bad2", "/DATADIR=$T\baddata", "/PORT=$p1", '/ADMINPAROL=short', '/COMPONENTS=server,desktop') },
        @{ N = "ftp:// server manzili";  A = @("/DIR=$T\bad3", "/DATADIR=$T\baddata", '/SERVERURL=ftp://x', '/COMPONENTS=desktop') },
        @{ N = "server siz tunnel";      A = @("/DIR=$T\bad4", "/DATADIR=$T\baddata", '/COMPONENTS=tunnel') },
        @{ N = "hech narsa tanlanmagan"; A = @("/DIR=$T\bad5", "/DATADIR=$T\baddata", '/COMPONENTS=') })) {
        $dir = ($case.A | Where-Object { $_ -like '/DIR=*' }).Substring(5)
        $code = Run-Setup $case.A ('bad-' + ($case.N -replace '\W', '_'))
        Check (($code -ne 0) -and -not (Test-Path "$dir\Server") -and -not (Test-Path "$dir\Desktop")) "$($case.N): chiqish kodi $code, fayl o'rnatilmadi"
    }

    "== 2. Birinchi o'rnatish: server + desktop, port $p1 =="
    $code = Run-Setup @("/DIR=$app1", "/DATADIR=$data1", "/PORT=$p1", "/ADMINPAROL=$pwd1", '/COMPONENTS=server,desktop', '/TASKS=') 'install1'
    Check ($code -eq 0) "o'rnatuvchi chiqish kodi 0 (kod $code)"
    foreach ($f in 'Server\FuelControl.Api.exe', 'Server\wwwroot\index.html', 'Server\appsettings.json', 'Server\appsettings.Production.json', 'Server\appsettings.Local.json',
                   'Desktop\FuelControl.exe', 'Desktop\sozlama.json', 'FuelControl Web.url', 'fuelcontrol.ico', 'nosystem.flag', 'unins000.exe') {
        Check (Test-Path "$app1\$f") "mavjud: $f"
    }
    foreach ($f in 'Server\appsettings.Development.json', 'Server\appsettings.Web.json', 'Tunnel\cloudflared.exe') {
        Check (-not (Test-Path "$app1\$f")) "YO'Q (bo'lmasligi kerak): $f"
    }
    Check ((Get-Item "$app1\Server\FuelControl.Api.exe").VersionInfo.ProductVersion -eq $Version) "API versiyasi $Version"
    Check ((Get-Item "$app1\Desktop\FuelControl.exe").VersionInfo.ProductVersion -eq $Version) "Desktop versiyasi $Version"
    Check ((Test-Path "$data1\zaxira") -and (Test-Path "$data1\logs")) "ma'lumot papkalari yaratildi (zaxira, logs)"
    $cfg = Json "$app1\Server\appsettings.Production.json"
    Check ($cfg.Urls -eq "http://0.0.0.0:$p1") "Urls = $($cfg.Urls)"
    Check ($cfg.ConnectionStrings.Baza -eq "Data Source=$data1\fuelcontrol.db") "baza yo'li: $($cfg.ConnectionStrings.Baza)"
    Check (($cfg.Zaxira.Papka -eq "$data1\zaxira") -and ($cfg.Log.Papka -eq "$data1\logs")) "zaxira va jurnal yo'llari"
    Check ($cfg.Jwt.Kalit -match '^[0-9a-f]{64}$') "JWT kaliti 64 hex: $($cfg.Jwt.Kalit.Substring(0, 8))..."
    Check ($cfg.Seed.AdminParol -ceq $pwd1) "AdminParol maxsus belgilar bilan to'g'ri yozilgan"
    $key1 = $cfg.Jwt.Kalit
    $loc = Json "$app1\Server\appsettings.Local.json"
    Check (($loc.Cors.Manbalar | Measure-Object).Count -eq 0) "Local.json: bo'sh Cors:Manbalar"
    Check ((Json "$app1\Desktop\sozlama.json").ServerManzili -eq "http://localhost:$p1") "desktop sozlama.json: http://localhost:$p1"
    Check ((Get-Content "$app1\FuelControl Web.url" -Raw) -match "URL=http://localhost:$p1") "Web yorlig'i: localhost:$p1"

    "== 3. Haqiqiy publish qilingan API Production rejimida (konsol), 127.0.0.1:$p1 =="
    $up = Start-Api $app1 $p1
    Check $up "API ishga tushdi va /openapi/v1.json 200 qaytardi"
    if ($up) {
        $root = Invoke-WebRequest "http://127.0.0.1:$p1/" -UseBasicParsing
        Check (($root.StatusCode -eq 200) -and ($root.Content -match '<app-root|<title')) "'/' PWA index.html (200, $($root.Headers['Content-Type']))"
        $mf = Invoke-WebRequest "http://127.0.0.1:$p1/manifest.webmanifest" -UseBasicParsing
        Check ($mf.Headers['Content-Type'] -match 'application/manifest\+json') "manifest MIME: $($mf.Headers['Content-Type'])"
        $r = Login $p1 $pwd1
        Check ($null -ne $r -and $r.foydalanuvchi.login -eq 'admin') "admin login'i o'rnatishdagi (maxsus belgili) parol bilan ishladi"
        Check ($null -eq (Login $p1 'notogri-parol')) "noto'g'ri parol rad etildi"
        # Toza o'rnatish (Production, baza yo'q): bazada faqat admin; demo, yoqilg'i, aparat, smena, nasiya yaratilmaydi.
        $api = "http://127.0.0.1:$p1"; $hdr = @{ Authorization = "Bearer $($r.token)" }
        $me = Invoke-RestMethod "$api/me" -Headers $hdr
        Check (($me.login -eq 'admin') -and ($me.rol -eq 'Admin')) "/me = admin (Admin)"
        Check ((Count-Json $p1 $r.token '/foydalanuvchilar') -eq 1) "bazada bitta foydalanuvchi (admin)"
        foreach ($yol in '/yoqilgilar', '/aparatlar', '/smenalar') {
            Check ((Invoke-WebRequest "$api$yol" -Headers $hdr -UseBasicParsing).Content.Trim() -eq '[]') "toza baza: $yol bo'sh"
        }
        $nas = Invoke-RestMethod "$api/nasiyalar" -Headers $hdr
        Check ((@($nas.royxat).Count -eq 0) -and ($nas.xulosa.faolQarz -eq 0)) "toza baza: /nasiyalar bo'sh"
        Check ((Invoke-WebRequest "$api/smenalar/joriy" -Headers $hdr -UseBasicParsing).StatusCode -eq 204) "toza baza: ochiq smena yo'q (204)"
        $och = $null
        try { Invoke-RestMethod "$api/smenalar/och" -Method Post -Headers $hdr -ContentType 'application/json' -Body '{"qaytim":0,"terminal":0,"depozit":0}' | Out-Null } catch { $och = $_ }
        Check ($och -and ([int]$och.Exception.Response.StatusCode -eq 400) -and ($och.ErrorDetails.Message -match 'kamida bitta aparat')) "aparatsiz smena ochilmaydi: 400 'kamida bitta aparat kerak'"
        $doc = $null; try { $doc = Invoke-WebRequest "http://127.0.0.1:$p1/scalar/v1" -UseBasicParsing } catch { }
        Check ($null -ne $doc) "Scalar hujjat sahifasi ochiladi"
    }
    Stop-Api
    Check (Test-Path "$data1\fuelcontrol.db") "baza fayli ma'lumotlar papkasida yaratildi"
    $logFile = Get-ChildItem "$data1\logs\fuelcontrol-*.log" -ErrorAction SilentlyContinue | Select-Object -First 1
    Check ($null -ne $logFile) "jurnal fayli: $($logFile.Name)"
    if ($logFile) {
        $lt = Get-Content $logFile.FullName -Raw
        Check ($lt -match ('FuelControl API tayyor\. Versiya ' + [regex]::Escape($Version) + ', muhit Production')) "jurnalda 'tayyor', versiya $Version, muhit Production"
        Check ($lt -match [regex]::Escape("$app1\Server")) "kontent ildizi o'rnatilgan Server papkasi"
        Check ($lt -notmatch [regex]::Escape($pwd1) -and $lt -notmatch [regex]::Escape($key1)) "jurnalda parol ham, JWT kaliti ham yo'q"
    }

    "== 4. Yangilash: qayta o'rnatish (port $p2, parol berilmaydi), Local.json'ga qo'lda CORS qo'shilgan =="
    $origin = 'https://web-sinov.example'
    Set-Content -LiteralPath "$app1\Server\appsettings.Local.json" -Encoding utf8NoBOM -Value "{ `"Cors`": { `"Manbalar`": [ `"$origin`" ] } }"
    $code = Run-Setup @("/DIR=$app1", "/DATADIR=$data1", "/PORT=$p2", '/COMPONENTS=server,desktop', '/TASKS=') 'install2'
    Check ($code -eq 0) "yangilash chiqish kodi 0 (kod $code)"
    $cfg2 = Json "$app1\Server\appsettings.Production.json"
    Check ($cfg2.Jwt.Kalit -ceq $key1) "JWT kaliti ESKISI saqlandi"
    Check ($null -eq $cfg2.Seed) "AdminParol konfiguratsiyada yo'q (baza bor - parol so'ralmaydi)"
    Check ($cfg2.Urls -eq "http://0.0.0.0:$p2") "port yangilandi: $($cfg2.Urls)"
    Check ((Get-Content "$app1\Server\appsettings.Local.json" -Raw) -match [regex]::Escape($origin)) "Local.json (qo'lda tahrirlangan) saqlandi"
    Check ((Json "$app1\Desktop\sozlama.json").ServerManzili -eq "http://localhost:$p2") "desktop sozlama.json yangi portga"
    $up = Start-Api $app1 $p2
    Check $up "yangilangan API ishga tushdi"
    if ($up) {
        $r = Login $p2 $pwd1
        Check ($null -ne $r -and $r.foydalanuvchi.login -eq 'admin') "eski baza: admin eski parol bilan kiradi (ma'lumot saqlandi)"
        $req = [Net.HttpWebRequest]::Create("http://127.0.0.1:$p2/openapi/v1.json"); $req.Headers.Add('Origin', $origin)
        $resp = $req.GetResponse(); $acao = $resp.Headers['Access-Control-Allow-Origin']; $resp.Close()
        Check ($acao -eq $origin) "Local.json dagi CORS manbasi ishlaydi (Access-Control-Allow-Origin: $acao)"
    }
    Stop-Api

    "== 5. Faqat desktop: server manzili oxirida '/' bilan =="
    $app2 = "$T\app2"; $data2 = "$T\data2"
    $code = Run-Setup @("/DIR=$app2", "/DATADIR=$data2", "/SERVERURL=http://127.0.0.1:$p2/", '/COMPONENTS=desktop', '/TASKS=') 'install3'
    Check ($code -eq 0) "desktop-only chiqish kodi 0 (kod $code)"
    Check ((Test-Path "$app2\Desktop\FuelControl.exe") -and -not (Test-Path "$app2\Server")) "faqat Desktop o'rnatildi"
    Check ((Json "$app2\Desktop\sozlama.json").ServerManzili -eq "http://127.0.0.1:$p2") "sozlama.json: oxirgi / olib tashlandi"
    Check (-not (Test-Path $data2)) "ma'lumot papkasi yaratilmadi (server yo'q)"

    '== 5b. Mijozdagi jim buyruq shakli (qo''shtirnoqli /COMPONENTS va /TASKS; server bilan /SERVERURL e''tiborga olinmaydi) =='
    $app5 = "$T\app5"; $data5 = "$T\data5"
    $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FuelControl.lnk'
    $hadLnk = Test-Path -LiteralPath $lnk      # foydalanuvchining oldindan bor yorlig'ini buzmaslik uchun
    $tasks = if ($hadLnk) { '/TASKS=' } else { '/TASKS="desktopicon"' }
    $code = Run-Setup @("/DIR=$app5", "/DATADIR=$data5", '/COMPONENTS="server,desktop"', $tasks, "/PORT=$p1", '/ADMINPAROL=KuchliParol1', '/SERVERURL=http://boshqa-kompyuter:9999') 'install6'
    Check ($code -eq 0) "mijoz buyrug'i: chiqish kodi 0 (kod $code)"
    Check (@(Get-Content "$T\install6.log" | Where-Object { $_ -like '*/COMPONENTS="server,desktop"*' }).Count -gt 0) "buyruq satrida /COMPONENTS=""server,desktop"" qo'shtirnoqli keldi"
    Check ((Test-Path "$app5\Server\FuelControl.Api.exe") -and (Test-Path "$app5\Desktop\FuelControl.exe") -and -not (Test-Path "$app5\Tunnel")) "server + desktop o'rnatildi, Tunnel yo'q"
    Check ((Json "$app5\Desktop\sozlama.json").ServerManzili -eq "http://localhost:$p1") "server tanlanganda /SERVERURL e'tiborga olinmadi: sozlama.json = http://localhost:$p1"
    Check ((Json "$app5\Server\appsettings.Production.json").Urls -eq "http://0.0.0.0:$p1") "port /PORT bo'yicha: http://0.0.0.0:$p1"
    if ($hadLnk) { '  (ish stolida FuelControl.lnk oldindan bor - desktopicon sinovi o''tkazib yuborildi)' }
    else { Check (Test-Path -LiteralPath $lnk) "desktopicon vazifasi: ish stolida FuelControl.lnk yaratildi" }
    Uninstall-One $app5
    if (-not $hadLnk) { Check (-not (Test-Path -LiteralPath $lnk)) "o'chirishda ish stoli yorlig'i ham o'chdi" }

    $app3 = "$T\app3"; $app4 = "$T\app4"
    if ($SkipTunnel) { "== 6. Tunnel komponenti: o'tkazib yuborildi (-SkipTunnel) ==" }
    else {
        "== 6. Tunnel komponenti: token bilan fayl o'rnatiladi, tokensiz o'rnatilmaydi =="
        $token = 'eyJhIjoiMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAiLCJ0IjoiMTIzNDU2NzgtMTIzNC0xMjM0LTEyMzQtMTIzNDU2Nzg5MDEyIn0='
        $code = Run-Setup @("/DIR=$app3", "/DATADIR=$T\data3", "/PORT=$p3", '/ADMINPAROL=KuchliParol1', '/COMPONENTS=server,tunnel', "/TUNNELTOKEN=$token") 'install4'
        Check (($code -eq 0) -and (Test-Path "$app3\Tunnel\cloudflared.exe")) "token bilan: cloudflared.exe o'rnatildi (kod $code)"
        $code = Run-Setup @("/DIR=$app4", "/DATADIR=$T\data4", "/PORT=$p4", '/ADMINPAROL=KuchliParol1', '/COMPONENTS=server,tunnel') 'install5'
        Check (($code -eq 0) -and -not (Test-Path "$app4\Tunnel")) "tokensiz: Tunnel o'tkazib yuborildi (kod $code)"
        # Inno buyruq satrini jurnalning boshiga o'zi yozadi (docs\ornatish.md, 13-bo'lim); o'rnatuvchi kodi tokenni yozmasligi kerak.
        $leak = @(Get-Content "$T\install4.log" | Where-Object { $_ -match [regex]::Escape($token) -and $_ -notmatch 'Setup command line:' }).Count
        Check ($leak -eq 0) "tunnel tokeni jurnalda faqat Inno'ning 'Setup command line' sarlavhasida (o'rnatuvchi kodi yozmaydi)"
    }

    "== 7. O'chirish: dastur fayllari ketadi, ma'lumotlar qoladi =="
    Uninstall-All
    Check (-not (Test-Path "$app1\Server\appsettings.Production.json") -and -not (Test-Path "$app1\Server\appsettings.Local.json") -and -not (Test-Path "$app1\Desktop\sozlama.json")) "o'rnatuvchi yozgan sozlama fayllari o'chdi"
    Check (-not (Test-Path "$app1\Server\FuelControl.Api.exe") -and -not (Test-Path "$app1\Desktop\FuelControl.exe")) "dastur fayllari o'chdi"
    Check (-not (Test-Path "$app1\unins000.exe")) "uninstaller o'zini o'chirdi"
    Check ((Test-Path "$data1\fuelcontrol.db") -and (Test-Path "$data1\logs") -and (Test-Path "$data1\zaxira")) "ma'lumotlar SAQLANDI (baza, jurnal, zaxira)"
    Check (-not (Test-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$appId}_is1")) "ro'yxatdan o'tish yozuvi (HKCU) o'chdi"

    if ($OldSetup) {
        "== 8. Mijoz rejasi: eski versiya ($(Split-Path $OldSetup -Leaf)) o'rnatiladi va unda ma'lumot yaratiladi -> o'chiriladi -> baza qoladi -> yangisi /ADMINPAROL'siz o'rnatiladi =="
        if (-not (Test-Path -LiteralPath $OldSetup)) { throw "OldSetup topilmadi: $OldSetup" }
        $app6 = "$T\app6"; $data6 = "$T\data6"; $eskiParol = 'EskiParol123'; $yangiParol = 'YangiParol456'; $opLogin = 'sinov-op'; $opParol = '2468'; $boshLogin = 'sinov-bosh'; $boshParol = '1357'
        $api6 = "http://127.0.0.1:$p4"
        $baza = @("/DIR=$app6", "/DATADIR=$data6", "/PORT=$p4", '/COMPONENTS="server,desktop"')     # mijoz buyrug'i shakli
        $tasks8 = if ($hadLnk) { '/TASKS=' } else { '/TASKS="desktopicon"' }
        $menyu = [Environment]::GetFolderPath('Programs')
        $code = Run-Setup ($baza + $tasks8 + "/ADMINPAROL=$eskiParol") 'old-install' $OldSetup
        Check ($code -eq 0) "eski versiya o'rnatildi (kod $code)"
        $up = Start-Api $app6 $p4
        Check $up "eski versiya API'si ishga tushdi (baza yaratildi)"
        $r = Login $p4 $eskiParol
        Check ($null -ne $r) "eski versiya: admin kirdi"
        if ($r) {
            # Mijozdagi holat (jonli baza namunasi, qiymatlar mijozniki kabi): 3 foydalanuvchi, 2 yoqilg'i va aparat (4-aparat - DIZEL ECO; 5-aparat -
            # pulti 10 000 da 0 ga qaytadi), bak tuzatish va kirimi, operator yopgan smena (nasiya, xarajat, kamomat), avans va yana bitta OCHIQ smena:
            # unda narx o'zgarishi segmenti 10 baravar katta (noto'g'ri) ko'rsatkich bilan yozilgan (2026-10-17). Hammasi eski versiyada yaratiladi.
            $yoq = Post-Json $p4 $r.token '/yoqilgilar' @{ nomi = 'Sinov Dizel ECO'; narx = 15500; rang = '#2563EB' }
            $yoq2 = Post-Json $p4 $r.token '/yoqilgilar' @{ nomi = 'Sinov AI-95'; narx = 14200; rang = '#16A34A' }
            $apr = Post-Json $p4 $r.token '/aparatlar' @{ raqam = 4; yoqilgiTuriId = $yoq.id; boshlangichTotalLitr = [decimal]109697.58; boshlangichBakQoldiq = [decimal]5117.83 }
            $apr2 = Post-Json $p4 $r.token '/aparatlar' @{ raqam = 5; yoqilgiTuriId = $yoq2.id; boshlangichTotalLitr = [decimal]19880.52; boshlangichBakQoldiq = [decimal]8000 }
            $opU = Post-Json $p4 $r.token '/foydalanuvchilar' @{ toliqIsm = 'Sinov Operator'; login = $opLogin; rol = 'Operator'; oylikMaosh = 4000000; parolYokiPin = $opParol }
            [void](Post-Json $p4 $r.token '/foydalanuvchilar' @{ toliqIsm = 'Sinov Boshliq'; login = $boshLogin; rol = 'Boshliq'; oylikMaosh = 0; parolYokiPin = $boshParol })
            # Operatorning oylik maoshi API ishga tushganda yoziladi: uni eski versiya o'zi yozsin (yangilashdan keyin harakatlar soni o'zgarmasligi uchun).
            Stop-Api
            Check (Start-Api $app6 $p4) "eski API qayta ishga tushdi (operatorning oylik maosh harakati eski versiyada yoziladi)"
            $r = Login $p4 $eskiParol
            $opT = Login $p4 $opParol $opLogin
            Check (($null -ne $r) -and ($null -ne $opT)) "eski versiya: admin va operator ($opLogin) kirdi"
            $apr2 = Put-Json $p4 $r.token "/aparatlar/$($apr2.id)" @{ raqam = 5; yoqilgiTuriId = $yoq2.id; bakQoldiq = [decimal]8200; sabab = 'sinov: qoldiq sanab tuzatildi'; krugHajmi = [decimal]10000 }
            $apr2 = Post-Json $p4 $r.token "/aparatlar/$($apr2.id)/kirim" @{ litr = [decimal]2000; vaqt = $null; hujjat = 'YX-1176' }
            Check (($apr2.bakQoldiq -eq 10200) -and ([decimal]$apr2.totalLitr -eq [decimal]19880.52) -and ([decimal]$apr2.krugHajmi -eq 10000)) "eski versiyada: 5-aparatga krug 10 000, bak qoldig'i tuzatildi (8 200), kirim +2 000 L (10 200)"
            # Birinchi smenani operatorning o'zi ochadi va yopadi: kamomat shu operator hisobiga tushadi.
            $ot = $opT.token
            $sm = Post-Json $p4 $ot '/smenalar/och' @{ qaytim = 100000; terminal = 50000; depozit = 200000 }
            $muddat = [DateTime]::UtcNow.AddHours(5).AddDays(10).ToString('yyyy-MM-dd')
            $ns1 = Post-Json $p4 $ot '/nasiyalar' @{ mijozIsmi = 'Sinov Mijoz'; telefon = '90 123 45 67'; mashinaRaqami = '01 A 777 BC'; summa = 200000; muddat = $muddat; izoh = $null }
            [void](Post-Json $p4 $ot '/xarajatlar' @{ summa = 15000; sabab = 'Sinov xarajat'; manba = 'Kassa' })
            $kors = @(Get-Json $p4 $ot '/aparatlar' | ForEach-Object { @{ aparatId = $_.id; qiymat = [decimal]$_.totalLitr + $(if ($_.id -eq $apr.id) { [decimal]100 } elseif ($_.id -eq $apr2.id) { [decimal]50 } else { [decimal]0 }) } })
            $yop = Post-Json $p4 $ot "/smenalar/$($sm.id)/yop" @{ korsatkichlar = $kors; terminal = 50000; depozit = 200000; sanalganNaqd = 2140000; izoh = $null }
            Check (($yop.savdo -eq 2260000) -and ($yop.farq -eq -5000)) "eski versiyada operator yopgan smena: savdo 2 260 000 (4-aparat 100 L x 15 500 + 5-aparat 50 L x 14 200), kamomat -5 000 (smena $($sm.id))"
            [void](Post-Json $p4 $r.token "/operatorlar/$($opU.id)/harakat" @{ turi = 'Avans'; summa = 300000; izoh = 'sinov avans' })
            # Ikkinchi smena OCHIQ qoldiriladi: mijoz yangilashni smena davom etayotganda o'rnatadi.
            $sm2 = Post-Json $p4 $r.token '/smenalar/och' @{ qaytim = 80000; terminal = 70000; depozit = 300000 }
            Check (($null -ne $sm2.id) -and ($sm2.id -ne $sm.id) -and ($null -eq $sm2.tugadi)) "eski versiyada ikkinchi smena ochildi va yangilash paytida ochiq turadi (id $($sm2.id))"
            $ns2 = Post-Json $p4 $r.token '/nasiyalar' @{ mijozIsmi = 'Sinov Mijoz Ikki'; telefon = '91 555 44 33'; mashinaRaqami = ''; summa = 40000; muddat = $muddat; izoh = $null }
            $qay = Post-Json $p4 $r.token "/nasiyalar/$($ns1.id)/qaytish" @{ summa = 30000; usul = 'Naqd'; smenaHisobiga = $true; izoh = $null }
            Check (($ns2.summa -eq 40000) -and ($qay.qoldiq -eq 170000)) "eski versiyada ochiq smenada nasiya (40 000) va qarz qaytishi (30 000) yozildi"
            # Narx 15 500 -> 16 000. 4-aparat ko'rsatkichi narx o'zgarguncha savdo bo'lmagani uchun 109 797.58 bo'lishi kerak edi, 1 099 797.58 yozildi (990 000 L).
            $narx = Put-Json $p4 $r.token "/yoqilgilar/$($yoq.id)" @{ nomi = 'Sinov Dizel ECO'; narx = 16000; rang = '#2563EB'; korsatkichlar = @(@{ aparatId = $apr.id; qiymat = [decimal]109847.58 }) }
            $sg = @((Get-Json $p4 $r.token '/smenalar/joriy').korsatkichlar)
            Check (($narx.narx -eq 16000) -and ($sg.Count -eq 1) -and ($sg[0].aparatRaqam -eq 4) -and ([decimal]$sg[0].boshi -eq [decimal]109797.58) -and ([decimal]$sg[0].oxiri -eq [decimal]109847.58) -and ($sg[0].narx -eq 15500) -and ([decimal]$sg[0].litr -eq [decimal]50) -and ($sg[0].summa -eq 775000) -and ($sg[0].narxOzgarishida -eq $true)) "eski versiyada ochiq smenada narx o'zgarishi segmenti yozildi: 109 797.58 -> 109 847.58 (50 L x 15 500)"
            Start-Sleep -Seconds 2     # qo'lda nusxa birinchi smena nusxasidan KEYINGI soniyada olinsin (nom soniyagacha aniq: tartib va 'oxirgi nusxa' belgisi aniq bo'ladi)
            $zaxOld = Post-Json $p4 $r.token '/zaxira' @{}
            Check (($zaxOld.hajmBayt -gt 0) -and ($zaxOld.sabab -eq 'qolda') -and (Test-Path -LiteralPath (Join-Path "$data6\zaxira" $zaxOld.faylNomi))) "eski versiyada qo'lda zaxira nusxa olindi: $($zaxOld.faylNomi)"
            $tgTok = '123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw'; $tgChat = '-1001234567890'; $uch = [string][char] 0x2026
            $tgPut = Put-Json $p4 $r.token '/zaxira/telegram' @{ token = $tgTok; chatId = $tgChat }
            $tgSn = Post-Json $p4 $r.token '/zaxira/telegram/sinov' @{}
            Check (($tgPut.telegram.tokenMaska -eq ($uch + 'Dsaw')) -and ($tgPut.telegram.faol -eq $true) -and ($tgSn.telegram.oxirgiTuri -eq 'sinov') -and ($tgSn.telegram.oxirgiMuvaffaqiyat -eq $false)) "eski versiyada Telegram sozlandi (token faqat maska) va sinov xabari yuborildi, holat yozildi: [$($tgSn.telegram.oxirgiXabar)]"
        }
        Stop-Api
        Uninstall-One $app6
        Check (-not (Test-Path "$app6\unins000.exe")) "eski versiya o'chirildi"
        Check (Test-Path "$data6\fuelcontrol.db") "o'chirishdan keyin baza SAQLANDI"
        # Eski versiya yaratgan haqiqiy baza (API to'xtagan, baza yopiq). Mijozda JONLI ma'lumot bor, shuning uchun yangilashdan keyin hamma narsa
        # joyida ekani jadvallarning barmoq izi bilan tekshiriladi (qator soni va har bir qiymat); yangi reliz bitta nullable ustun qo'shadi.
        $dbFayl = "$data6\fuelcontrol.db"
        $repoMig = @(Get-ChildItem -LiteralPath (Join-Path $Repo 'src\backend\FuelControl.Api\Data\Migrations') -Filter '*.cs' |
            ForEach-Object { [regex]::Match($_.Name, '^(\d{14}_\w+)\.cs$') } | Where-Object { $_.Success } | ForEach-Object { $_.Groups[1].Value })
        $eskiMig = Sql-Qatorlar $dbFayl 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId'
        $yangiMigId = @($repoMig | Where-Object { $eskiMig -notcontains $_ })
        Check (($eskiMig -join ';') -eq ($repoMig -join ';')) "eski baza: $($eskiMig.Count) ta migratsiya, repodagi bilan bir xil (oxirgisi $($eskiMig[-1])); bu reliz migratsiya qo'shmaydi"
        $ustunEski = Baza-Ustunlari $dbFayl
        $izEski = Baza-Izi $dbFayl
        Check ($ustunEski.ContainsKey('ZaxiraSozlamalari')) "eski baza: ZaxiraSozlamalari jadvali bor (0.1.5 sxemasi)"
        $krugEski = Sql-Qatorlar $dbFayl 'SELECT Raqam, KrugHajmi FROM Aparatlar ORDER BY Raqam'
        Check (($krugEski[0] -eq '4|') -and ($krugEski[1] -match '^5\|10000')) "eski baza: 5-aparatda krug 10 000, 4-aparatda yo'q ($($krugEski -join '; '))"
        $zaxEski = @{}; foreach ($zf in Get-ChildItem "$data6\zaxira" -Filter 'fuelcontrol-*.db') { $zaxEski[$zf.Name] = (Get-FileHash $zf.FullName -Algorithm SHA256).Hash }
        Check (($zaxEski.Count -eq 2) -and (@($zaxEski.Keys | Where-Object { $_ -match '^fuelcontrol-\d{8}-\d{6}-smena\d+\.db$' }).Count -eq 1) -and (@($zaxEski.Keys | Where-Object { $_ -match '^fuelcontrol-\d{8}-\d{6}-qolda\.db$' }).Count -eq 1)) "eski versiya (0.1.5) zaxira nusxalari: 2 ta - birinchi smena yopilganda olingan (-smena) va qo'lda olingan (-qolda); kunlik avtomatik nusxa yo'q"
        $kutilgan = [ordered] @{ Foydalanuvchilar = 3; Yoqilgilar = 2; NarxTarixlari = 1; Aparatlar = 2; BakKirimlari = 1; BakTuzatishlari = 1; Smenalar = 2; SmenaKorsatkichlari = 3; Nasiyalar = 2; NasiyaQaytishlari = 1; Xarajatlar = 1; Harakatlar = 3; ZaxiraSozlamalari = 1 }
        $tavsif = ($kutilgan.Keys | ForEach-Object { '{0} {1}' -f $_, $kutilgan[$_] }) -join ', '
        $nomos = @($kutilgan.Keys | Where-Object { (Iz-Soni $izEski $_) -ne $kutilgan[$_] })
        Check ($nomos.Count -eq 0) "eski baza (jonli ma'lumot namunasi), qatorlar: $tavsif$(if ($nomos.Count) { ' - mos kelmadi: ' + ($nomos -join ', ') })"
        $auditEski = Iz-Soni $izEski 'Audit'
        $harTur = Sql-Qatorlar $dbFayl 'SELECT Turi, COUNT(*) FROM Harakatlar GROUP BY Turi ORDER BY Turi'
        Check (($harTur -join ';') -eq 'Avans|1;Kamomat|1;Maosh|1') "eski baza: harakatlar - avans, kamomat va oylik maosh (har biridan 1 ta); audit yozuvlari $auditEski ta"
        $audTurEski = Sql-Qatorlar $dbFayl 'SELECT Tur, COUNT(*) FROM Audit GROUP BY Tur ORDER BY Tur'
        $aprEski = Sql-Qatorlar $dbFayl 'SELECT Raqam, TotalLitr, BakQoldiq FROM Aparatlar ORDER BY Raqam'
        Check ($aprEski.Count -eq 2) "eski baza: aparatlarning total va bak qoldig'i ($($aprEski -join '; '))"
        $smena1Eski = Sql-Qatorlar $dbFayl "SELECT * FROM Smenalar WHERE Id = $($sm.id)"
        $seg1Eski = Sql-Qatorlar $dbFayl "SELECT * FROM SmenaKorsatkichlari WHERE SmenaId = $($sm.id) ORDER BY Id"
        $smena2Eski = Sql-Qatorlar $dbFayl "SELECT * FROM Smenalar WHERE Id = $($sm2.id)"
        $seg2Eski = Sql-Qatorlar $dbFayl "SELECT * FROM SmenaKorsatkichlari WHERE SmenaId = $($sm2.id) ORDER BY Id"
        Check (($smena2Eski.Count -eq 1) -and ($seg2Eski.Count -eq 1) -and ($seg2Eski[0] -match "^\d+\|$($sm2.id)\|$($apr.id)\|1\|")) "eski baza: ochiq smena (id $($sm2.id)) va uning narx o'zgarishi segmenti (4-aparat) bor"
        Check (-not (Test-Path -LiteralPath (Join-Path $menyu 'FuelControl.lnk'))) "o'chirishda Start menyu yorlig'i ketdi"
        if (-not $hadLnk) { Check (-not (Test-Path -LiteralPath $lnk)) "o'chirishda ish stoli yorlig'i ketdi" }

        '-- 8a. Mijoz buyrug''i: yangi versiya /ADMINPAROL''siz, baza joyida --'
        $code = Run-Setup ($baza + $tasks8) 'new-over-data'
        Check ($code -eq 0) "yangi versiya o'rnatildi (kod $code)"
        Check ($null -eq (Json "$app6\Server\appsettings.Production.json").Seed) "AdminParol konfiguratsiyada yo'q (baza bor)"
        Check ((Json "$app6\Server\appsettings.Production.json").Urls -eq "http://0.0.0.0:$p4") "port /PORT bo'yicha: http://0.0.0.0:$p4"
        Check ((Json "$app6\Desktop\sozlama.json").ServerManzili -eq "http://localhost:$p4") "desktop sozlama.json = http://localhost:$p4"
        Check ((Test-Path -LiteralPath (Join-Path $menyu 'FuelControl.lnk')) -and (Test-Path -LiteralPath (Join-Path $menyu 'FuelControl Web.lnk'))) "Start menyu yorliqlari (FuelControl, FuelControl Web) qayta yaratildi"
        if (-not $hadLnk) { Check (Test-Path -LiteralPath $lnk) "ish stoli yorlig'i FuelControl.lnk qayta yaratildi" }
        Check ((Get-Item "$app6\Server\FuelControl.Api.exe").VersionInfo.ProductVersion -eq $Version) "o'rnatilgan API versiyasi $Version"
        # Jonli bazani yangilash: o'rnatuvchi ham, yangi API'ning birinchi ishga tushishi ham mavjud ma'lumotga tegmasligi shart. Yagona o'zgarish -
        # Aparatlar.KrugHajmi ustuni (hammasi null) va migratsiyalar tarixidagi bitta yangi yozuv. Taqqoslash eski sxemaning ustunlari bo'yicha.
        $izYangi0 = Baza-Izi $dbFayl $ustunEski
        $farq0 = Iz-Farqi $izEski $izYangi0
        Check ($farq0.Count -eq 0) "yangi versiya o'rnatilgach baza o'zgarmadi: $($izEski.Count) ta jadvalning hamma qatori va qiymati bir xil$(if ($farq0.Count) { ' - farq: ' + ($farq0 -join ', ') })"
        $up = Start-Api $app6 $p4
        Check $up "yangi API eski bazada ishga tushdi (migratsiya qo'llandi)"
        Stop-Api
        $izYangi1 = Baza-Izi $dbFayl $ustunEski
        $farq1 = Iz-Farqi $izEski $izYangi1
        Check ($farq1.Count -eq 0) "yangi API birinchi marta ishga tushgach HECH BIR jadval o'zgarmadi (migratsiya yo'q; foydalanuvchilar, aparatlar, bak, segmentlar, nasiya, harakat, audit, Telegram sozlamasi va migratsiyalar tarixi)$(if ($farq1.Count) { ' - farq: ' + ($farq1 -join ', ') })"
        $nomos = @($kutilgan.Keys | Where-Object { (Iz-Soni $izYangi1 $_) -ne $kutilgan[$_] })
        Check (($nomos.Count -eq 0) -and ((Iz-Soni $izYangi1 'Audit') -eq $auditEski)) "yangilashdan keyin qatorlar soni joyida: $tavsif, audit $auditEski"
        $mig1 = Sql-Qatorlar $dbFayl 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId'
        Check (($mig1 -join ';') -eq ($eskiMig -join ';')) "migratsiyalar tarixi o'zgarmadi: $($mig1.Count) ta yozuv (bu reliz migratsiya qo'shmaydi)"
        Check ((Sql-Qatorlar $dbFayl 'SELECT COUNT(*) FROM ZaxiraSozlamalari')[0] -eq '1') "ZaxiraSozlamalari: eski qator (Telegram sozlamasi) joyida"
        $krugYangi = Sql-Qatorlar $dbFayl 'SELECT Raqam, KrugHajmi FROM Aparatlar ORDER BY Raqam'
        Check (($krugYangi[0] -eq '4|') -and ($krugYangi[1] -match '^5\|10000')) "aparatlar krug hajmi o'zgarmadi: 4-aparatda yo'q, 5-aparatda 10 000"
        $zaxYangi = @{}; foreach ($zf in Get-ChildItem "$data6\zaxira" -Filter 'fuelcontrol-*.db') { $zaxYangi[$zf.Name] = (Get-FileHash $zf.FullName -Algorithm SHA256).Hash }
        Check (((($zaxYangi.Keys | Sort-Object) -join ';') -eq (($zaxEski.Keys | Sort-Object) -join ';')) -and (@($zaxEski.Keys | Where-Object { $zaxYangi[$_] -ne $zaxEski[$_] }).Count -eq 0)) "eski zaxira nusxalari ($($zaxEski.Count) ta) bayt-bayt joyida; yangi API ishga tushganda kunlik nusxa olinmadi"
        $aprYangi = Sql-Qatorlar $dbFayl 'SELECT Raqam, TotalLitr, BakQoldiq FROM Aparatlar ORDER BY Raqam'
        Check (($aprYangi -join ';') -eq ($aprEski -join ';')) "aparatlarning total va bak qoldig'i santimigacha o'zgarmadi ($($aprYangi -join '; '))"
        $sm2Yangi = Sql-Qatorlar $dbFayl "SELECT * FROM Smenalar WHERE Id = $($sm2.id)"
        $sg2Yangi = Sql-Qatorlar $dbFayl "SELECT * FROM SmenaKorsatkichlari WHERE SmenaId = $($sm2.id) ORDER BY Id"
        Check ((($sm2Yangi -join "`n") -eq ($smena2Eski -join "`n")) -and (($sg2Yangi -join "`n") -eq ($seg2Eski -join "`n"))) "ochiq smena (id $($sm2.id)) va uning narx o'zgarishi segmenti bazada aynan o'sha"
        $up = Start-Api $app6 $p4
        Check $up "yangi API qayta ishga tushdi"
        if ($up) {
            $r = Login $p4 $eskiParol
            Check ($null -ne $r) "eski admin paroli ishlaydi"
            $op = Login $p4 $opParol $opLogin
            Check ($null -ne $op) "eski operator ($opLogin) o'z paroli bilan kiradi"
            $bosh = Login $p4 $boshParol $boshLogin
            Check ($null -ne $bosh) "eski boshliq ($boshLogin) o'z paroli bilan kiradi"
            if ($r -and $op -and $bosh) {
                $tk = $r.token
                Check ((Count-Json $p4 $tk '/foydalanuvchilar') -eq 3) "foydalanuvchilar joyida: admin, $opLogin, $boshLogin"
                $y = @(Get-Json $p4 $tk '/yoqilgilar')
                $y1 = @($y | Where-Object { $_.nomi -eq 'Sinov Dizel ECO' }); $y2 = @($y | Where-Object { $_.nomi -eq 'Sinov AI-95' })
                Check (($y.Count -eq 2) -and ($y1.Count -eq 1) -and ($y1[0].narx -eq 16000) -and ($y2.Count -eq 1) -and ($y2[0].narx -eq 14200)) "yoqilg'ilar joyida: Sinov Dizel ECO (16 000 - ochiq smenada o'zgargan narx), Sinov AI-95 14 200"
                $a = @(Get-Json $p4 $tk '/aparatlar')
                Check (($a.Count -eq 2) -and ($a[0].raqam -eq 4) -and ([decimal]$a[0].totalLitr -eq [decimal]109797.58) -and ([decimal]$a[0].bakQoldiq -eq [decimal]5017.83) -and ($a[1].raqam -eq 5) -and ([decimal]$a[1].totalLitr -eq [decimal]19930.52) -and ([decimal]$a[1].bakQoldiq -eq [decimal]10150) -and ($null -eq $a[0].krugHajmi) -and ([decimal]$a[1].krugHajmi -eq 10000)) "aparatlar joyida (santimigacha): 4-aparat total 109797.58 / bak 5017.83, 5-aparat total 19930.52 / bak 10150.00; krug: 4-aparatda yo'q, 5-aparatda 10 000"
                $bk = @(Get-Json $p4 $tk '/bak-kirimlar')
                Check (($bk.Count -eq 1) -and ($bk[0].aparatRaqam -eq 5) -and ([decimal]$bk[0].litr -eq [decimal]2000) -and ([decimal]$bk[0].qoldiqOldin -eq [decimal]8200) -and ([decimal]$bk[0].qoldiqKeyin -eq [decimal]10200) -and ($bk[0].hujjat -eq 'YX-1176')) "bak kirimi joyida: 5-aparat +2 000 L (8 200 -> 10 200), yuk xati YX-1176"
                $s = @(Get-Json $p4 $tk '/smenalar')
                $s1 = @($s | Where-Object { $_.id -eq $sm.id })
                Check (($s.Count -eq 2) -and ($s1.Count -eq 1) -and ($s1[0].savdo -eq 2260000) -and ($s1[0].farq -eq -5000) -and ($null -ne $s1[0].tugadi)) "yopilgan smena joyida: savdo 2 260 000, farq -5 000 (jami 2 ta smena: yopilgan va ochiq)"
                $d1 = Get-Json $p4 $tk "/smenalar/$($sm.id)"
                Check ((@($d1.korsatkichlar).Count -eq 2) -and (@($d1.nasiyalar).Count -eq 1) -and ($d1.nasiyalar[0].summa -eq 200000) -and (@($d1.xarajatlar).Count -eq 1) -and ($d1.xarajatlar[0].summa -eq 15000)) "yopilgan smena tafsiloti ochiladi: 2 ta segment (4- va 5-aparat), nasiya 200 000, xarajat 15 000"
                $n = Get-Json $p4 $tk '/nasiyalar'
                $n1 = @($n.royxat | Where-Object { $_.telefon -eq '+998 90 123 45 67' })
                $n2 = @($n.royxat | Where-Object { $_.telefon -eq '+998 91 555 44 33' })
                Check ((@($n.royxat).Count -eq 2) -and ($n1.Count -eq 1) -and ($n1[0].qaytgan -eq 30000) -and ($n1[0].qoldiq -eq 170000) -and ($n2.Count -eq 1) -and ($n2[0].qoldiq -eq 40000)) "nasiyalar joyida: +998 90 123 45 67 (30 000 qaytgan, qoldiq 170 000) va +998 91 555 44 33 (qoldiq 40 000)"
                Check ((@($n.royxat | Where-Object { $_.eskiQarz -eq $false }).Count -eq 2) -and ((Count-Json $p4 $tk '/xarajatlar') -eq 1)) "nasiyalar eskiQarz = false o'qiladi; xarajatlar joyida (1 ta)"
                $hs = Get-Json $p4 $tk "/operatorlar/$($opU.id)/hisob"
                $hTur = (@($hs.harakatlar | ForEach-Object { '{0}:{1}' -f $_.turi, $_.summa }) | Where-Object { $_ -notlike 'Maosh:*' } | Sort-Object) -join ','
                Check ((@($hs.harakatlar).Count -eq 3) -and (@($hs.harakatlar | Where-Object { $_.turi -eq 'Maosh' }).Count -eq 1) -and ($hTur -eq 'Avans:-300000,Kamomat:-5000')) "operator hisobi joyida: 3 ta harakat - oylik maosh, avans -300 000, kamomat -5 000"
                Check ((Count-Json $p4 $tk '/audit') -eq $auditEski) "audit yozuvlari joyida: $auditEski ta (kirish audit yozmaydi)"
                $j = Get-Json $p4 $tk '/smenalar/joriy'
                Check (($j.smena.id -eq $sm2.id) -and ($null -eq $j.smena.tugadi) -and ($j.smena.ochishQaytim -eq 80000) -and ($j.smena.ochishTerminal -eq 70000) -and ($j.smena.ochishDepozit -eq 300000)) "ochiq smena joyida (eski versiyada ochilgan, id $($sm2.id)): ochilish qoldiqlari 80 000 / 70 000 / 300 000"
                Check (($j.smena.nasiyaJami -eq 40000) -and ($j.smena.qaytganNasiya -eq 30000) -and (@($j.nasiyalar).Count -eq 1) -and (@($j.qaytishlar).Count -eq 1)) "ochiq smena yig'indisi joyida: nasiya 40 000, qaytgan 30 000"
                $sg = @($j.korsatkichlar)
                Check (($sg.Count -eq 1) -and ($sg[0].aparatRaqam -eq 4) -and ([decimal]$sg[0].boshi -eq [decimal]109797.58) -and ([decimal]$sg[0].oxiri -eq [decimal]109847.58) -and ($sg[0].narx -eq 15500) -and ($sg[0].summa -eq 775000) -and ($sg[0].narxOzgarishida -eq $true)) "ochiq smenaning narx o'zgarishi segmenti joyida: 4-aparat 109 797.58 -> 109 847.58, narx 15 500"
                foreach ($yol in '/boshqaruv', '/hisobot', '/xarajatlar') {
                    $kod = try { (Invoke-WebRequest "$api6$yol" -Headers @{ Authorization = "Bearer $tk" } -UseBasicParsing).StatusCode } catch { 0 }
                    Check ($kod -eq 200) "GET $yol -> $kod"
                }

                # --- Yangilashdan keyin zaxira va Telegram sozlamasi joyida (0.1.5 da kiritilgan token DPAPI bilan shifrlangan holda bazada o'zgarmagan) ---
                $uch = [string][char] 0x2026
                $zaxPapka = "$data6\zaxira"
                $hz = Get-Json $p4 $tk '/zaxira/holat'
                Check (($hz.papka -eq $zaxPapka) -and ($hz.standartPapkaga -eq $false) -and ($hz.saqlash -eq 30) -and ((@($hz.nusxalar | ForEach-Object { $_.sabab }) -join ',') -eq 'qolda,smena') -and ($hz.nusxalar[0].nomi -eq $zaxOld.faylNomi) -and ($hz.nusxalar[1].smenaId -eq $sm.id)) "zaxira holati: eski (0.1.5) ikki nusxa ro'yxatda (qo'lda va 1-smena yopilgandagi), papka standart, 30 ta saqlanadi"
                Check (($hz.telegram.tokenMaska -eq ($uch + 'Dsaw')) -and ($hz.telegram.chatId -eq $tgChat) -and ($hz.telegram.yoqilgan -eq $true) -and ($hz.telegram.faol -eq $true) -and ($hz.telegram.oxirgiTuri -eq 'sinov') -and ($hz.telegram.oxirgiMuvaffaqiyat -eq $false) -and ($hz.telegram.oxirgiXabar -eq $tgSn.telegram.oxirgiXabar)) "Telegram sozlamasi yangilashdan keyin joyida: token (...Dsaw), chat ID, faol, oxirgi holat o'zgarmagan"
                $xom = (Invoke-WebRequest "$api6/zaxira/holat" -Headers @{ Authorization = "Bearer $tk" } -UseBasicParsing).Content
                Check (($xom -notmatch 'AAHdq') -and ($xom -notmatch '123456789:')) "GET /zaxira/holat javobida token yo'q (faqat maska)"
                
                # --- Spec 8.18: POST /auth/yangila o'rnatilgan API'da (faol foydalanuvchi sessiyasini uzaytirish) ---
                Start-Sleep -Seconds 1
                $yn = Post-Json $p4 $tk '/auth/yangila' @{}
                $qolgan = ([datetime]$yn.tokenMuddati).ToUniversalTime().Subtract([datetime]::UtcNow).TotalMinutes
                Check (($yn.token -ne $tk) -and ($yn.foydalanuvchi.login -eq 'admin') -and ($qolgan -gt 11.9 * 60) -and ($qolgan -lt 12.1 * 60)) "yangila: yangi token, foydalanuvchi admin, muddat 12 soat (qolgan $([math]::Round($qolgan / 60, 2)) soat)"
                Check (((Get-Json $p4 $yn.token '/me').login -eq 'admin') -and ((Get-Json $p4 $tk '/me').login -eq 'admin')) "yangi token ishlaydi va eski token ham muddati tugaguncha ishlaydi"
                $opY = Post-Json $p4 $op.token '/auth/yangila' @{}
                Check (($opY.foydalanuvchi.login -eq $opLogin) -and ($opY.foydalanuvchi.rol -eq 'Operator') -and ((@($opY.foydalanuvchi.ruxsatlar) | Sort-Object) -join ',') -eq ((@($op.foydalanuvchi.ruxsatlar) | Sort-Object) -join ',')) "yangila: operator ($opLogin) o'z roli va ruxsatlari bilan"
                $yX = Post-Xato $p4 'bu.token.emas' '/auth/yangila' @{}
                $yBos = try { (Invoke-WebRequest "$api6/auth/yangila" -Method Post -UseBasicParsing).StatusCode } catch { [int] $_.Exception.Response.StatusCode }
                Check (($yX.Kod -eq 401) -and ($yBos -eq 401)) "yangila: yaroqsiz token ($($yX.Kod)) va tokensiz ($yBos) -> 401"
                
                # Avtorizatsiya:Muddat (appsettings.Local.json): klientlar qisqa token bilan sinashi uchun; login va yangila bir xil muddat beradi.
                Stop-Api
                $localJson = "$app6\Server\appsettings.Local.json"
                $localAsl = if (Test-Path -LiteralPath $localJson) { Get-Content -LiteralPath $localJson -Raw } else { $null }
                @{ Avtorizatsiya = @{ Muddat = '00:03:00' } } | ConvertTo-Json | Set-Content -LiteralPath $localJson -Encoding utf8NoBOM
                $upM = Start-Api $app6 $p4
                Check $upM "API appsettings.Local.json (Avtorizatsiya:Muddat = 00:03:00) bilan ishga tushdi"
                if ($upM) {
                    $lq = Login $p4 $eskiParol
                    $yq = Post-Json $p4 $lq.token '/auth/yangila' @{}
                    $m1 = ([datetime]$lq.tokenMuddati).ToUniversalTime().Subtract([datetime]::UtcNow).TotalSeconds
                    $m2 = ([datetime]$yq.tokenMuddati).ToUniversalTime().Subtract([datetime]::UtcNow).TotalSeconds
                    Check (($m1 -gt 150) -and ($m1 -lt 185) -and ($m2 -gt 150) -and ($m2 -lt 185)) "sozlangan muddat: login va yangila ikkalasi ~3 daqiqa (login $([math]::Round($m1)) s, yangila $([math]::Round($m2)) s)"
                }
                Stop-Api
                if ($null -ne $localAsl) { Set-Content -LiteralPath $localJson -Value $localAsl -NoNewline -Encoding utf8NoBOM } else { Remove-Item -LiteralPath $localJson -Force }
                Check (Start-Api $app6 $p4) "API standart muddat bilan qayta ishga tushdi"
                $tk = (Login $p4 $eskiParol).token
                $op = Login $p4 $opParol $opLogin
                
                # Ochiq smena (eski versiyada ochilgan) yangilashdan keyin yopiladi: yopish javobidan keyin fonda zaxira nusxa olinadi va Telegram'ga yuborishga urinadi.
                $fayllar0 = @(Get-ChildItem $zaxPapka -Filter 'fuelcontrol-*.db').Count
                $a1 = @(Get-Json $p4 $tk '/aparatlar')
                $kors2 = @(@{ aparatId = $a1[0].id; qiymat = [decimal]109897.58 }, @{ aparatId = $a1[1].id; qiymat = [decimal]20030.52 })
                $yop2 = Post-Json $p4 $tk "/smenalar/$($sm2.id)/yop" @{ korsatkichlar = $kors2; terminal = 125000; depozit = 320000; sanalganNaqd = 2990000; izoh = $null; plastikSummalari = @(60000, 65000) }
                Check (($yop2.savdo -eq 2995000) -and ($yop2.kutilgan -eq 2990000) -and ($yop2.farq -eq 0) -and ($null -ne $yop2.tugadi)) "smena yopildi: 50 L x 15 500 + 50 L x 16 000 + 100 L x 14 200 = savdo 2 995 000, kutilgan 2 990 000, farq 0"
                $smenaNusxa = $null
                for ($i = 0; $i -lt 60 -and -not $smenaNusxa; $i++) {
                    $smenaNusxa = Get-ChildItem $zaxPapka -Filter "fuelcontrol-*-smena$($sm2.id).db" -ErrorAction SilentlyContinue | Select-Object -First 1
                    if (-not $smenaNusxa) { Start-Sleep -Milliseconds 500 }
                }
                Check ($null -ne $smenaNusxa) "smena yopilgach fonda zaxira nusxa olindi: $($smenaNusxa.Name)"
                if ($smenaNusxa) {
                    $bosh16 = [byte[]]::new(16); $fs = [IO.File]::OpenRead($smenaNusxa.FullName); try { [void] $fs.Read($bosh16, 0, 16) } finally { $fs.Dispose() }
                    Check (([Text.Encoding]::ASCII.GetString($bosh16, 0, 15) -eq 'SQLite format 3') -and ($smenaNusxa.Length -gt 0)) "nusxa haqiqiy SQLite bazasi ($($smenaNusxa.Length) bayt)"
                    Check (@(Get-ChildItem $zaxPapka -Filter 'fuelcontrol-*.db').Count -eq $fayllar0 + 1) "smena yopilganda aynan bitta nusxa olindi (jami $($fayllar0 + 1) ta)"
                    $tgH = $null
                    for ($i = 0; $i -lt 40; $i++) { $tgH = (Get-Json $p4 $tk '/zaxira/holat').telegram; if ($tgH.oxirgiTuri -eq 'nusxa') { break }; Start-Sleep -Milliseconds 500 }
                    Check (($tgH.oxirgiTuri -eq 'nusxa') -and ($tgH.oxirgiMuvaffaqiyat -eq $false) -and ($tgH.oxirgiXabar -match [regex]::Escape($smenaNusxa.Name)) -and ($tgH.oxirgiXabar -notmatch 'AAHdq')) "nusxa Telegram'ga yuborishga urindi (soxta token): holatda xato, fayl nomi bor, token yo'q: [$($tgH.oxirgiXabar)]"
                }
                $a3 = @(Get-Json $p4 $tk '/aparatlar')
                Check (($a3.Count -eq 2) -and ([decimal]$a3[0].totalLitr -eq [decimal]109897.58) -and ([decimal]$a3[0].bakQoldiq -eq [decimal]4917.83) -and ($null -eq $a3[0].krugHajmi) -and ([decimal]$a3[1].totalLitr -eq [decimal]20030.52) -and ([decimal]$a3[1].bakQoldiq -eq [decimal]10050) -and ([decimal]$a3[1].krugHajmi -eq 10000)) "aparatlar smena yopilgach: 4 - total 109897.58 / bak 4917.83; 5 - total 20030.52 / bak 10050.00, krug 10 000 joyida"
                $h = Get-Json $p4 $tk '/hisobot'
                Check (($h.jami.nasiya -eq 240000) -and ($h.jami.qaytganNasiya -eq 30000) -and ($h.eskiQarzQaytgan -eq 0)) "hisobot ochiladi: nasiya 240 000 (200 000 + 40 000), qaytgan 30 000, eski qarz 0"
                if ($op) {
                    $kodOxirgi = try { (Invoke-WebRequest "$api6/smenalar/oxirgi" -Headers @{ Authorization = "Bearer $($op.token)" } -UseBasicParsing).StatusCode } catch { [int] $_.Exception.Response.StatusCode }
                    $tp = Get-Json $p4 $op.token '/smenalar/oxirgi/topshirish'
                    Check (($kodOxirgi -eq 403) -and ($tp.id -eq $sm2.id) -and ($tp.PSObject.Properties.Name -notcontains 'savdo')) "8.10: operatorga /smenalar/oxirgi -> $kodOxirgi, /oxirgi/topshirish pul natijasisiz (smena $($tp.id))"
                }
            }
        }
        Stop-Api

        # API to'xtadi, baza yopiq: natija bazaning o'zidan ham tekshiriladi.
        $izOxiri = Baza-Izi $dbFayl $ustunEski
        $yangiMig = Sql-Qatorlar $dbFayl 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId'
        Check (($yangiMig -join ';') -eq ($repoMig -join ';')) "baza tarixi repodagi $($repoMig.Count) ta migratsiya bilan bir xil: kutilayotgan ham, begona ham yo'q (oxirgisi $($yangiMig[-1]))"
        # Tuzatish, krug va yopish faqat Aparatlar, Smenalar, SmenaKorsatkichlari va Audit'ga tegadi; qolgan hamma jadval (eski ustunlari) qiymatigacha bir xil.
        $ozgargan = (@($izEski.Keys | Where-Object { $_ -ne '__EFMigrationsHistory' -and $izEski[$_] -ne $izOxiri[$_] }) | Sort-Object) -join ', '
        Check ($ozgargan -eq 'Aparatlar, Audit, SmenaKorsatkichlari, Smenalar, ZaxiraSozlamalari') "faqat kutilgan jadvallar o'zgardi (aparatlar, audit, smena segmentlari, smenalar, zaxira sozlamasining yuborish holati): $ozgargan"
        $yakuniy = [ordered] @{ Foydalanuvchilar = 3; Yoqilgilar = 2; NarxTarixlari = 1; Aparatlar = 2; BakKirimlari = 1; BakTuzatishlari = 1; Smenalar = 2; SmenaKorsatkichlari = 5; Nasiyalar = 2; NasiyaQaytishlari = 1; Xarajatlar = 1; Harakatlar = 3; ZaxiraSozlamalari = 1 }
        $nomos = @($yakuniy.Keys | Where-Object { (Iz-Soni $izOxiri $_) -ne $yakuniy[$_] })
        Check ($nomos.Count -eq 0) "yakuniy qatorlar soni: foydalanuvchilar 3, aparatlar 2, bak kirimi 1, smena segmentlari 5 (birinchi smenada 2, ikkinchisida 3), nasiyalar 2, harakatlar 3$(if ($nomos.Count) { ' - mos kelmadi: ' + ($nomos -join ', ') })"
        $audTurOxiri = Sql-Qatorlar $dbFayl 'SELECT Tur, COUNT(*) FROM Audit GROUP BY Tur ORDER BY Tur'
        $tEski = @{}; foreach ($qt in $audTurEski) { $tEski[$qt.Split('|')[0]] = [int] $qt.Split('|')[1] }
        $tYangi = @{}; foreach ($qt in $audTurOxiri) { $tYangi[$qt.Split('|')[0]] = [int] $qt.Split('|')[1] }
        $tFarq = (@(@($tEski.Keys) + @($tYangi.Keys) | Select-Object -Unique | Sort-Object | Where-Object { [int]$tYangi[$_] -ne [int]$tEski[$_] } | ForEach-Object { '{0} {1:+#;-#}' -f $_, ([int]$tYangi[$_] - [int]$tEski[$_]) })) -join ', '
        Check (((Iz-Soni $izOxiri 'Audit') -eq ($auditEski + 1)) -and ($tFarq -eq 'smena +1')) "audit yozuvlari $auditEski -> $($auditEski + 1): faqat 'Smena yopildi' qo'shildi (/auth/yangila va nusxa yuborish auditga yozmaydi)"
        $smQ = Sql-Qatorlar $dbFayl "SELECT NasiyaJami, QaytganNasiya, XarajatJami, Savdo, Plastik, DepozitFarqi, Kutilgan, Farq, PlastikSummalari FROM Smenalar WHERE Id = $($sm2.id)"
        Check (($smQ.Count -eq 1) -and ($smQ[0] -eq '40000|30000|0|2995000|55000|20000|2990000|0|60000,65000')) "yopilgan smena yig'indilari bazada: nasiya 40 000, qaytgan 30 000, savdo 2 995 000, plastik 55 000, depozit farqi 20 000, kutilgan 2 990 000, farq 0, plastik qismlari 60000,65000"
        $sq = "SELECT COUNT(*) FROM SmenaKorsatkichlari WHERE SmenaId = $($sm2.id)"
        $s4 = Sql-Qatorlar $dbFayl "$sq AND AparatId = $($apr.id) AND Tartib = 1 AND CAST(Boshi AS REAL) = 109797.58 AND CAST(Oxiri AS REAL) = 109847.58 AND Narx = 15500 AND CAST(Litr AS REAL) = 50 AND Summa = 775000 AND NarxOzgarishida = 1"
        $s4b = Sql-Qatorlar $dbFayl "$sq AND AparatId = $($apr.id) AND Tartib = 2 AND CAST(Boshi AS REAL) = 109847.58 AND CAST(Oxiri AS REAL) = 109897.58 AND Narx = 16000 AND CAST(Litr AS REAL) = 50 AND Summa = 800000 AND NarxOzgarishida = 0"
        $s5 = Sql-Qatorlar $dbFayl "$sq AND AparatId = $($apr2.id) AND Tartib = 1 AND CAST(Boshi AS REAL) = 19930.52 AND CAST(Oxiri AS REAL) = 20030.52 AND Narx = 14200 AND CAST(Litr AS REAL) = 100 AND Summa = 1420000 AND NarxOzgarishida = 0"
        $sJami = Sql-Qatorlar $dbFayl $sq
        Check (($s4[0] -eq '1') -and ($s4b[0] -eq '1') -and ($s5[0] -eq '1') -and ($sJami[0] -eq '3')) "yopilgan smena segmentlari bazada: 4-aparat 50 L x 15 500 (narx o'zgarishi) + 50 L x 16 000, 5-aparat 100 L x 14 200"
        $zSoz = Sql-Qatorlar $dbFayl "SELECT COUNT(*), SUM(TelegramTokenShifr LIKE 'dpapi1:%'), SUM(instr(TelegramTokenShifr, 'AAHdq') = 0 AND instr(TelegramTokenShifr, '123456789') = 0), MIN(TelegramChatId), MIN(TelegramYoqilgan) FROM ZaxiraSozlamalari"
        Check ($zSoz[0] -eq '1|1|1|-1001234567890|1') "ZaxiraSozlamalari: bitta qator, token bazada DPAPI bilan shifrlangan (ochiq matn yo'q), chat ID, yoqilgan ($($zSoz[0]))"
        $nuqtaB = [string][char] 0x00B7
        $zAud = @(Sql-Qatorlar $dbFayl "SELECT Tafsilot FROM Audit WHERE Amal = 'Telegram sozlandi'")
        Check (($zAud.Count -eq 1) -and ($zAud[0] -eq "token yangilandi $nuqtaB chat ID -1001234567890") -and ($zAud[0] -notmatch 'Dsaw|AAHdq')) "auditda Telegram sozlamasi: '$($zAud[0])' (tokenning belgisi yo'q)"
        $zArx = @(Sql-Qatorlar $dbFayl "SELECT COUNT(*) FROM Audit WHERE Amal = 'Zaxira nusxa olindi'")
        Check ($zArx[0] -eq '1') "auditda 'Zaxira nusxa olindi' 1 ta: eski versiyadagi qo'lda nusxa (smena yopilganda avtomatik nusxa auditga yozilmaydi)"
        $ap = Sql-Qatorlar $dbFayl "SELECT COUNT(*) FROM Aparatlar WHERE (Raqam = 4 AND CAST(TotalLitr AS REAL) = 109897.58 AND CAST(BakQoldiq AS REAL) = 4917.83 AND KrugHajmi IS NULL) OR (Raqam = 5 AND CAST(TotalLitr AS REAL) = 20030.52 AND CAST(BakQoldiq AS REAL) = 10050 AND CAST(KrugHajmi AS REAL) = 10000)"
        Check ($ap[0] -eq '2') "aparatlar bazada: 4 - total 109897.58, bak 4917.83, krug yo'q; 5 - total 20030.52, bak 10050.00, krug 10 000"
        $smena1Oxiri = Sql-Qatorlar $dbFayl "SELECT * FROM Smenalar WHERE Id = $($sm.id)"
        $seg1Oxiri = Sql-Qatorlar $dbFayl "SELECT * FROM SmenaKorsatkichlari WHERE SmenaId = $($sm.id) ORDER BY Id"
        Check ((($seg1Oxiri -join "`n") -eq ($seg1Eski -join "`n")) -and (($smena1Oxiri -join "`n") -eq ($smena1Eski -join "`n"))) "birinchi (yopilgan) smena va uning segmentlari tegilmagan"
        Check ((Sql-Qatorlar $dbFayl 'PRAGMA integrity_check')[0] -eq 'ok') "baza yaxlitligi: PRAGMA integrity_check = ok"
        $fk = Sql-Qatorlar $dbFayl 'PRAGMA foreign_key_check'
        Check ($fk.Count -eq 0) "begona kalit buzilishi yo'q (PRAGMA foreign_key_check bo'sh)"
        $idx = Sql-Qatorlar $dbFayl "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'IX_Smenalar_BittaOchiq'"
        Check ($idx.Count -eq 1) "'bitta ochiq smena' indeksi (IX_Smenalar_BittaOchiq) yangilashdan keyin ham bor"
        $jwt0 = (Json "$app6\Server\appsettings.Production.json").Jwt.Kalit
        Uninstall-One $app6

        '-- 8b. Baza bor bo''lsa /ADMINPAROL berilsa ham e''tiborga olinmaydi --'
        $code = Run-Setup ($baza + $tasks8 + "/ADMINPAROL=$yangiParol") 'new-over-data-pwd'
        Check ($code -eq 0) "qayta o'rnatish /ADMINPAROL bilan (kod $code)"
        Check ($null -eq (Json "$app6\Server\appsettings.Production.json").Seed) "baza bor: AdminParol konfiguratsiyaga yozilmadi"
        $up = Start-Api $app6 $p4
        Check $up "API ishga tushdi"
        if ($up) {
            Check ($null -ne (Login $p4 $eskiParol)) "eski admin paroli ishlaydi"
            Check ($null -eq (Login $p4 $yangiParol)) "yangi parol ISHLAMAYDI (toza o'rnatish uchun ma'lumot papkasini o'chirish shart)"
            $rb = Login $p4 $eskiParol
            $hb = if ($rb) { Get-Json $p4 $rb.token '/zaxira/holat' } else { $null }
            $jwt1 = (Json "$app6\Server\appsettings.Production.json").Jwt.Kalit
            Check ($hb -and ($hb.telegram.tokenMaska -eq ($uch + 'Dsaw')) -and ($hb.telegram.chatId -eq $tgChat) -and ($hb.telegram.faol -eq $true)) "o'chirib qayta o'rnatishdan keyin ham Telegram tokeni va chat ID saqlandi (JWT kaliti $(if ($jwt1 -ne $jwt0) { 'yangilandi' } else { 'o''zgarmadi' }); ma'lumot papkasidagi baza o'z joyida)"
            Check ($hb.papka -eq "$data6\zaxira") "o'chirilgan Local.json bilan zaxira papkasi standartga qaytdi: $data6\zaxira"
        }
        Stop-Api
        Uninstall-One $app6

        '-- 8c. Ma''lumot papkasini o''chirib toza o''rnatish: bazada faqat admin --'
        Remove-Item -LiteralPath $data6 -Recurse -Force
        $code = Run-Setup ($baza + $tasks8 + "/ADMINPAROL=$yangiParol") 'new-clean'
        Check ($code -eq 0) "toza o'rnatish (kod $code)"
        $up = Start-Api $app6 $p4
        Check $up "yangi API toza bazada ishga tushdi"
        if ($up) {
            $r = Login $p4 $yangiParol
            Check ($null -ne $r) "yangi admin paroli ishlaydi"
            Check ($null -eq (Login $p4 $eskiParol)) "eski parol ishlamaydi"
            Check ($null -eq (Login $p4 $opParol $opLogin)) "eski operator yo'q"
            if ($r) {
                $hdr = @{ Authorization = "Bearer $($r.token)" }
                foreach ($yol in '/yoqilgilar', '/aparatlar', '/smenalar') {
                    Check ((Invoke-WebRequest "$api6$yol" -Headers $hdr -UseBasicParsing).Content.Trim() -eq '[]') "toza o'rnatish: $yol bo'sh"
                }
                Check ((Count-Json $p4 $r.token '/foydalanuvchilar') -eq 1) "toza o'rnatish: bitta foydalanuvchi (admin)"
            }
        }
        Stop-Api
    }

}
catch { "!!! ISTISNO: $($_.Exception.Message)"; $_.InvocationInfo.PositionMessage; $_.ScriptStackTrace; $script:fail++ }
finally {
    Stop-Api
    Uninstall-All
}

""
"Tekshiruvlar: $($script:total - $script:fail)/$($script:total) o'tdi"
if ($script:fail -eq 0) {
    'HAMMASI O''TDI'
    if (-not $KeepFiles) { try { [IO.Directory]::Delete($T, $true) } catch { Write-Warning "Ish papkasini o'chirib bo'lmadi: $T" } }
}
else {
    "$($script:fail) TA XATO (jurnallar: $T)"
    exit 1
}
