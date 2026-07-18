$ErrorActionPreference = 'Stop'

$expected = [ordered]@{
    'index.html' = '8F8F147A5095F879ADC0899E4D858C193425E33AA122012187A289F9C995A8D5'
    'style.css' = '6113FBAC86FBE7A15DFF6E187C20ED2EB064926C0B4DCF90F7BFD69452109FB8'
    'app.js' = '5BF01813754CE3ADD0ADE18AA1653D41C8F9F084EE85A56B4BA1A87C491FFE0E'
}

foreach ($fileName in $expected.Keys) {
    $path = Join-Path $PSScriptRoot $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "레퍼런스 파일이 없습니다: $path"
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $expected[$fileName]) {
        throw "레퍼런스 파일이 변경되었습니다: $fileName"
    }
}

Write-Output 'Reference design checks passed: 1/1'
