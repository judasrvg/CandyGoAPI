[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl = "https://candygoapi.onrender.com",

    [Parameter(Mandatory = $false)]
    [string]$AdminPhone,

    [Parameter(Mandatory = $false)]
    [string]$AdminPassword,

    [Parameter(Mandatory = $false)]
    [string]$OutputFolder = "",

    [Parameter(Mandatory = $false)]
    [switch]$ClearOutput,

    [Parameter(Mandatory = $false)]
    [switch]$SkipExisting
)

$ErrorActionPreference = "Stop"

function Resolve-AbsoluteUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$PathOrUrl
    )

    $raw = $PathOrUrl.Trim()
    if ($raw -match '^https?://') {
        return $raw
    }

    if ($raw.StartsWith("/")) {
        return "$BaseUrl$raw"
    }

    return "$BaseUrl/$raw"
}

function Get-FileNameFromUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Fallback
    )

    try {
        $uri = [System.Uri]$Url
        $name = [System.IO.Path]::GetFileName($uri.LocalPath)
        if ([string]::IsNullOrWhiteSpace($name)) {
            return $Fallback
        }

        return $name
    } catch {
        return $Fallback
    }
}

function To-SafeBaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Raw
    )

    $trimmed = $Raw.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw "ApiBaseUrl es requerido."
    }

    return $trimmed.TrimEnd("/")
}

if ([string]::IsNullOrWhiteSpace($OutputFolder)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputFolder = Join-Path $scriptRoot "..\src\CandyGo.Api\wwwroot\images\products"
}

$ApiBaseUrl = To-SafeBaseUrl -Raw $ApiBaseUrl

if ([string]::IsNullOrWhiteSpace($AdminPhone)) {
    $AdminPhone = Read-Host "Telefono admin"
}

if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    $secure = Read-Host "Password admin" -AsSecureString
    $AdminPassword = [System.Net.NetworkCredential]::new("", $secure).Password
}

if ([string]::IsNullOrWhiteSpace($AdminPhone) -or [string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw "Debes indicar AdminPhone y AdminPassword."
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputFolder)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

if ($ClearOutput) {
    Get-ChildItem -Path $resolvedOutput -File | Remove-Item -Force
}

Write-Host "API: $ApiBaseUrl"
Write-Host "Destino: $resolvedOutput"
Write-Host "Autenticando admin..."

$loginBody = @{
    phone    = $AdminPhone
    password = $AdminPassword
} | ConvertTo-Json

try {
    $login = Invoke-RestMethod `
        -Method Post `
        -Uri "$ApiBaseUrl/api/auth/admin/login" `
        -ContentType "application/json" `
        -Body $loginBody
}
catch {
    $ex = $_.Exception
    $detail = ""

    try {
        if ($ex.Response -and $ex.Response.GetResponseStream()) {
            $reader = New-Object System.IO.StreamReader($ex.Response.GetResponseStream())
            $detail = $reader.ReadToEnd()
            $reader.Close()
        }
    } catch {
        # ignore parse detail
    }

    if (-not [string]::IsNullOrWhiteSpace($detail)) {
        throw "Login admin falló. Detalle API: $detail"
    }

    throw "Login admin falló. Verifica telefono/password y que el admin exista."
}

if ([string]::IsNullOrWhiteSpace($login.token)) {
    throw "No se recibió token de admin."
}

$headers = @{
    Authorization    = "Bearer $($login.token)"
    "X-CandyGo-Client" = "web-admin"
}

Write-Host "Obteniendo productos..."
$products = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/admin/products" -Headers $headers
if ($null -eq $products) {
    $products = @()
}

$downloaded = 0
$skipped = 0
$failed = 0
$seenNames = @{}

foreach ($product in $products) {
    $rawImage = [string]$product.imageUrl
    if ([string]::IsNullOrWhiteSpace($rawImage)) {
        continue
    }

    $absoluteUrl = Resolve-AbsoluteUrl -BaseUrl $ApiBaseUrl -PathOrUrl $rawImage
    $fallbackName = "product_$($product.id)_$(Get-Random).webp"
    $fileName = Get-FileNameFromUrl -Url $absoluteUrl -Fallback $fallbackName

    if ($seenNames.ContainsKey($fileName)) {
        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($fileName)
        $ext = [System.IO.Path]::GetExtension($fileName)
        $fileName = "{0}_{1}{2}" -f $baseName, $product.id, $ext
    }
    $seenNames[$fileName] = $true

    $outputPath = Join-Path $resolvedOutput $fileName
    if ($SkipExisting -and (Test-Path $outputPath)) {
        $skipped++
        continue
    }

    try {
        Invoke-WebRequest -Uri $absoluteUrl -OutFile $outputPath
        $downloaded++
        Write-Host ("OK  [{0}] {1}" -f $product.id, $fileName)
    } catch {
        $failed++
        Write-Warning ("FAIL [{0}] {1} -> {2}" -f $product.id, $absoluteUrl, $_.Exception.Message)
    }
}

Write-Host ""
Write-Host "Resumen:"
Write-Host "Descargadas: $downloaded"
Write-Host "Saltadas:    $skipped"
Write-Host "Fallidas:    $failed"
Write-Host "Carpeta:     $resolvedOutput"
