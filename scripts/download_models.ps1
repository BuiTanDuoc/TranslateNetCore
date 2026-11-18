<#
.SYNOPSIS
  Tải model OPUS-MT đã export sẵn sang ONNX từ Hugging Face cho NetTranslate (không cần Python).

.EXAMPLE
  .\scripts\download_models.ps1 en-vi vi-en
  .\scripts\download_models.ps1 en-vi vi-en -Quantized     # bản int8, nhẹ hơn ~4 lần
  .\scripts\download_models.ps1 en-vi -Force                # tải lại

  Nếu PowerShell chặn script:  powershell -ExecutionPolicy Bypass -File .\scripts\download_models.ps1 en-vi vi-en
#>
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Pairs = @("en-vi", "vi-en"),
    [switch] $Quantized,
    [switch] $Force,
    [string] $Out = (Join-Path $PSScriptRoot "..\models")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest nhanh hơn nhiều khi tắt progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$HF = "https://huggingface.co"

# Model đa ngôn ngữ đích cần token chọn ngôn ngữ
$TargetPrefixes = @{ "en-zh" = ">>cmn_Hans<<" }

function Try-Download([string] $Url, [string] $Dest) {
    try {
        Invoke-WebRequest -Uri $Url -OutFile "$Dest.part" -UseBasicParsing
        Move-Item -Force "$Dest.part" $Dest
        return $true
    } catch {
        Remove-Item -ErrorAction SilentlyContinue "$Dest.part"
        return $false
    }
}

# Tải file đầu tiên tải được trong danh sách repo
function Get-FromRepos([string[]] $Repos, [string] $RemotePath, [string] $Dest, [bool] $Required) {
    if ((Test-Path $Dest) -and -not $Force) { Write-Host "    = $(Split-Path $Dest -Leaf)"; return $true }
    foreach ($repo in $Repos) {
        if (Try-Download "$HF/$repo/resolve/main/$RemotePath" $Dest) {
            $mb = [math]::Round((Get-Item $Dest).Length / 1MB, 1)
            Write-Host "    + $(Split-Path $Dest -Leaf) ($mb MB) <- $repo"
            return $true
        }
    }
    if ($Required) { throw "Không tải được $RemotePath từ: $($Repos -join ', ')" }
    Write-Host "    - $(Split-Path $Dest -Leaf) (không có, bỏ qua)"
    return $false
}

$failed = @()
foreach ($pair in $Pairs) {
    if ($pair -notmatch '^[a-z]{2,3}-[a-z]{2,3}$') { Write-Warning "Bỏ qua '$pair' (định dạng phải như en-vi)"; continue }

    $dir = Join-Path $Out $pair
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Write-Host "[$pair] -> $dir"

    $onnxRepos = @("Xenova/opus-mt-$pair", "onnx-community/opus-mt-$pair")
    $tokRepos  = $onnxRepos + @("Helsinki-NLP/opus-mt-$pair")

    try {
        # Tokenizer & cấu hình
        foreach ($f in @("source.spm", "vocab.json", "config.json")) {
            Get-FromRepos $tokRepos $f (Join-Path $dir $f) $true | Out-Null
        }
        foreach ($f in @("target.spm", "generation_config.json")) {
            Get-FromRepos $tokRepos $f (Join-Path $dir $f) $false | Out-Null
        }

        # Model ONNX
        $suffix = if ($Quantized) { "_quantized" } else { "" }
        Get-FromRepos $onnxRepos "onnx/encoder_model$suffix.onnx" (Join-Path $dir "encoder_model$suffix.onnx") $true | Out-Null
        Get-FromRepos $onnxRepos "onnx/decoder_model$suffix.onnx" (Join-Path $dir "decoder_model$suffix.onnx") $true | Out-Null
        Get-FromRepos $onnxRepos "onnx/decoder_with_past_model$suffix.onnx" (Join-Path $dir "decoder_with_past_model$suffix.onnx") $false | Out-Null

        $src, $tgt = $pair.Split("-")
        $manifest = [ordered]@{ source = $src; target = $tgt }
        if ($TargetPrefixes.ContainsKey($pair)) { $manifest.targetPrefix = $TargetPrefixes[$pair] }
        $manifest | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $dir "model.json")

        Write-Host "[$pair] xong`n"
    } catch {
        Write-Warning "[$pair] lỗi: $($_.Exception.Message)"
        $failed += $pair
    }
}

if ($failed.Count -gt 0) {
    Write-Warning "Không tải được: $($failed -join ', ')"
    exit 1
}
Write-Host "Hoàn tất. Khởi động lại NetTranslate hoặc gọi POST /admin/models/reload."
