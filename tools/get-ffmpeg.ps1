$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'ffmpeg.exe'
$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$work = Join-Path $env:TEMP ('ffmpeg-download-' + [guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    Write-Host "下载 $url ..."
    $zip = Join-Path $work 'ffmpeg.zip'
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    Write-Host "解压 ..."
    Expand-Archive -LiteralPath $zip -DestinationPath $work -Force

    $exe = Get-ChildItem -Path $work -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $exe) {
        throw '压缩包里没有找到 ffmpeg.exe'
    }

    Copy-Item -LiteralPath $exe.FullName -Destination $target -Force
    Write-Host "已安装到 $target"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
