# 定義未整理目錄
$UnorganizedDirectory ="F:\H" #"\\192.168.0.6\Download\FH\H"

# 定義目錄路徑
#$RootDirectory = "f:\h\"
$RootDirectory = "E:\H\Comic"
#$RootDirectory = "E:\H\Comic\_Legacy\"


# 定義要匹配的圖檔副檔名
$ImageExtensions = @(".jpg", ".jpeg", ".png")

# 定義壓縮檔副檔名
$ArchiveExtensions = @(".zip", ".rar", ".7z")

# 定義是否為調試模式
$Debug = $false # 設為 true 時不執行 copy

# 定義排除的目錄
$ExcludedDirectories = @("E:\H\Comic\_Legacy")



# 顯示功能選單並讓使用者選擇
Write-Host "請選擇要執行的功能："
Write-Host "1. 列出並重命名子目錄中的圖片為 cover"
Write-Host "2. 生成Cover檔案"
Write-Host "3. 未整理檔案歸檔"

$Choice = Read-Host "輸入選項編號"

switch ($Choice) {
    "1" {
        # 功能 1: 列出並重命名子目錄中的圖片為 cover
        Write-Host "執行功能 1: 列出並重命名圖片"
        $Subdirectories = Get-ChildItem -Path $RootDirectory -Directory
        foreach ($Subdir in $Subdirectories) {
            if ($ExcludedDirectories -contains $Subdir.Name) {
                Write-Host "Skipping excluded directory: $($Subdir.FullName)"
                continue
            }
            foreach ($Extension in $ImageExtensions) {
                $Files = Get-ChildItem -Path $Subdir.FullName -Filter "*$Extension" -File -ErrorAction SilentlyContinue
                foreach ($File in $Files) {
                    $NewName = "$($Subdir.FullName)\cover$($File.Extension)"
                    if ($File.BaseName -ieq "cover") {
                        Write-Host "File $($File.FullName) is already named 'cover', skipping."
                        continue
                    }
                    try {
                        Rename-Item -Path $File.FullName -NewName $NewName -ErrorAction Stop
                        Write-Host "Renamed $($File.FullName) to $NewName"
                    } catch {
                        Write-Host "Failed to rename $($File.FullName): $_"
                    }
                }
            }
        }
    }
    "2" {
       # 功能 2: 僅列出子目錄中的圖片
        Write-Host "執行功能 2: 處理子目錄中的圖片或壓縮檔"
        $Subdirectories = Get-ChildItem -Path $RootDirectory -Directory
        foreach ($Subdir in $Subdirectories) {
            if ($ExcludedDirectories -contains $Subdir.Name) {
                Write-Host "Skipping excluded directory: $($Subdir.FullName)"
                continue
            }
            # 檢查是否已有檔名為 cover 的檔案
            $ExistingFiles = @(Get-ChildItem -Path $Subdir.FullName -Filter "cover*" -File -ErrorAction SilentlyContinue)
            if ($ExistingFiles.Count -eq $ImageExtensions.Count) {
                Write-Host "All cover files already exist in directory $($Subdir.FullName), skipping."
                continue
            }
            $ChildDirs = Get-ChildItem -Path $Subdir.FullName -Directory | Sort-Object Name | Select-Object -First 1
            foreach ($ChildDir in $ChildDirs) {
                # 處理圖片
                $Images = Get-ChildItem -LiteralPath $ChildDir.FullName -File |
                    Where-Object { $_.Extension -in $ImageExtensions } |
                    Sort-Object Name | Select-Object -First 1
                if ($Images) {
                    foreach ($Image in $Images) {
                        #$DestinationPath = "$($Subdir.FullName)\cover$($Image.Extension)"
                        $DestinationPath = "$($Subdir.FullName)\cover$($Image.Extension)"
                        if (-Not (Test-Path -Path $DestinationPath)) {
                            if (-Not $Debug) {
                                Copy-Item -LiteralPath $Image.FullName -Destination $DestinationPath -Force
                                Write-Host "Copied image $($Image.FullName) to $DestinationPath as cover"
                            } else {
                                Write-Host "Debug mode: [Folder] Skipping copy of $($Image.FullName) to $DestinationPath"
                            }
                        } else {
                          #  Write-Host "Cover image already exists at $DestinationPath, skipping copy."
                        }
                    }
                    continue
                }
            }
            # 處理壓縮檔
         # 處理壓縮檔
                $Archives = Get-ChildItem -LiteralPath $Subdir.FullName -File |
                    Where-Object { $_.Extension -in $ArchiveExtensions } |
                    Sort-Object Name | Select-Object -First 1
                if ($Archives) {
                    foreach ($Archive in $Archives) {
                        $DestinationPath = "$($Subdir.FullName)\cover$($Archive.Extension)"
                        if (-Not (Test-Path -Path $DestinationPath)) {
                            if (-Not $Debug) {
                                try {
                                    Add-Type -AssemblyName System.IO.Compression.FileSystem
                                    $ZipPath = $Archive.FullName
                                    $TempDir = [System.IO.Path]::GetTempPath() + [System.IO.Path]::GetRandomFileName()
                                    [System.IO.Directory]::CreateDirectory($TempDir) | Out-Null
                                    [System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $TempDir)
                                    $FilesInZip = Get-ChildItem -Path $TempDir -File |
                                        Where-Object { $_.Extension -in $ImageExtensions } |
                                        Sort-Object Name | Select-Object -First 1
                                    if ($FilesInZip) {
                                        Copy-Item -Path $FilesInZip.FullName -Destination "$($Subdir.FullName)\cover$($FilesInZip.Extension)" -Force
                                        Write-Host "Extracted and copied $($FilesInZip.FullName) to $($Subdir.FullName) as cover"
                                    }
                                    Remove-Item -Path $TempDir -Recurse -Force
                                } catch {
                                    Write-Host "Failed to process archive $($Archive.FullName): $_"
                                }
                            } else {
                                Write-Host "Debug mode: Skipping processing of $($Archive.FullName)"
                            }
                        } else {
                            Write-Host "Cover archive already exists at $DestinationPath, skipping."
                        }
                    }
                }
        }
    }
   "3" {
        # 功能 3: 掃描第一層子目錄並比對關鍵字目錄
        Write-Host "執行功能 3: 掃描第一層子目錄並比對關鍵字目錄"
        if (-Not (Test-Path -Path $UnorganizedDirectory)) {
            Write-Host "未整理目錄不存在：$UnorganizedDirectory"
            return
        }
        # 初始化移動清單
        $MoveList = @()

        # 僅檢查第一層子目錄
        $Subdirectories = Get-ChildItem -Path $UnorganizedDirectory -Directory -ErrorAction SilentlyContinue
        foreach ($Subdir in $Subdirectories) {
            if ($Subdir.Name -match "\[(.*?)\]") {
                $Keyword = $Matches[1]
                # 清理並分隔關鍵字
                $Keyword = $Keyword -replace "[^\p{L}\p{N}\s]", "" # 保留文字（中、英、日）與數字
                $KeywordArray = $Keyword.Trim() -split "\s+"         # 分割成陣列

                foreach ($Key in $KeywordArray) {
                    $MatchingDirs = Get-ChildItem -Path $RootDirectory -Directory |
                        Where-Object { $_.Name -like "*$Key*" } # 模糊匹配
                    if ($MatchingDirs) {
                        foreach ($MatchingDir in $MatchingDirs) {
                            $MoveList += [PSCustomObject]@{
                                Source = $Subdir.FullName
                                Destination = $MatchingDir.FullName
                            }
                            break
                        }
                        break
                    }
                }
            }
        }

        # 處理壓縮檔
        $Archives = Get-ChildItem -Path $UnorganizedDirectory -File |
            Where-Object { $_.Extension -in $ArchiveExtensions }
        foreach ($Archive in $Archives) {
            if ($Archive.Name -match "\[(.*?)\]") {
                $ArchiveKeyword = $Matches[1] -replace "[^\p{L}\p{N}\s]", ""
                $ArchiveKeywordArray = $ArchiveKeyword.Trim() -split "\s+"

                foreach ($Key in $ArchiveKeywordArray) {
                    $MatchingDirs = Get-ChildItem -Path $RootDirectory -Directory |
                        Where-Object { $_.Name -like "*$Key*" }
                    if ($MatchingDirs) {
                        foreach ($MatchingDir in $MatchingDirs) {
                            $MoveList += [PSCustomObject]@{
                                Source = $Archive.FullName
                                Destination = $MatchingDir.FullName
                            }
                            break
                        }
                        break
                    }
                }
            }
        }

        # 列出所有移動操作並讓使用者確認
        # 計算最大寬度以對齊 Source 和 Destination
        $MaxSourceWidth = ($MoveList | ForEach-Object { $_.Source.Length } | Measure-Object -Maximum).Maximum
        $MaxDestinationWidth = ($MoveList | ForEach-Object { $_.Destination.Length } | Measure-Object -Maximum).Maximum

         # 使用 Destination 作為群組條件顯示
        $GroupedMoveList = $MoveList | Group-Object -Property Destination

        foreach ($Group in $GroupedMoveList) {
            Write-Host "Destination: $($Group.Name)"
            $Group.Group | Format-Table -Property Source -AutoSize
            Write-Host ""
        }


        $Confirm = Read-Host "確認執行移動操作？ (y/n)"
        if ($Confirm -eq "y") {
            $TotalItems = $MoveList.Count
            $CurrentItem = 0

            foreach ($MoveItem in $MoveList) {
                $CurrentItem++
                Write-Progress -Activity "Moving Files" -Status "Processing $CurrentItem of $TotalItems" -PercentComplete (($CurrentItem / $TotalItems) * 100)

                try {
                    Move-Item -LiteralPath $MoveItem.Source -Destination $MoveItem.Destination -Force
                    Write-Host "Moved $($MoveItem.Source) to $($MoveItem.Destination)"
                } catch {
                    Write-Host "Failed to move $($MoveItem.Source) to $($MoveItem.Destination): $_"
                }
            }
        } else {
            Write-Host "移動操作已取消。"
            }
    }
    default {
        Write-Host "無效的選項，請重新執行腳本。"
    }
}
