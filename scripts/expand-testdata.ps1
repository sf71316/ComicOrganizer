$ErrorActionPreference = 'Stop'

$root = Join-Path $PSScriptRoot '..\TestData\Library'
$src = Join-Path $root '[山田太郎]\[山田太郎] 星空之旅\Ch1\01.jpg'
if (-not (Test-Path -LiteralPath $src)) {
    Write-Error "Source jpg not found: $src"
    exit 1
}

$authorWorks = [ordered]@{
    "[山田太郎]"      = @("星空之旅", "月光下", "晨曦", "青春日記", "深秋告白")
    "[鈴木花子]"      = @("海邊的夏天", "雪國物語", "櫻花飄落", "夜空の星", "靜謐之森")
    "[Studio Alpha]"  = @("Mystic Forest", "Crystal Mountain", "Ocean Tide", "Solar Flare", "Lunar Eclipse")
    "[田中健一]"      = @("武士魂", "江戶物語", "風林火山", "修羅之道", "戰國紀")
    "[佐藤美咲]"      = @("戀愛日記", "校園物語", "心動瞬間", "純情傳說", "魔法少女")
    "[小林優子]"      = @("異世界記", "龍與騎士", "魔法學院", "精靈之歌", "帝國黃昏")
    "[高橋悠太]"      = @("機甲傳奇", "星際遠征", "宇宙騎兵", "未來戰記", "賽博龐克")
    "[伊藤さくら]"    = @("家庭故事", "廚房日常", "孩子的夏天", "母親之愛", "家族羈絆")
    "[渡辺翔]"        = @("籃球少年", "足球熱情", "棒球之魂", "網球王子", "武道精神")
    "[中村結衣]"      = @("妖怪繪卷", "神社物語", "陰陽師", "平安物怪", "物の怪退治")
    "[松本陽介]"      = @("推理事件簿", "偵探日記", "密室殺人", "名偵探", "怪盜紳士")
    "[Studio Beta]"   = @("Galactic Wars", "Iron Knight", "Fire Dragon", "Sky Warrior", "Storm Bringer")
    "[Studio Gamma]"  = @("Hidden Truth", "Dark Secret", "Lost City", "Ancient Curse", "Forbidden Land")
    "[Studio Delta]"  = @("Spring Bloom", "Summer Wave", "Autumn Leaves", "Winter Snow", "Eternal Season")
    "[Studio Epsilon]"= @("Steel Heart", "Glass Tower", "Velvet Rose", "Gold Dust", "Silver Lining")
    "[John Smith]"    = @("City Lights", "Urban Tales", "Street Hero", "Night Walker", "Day Dreamer")
    "[Emily Brown]"   = @("Tea Time", "Garden Love", "Vintage Days", "Cottage Life", "Country Road")
    "[David Chen]"    = @("Dragon Phoenix", "Eastern Wind", "Bamboo Sword", "Jade Mountain", "Silk Road")
    "[Sarah Lee]"     = @("Ocean Depths", "Sky Temple", "Mountain Peak", "Forest Path", "Desert Sands")
    "[Michael Park]"  = @("Cyber Genesis", "Quantum Leap", "Neural Net", "Virtual World", "Digital Dawn")
}

$created = 0
foreach ($author in $authorWorks.Keys) {
    foreach ($work in $authorWorks[$author]) {
        foreach ($ch in @("Ch1", "Ch2")) {
            $folder = Join-Path $root "$author\$author $work\$ch"
            if (-not (Test-Path -LiteralPath $folder)) {
                New-Item -Path $folder -ItemType Directory -Force | Out-Null
            }
            for ($i = 1; $i -le 4; $i++) {
                $name = ('{0:D2}.jpg' -f $i)
                $dst = Join-Path $folder $name
                if (-not (Test-Path -LiteralPath $dst)) {
                    Copy-Item -LiteralPath $src -Destination $dst -Force
                    $created++
                }
            }
        }
    }
}

Write-Host "Done. Created $created jpg files."
Write-Host ("Authors: {0}, Works/author: 5, Chapters/work: 2, Pages/chapter: 4" -f $authorWorks.Count)
