param([string]$ProjectFile)
$xml = [xml](Get-Content $ProjectFile)
$vn = $xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
$v = [version]$vn.Version
$new = "$($v.Major).$($v.Minor).$($v.Build).$($v.Revision + 1)"
$vn.Version = $new
$xml.Save($ProjectFile)
Write-Host "Build version: $new"
