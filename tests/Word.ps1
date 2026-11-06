# Called by Integration.ps1 with PAIRTAP_TEST_WORD=1.
if (Get-Process WINWORD -ErrorAction SilentlyContinue) {
    throw 'Close Word before running the isolated Word fixture.'
}

$word = New-Object -ComObject Word.Application
$document = $null
$other = $null
try {
    $document = $word.Documents.Add()
    $word.Visible = $true
    $document.Activate()
    Pump 500
    $wordWindow = (Get-Process WINWORD).MainWindowHandle
    $wordId = (Get-Process WINWORD).Id
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        [void][TestKeys]::SetForegroundWindow($wordWindow)
        Pump 150
        if ([TestKeys]::Owner([TestKeys]::GetForegroundWindow()) -eq $wordId) { break }
        [TestKeys]::Stroke(0x12)
    }
    $front = Get-Process -Id ([TestKeys]::Owner([TestKeys]::GetForegroundWindow()))
    Assert ($front.Id -eq $wordId) "Word document focused (foreground $($front.ProcessName))"
    $document.Content.Text = 'prefix AbC suffix'
    $document.Range(7, 10).Bold = 1
    $document.Range(7, 10).Select()
    $before = $sequenceMethod.Invoke($null,@())
    [TestKeys]::Symbol(0x39)
    $wrapped = Wait-Condition { $document.Content.Text.TrimEnd("`r") -cin @('prefix (AbC) suffix', 'prefix （AbC） suffix') } 1800
    Assert $wrapped "Word wraps selected text (actual $($document.Content.Text.TrimEnd("`r")))"
    Assert ($sequenceMethod.Invoke($null,@()) -eq $before) 'Word leaves the clipboard sequence unchanged'
    Assert ($document.Range(8, 11).Bold -eq -1) 'Word preserves bold formatting'
    [void]$document.Undo(1)
    Assert ($document.Content.Text.TrimEnd("`r") -ceq 'prefix AbC suffix') 'Word undo removes both delimiters'
    [void]$document.Redo(1)
    Assert ($document.Content.Text.TrimEnd("`r") -cin @('prefix (AbC) suffix', 'prefix （AbC） suffix')) 'Word redo restores both delimiters'
    Pump 150
    $document.Range(8, 11).Select()
    [TestKeys]::Hotkey(0x75)
    Assert (Wait-Condition { (Get-Process -Id ([TestKeys]::Owner([TestKeys]::GetForegroundWindow())) -ErrorAction SilentlyContinue).Path -eq $env:PAIRTAP_TEST_EXE } 1800) 'Word selection opens the palette'
    [TestKeys]::Enter()
    Assert (Wait-Condition { $document.Content.Text.TrimEnd("`r") -cin @('prefix (“AbC”) suffix', 'prefix （“AbC”） suffix') } 2200) 'Word palette returns to the original selection'

    $document.Content.Text = 'prefix hyperlink suffix'
    $link = $document.Hyperlinks.Add($document.Range(7, 16), 'https://example.com')
    $address = $link.Address
    $field = $document.Fields.Item(1)
    $code = $field.Code.Text
    $fieldCode = $field.Code.Start
    $document.Range($fieldCode + 2, $fieldCode + 6).Select()
    [TestKeys]::Symbol(0x39)
    Pump 350
    Assert ($field.Code.Text -ceq $code) 'Word field instruction remains intact'
    $linkStart = $field.Result.Start
    $document.Range($linkStart + 2, $linkStart + 6).Select()
    Assert ($word.Selection.Text -ceq 'perl') 'Word fixture selects the visible hyperlink result'
    [TestKeys]::Symbol(0x39)
    Assert (Wait-Condition { $link.TextToDisplay -cin @('hy(perl)ink', 'hy（perl）ink') } 2200) 'Word wraps inside a hyperlink'
    Assert ($document.Hyperlinks.Count -eq 1 -and $link.Address -ceq $address) 'Word preserves hyperlink destination'

    function Assert-WordPair([int]$start, [int]$end, [byte]$key, [string[]]$expected) {
        $document.Range($start, $end).Select()
        $stamp = $sequenceMethod.Invoke($null,@())
        [TestKeys]::Symbol($key)
        Assert (Wait-Condition { $document.Content.Text -cin $expected } 2200) 'Word wraps only the specified range'
        Assert ($sequenceMethod.Invoke($null,@()) -eq $stamp) 'Word clipboard sequence unchanged'
    }

    $multiline = "甲😀`r乙é"
    $document.Content.Text = $multiline
    $last = $document.Content.End
    Assert-WordPair 0 $last 0x30 @("($multiline)`r", "（$multiline）`r")
    Assert ($document.Paragraphs.Count -eq 2) 'Word multiline wrap preserves paragraph count and the final marker'

    $longPrefix = 'x' * 70000
    $document.Content.Text = $longPrefix + 'AbC'
    Assert-WordPair 70000 70003 0x39 @("$longPrefix(AbC)`r", "$longPrefix（AbC）`r")

    $document.Content.Text = 'document body'
    $header = $document.Sections.Item(1).Headers.Item(1)
    $header.Range.Text = 'header text'
    $view = $word.ActiveWindow.ActivePane.View
    $view.SeekView = 1 # wdSeekPrimaryHeader
    $header.Range.Select()
    [TestKeys]::Symbol(0x39)
    Assert (Wait-Condition { $header.Range.Text -cin @("(header text)`r", "（header text）`r") } 2200) 'Word wraps header text in the header story'
    Assert ($document.Content.Text -ceq "document body`r") 'Word header operation leaves the main story unchanged'
    $view.SeekView = 0 # wdSeekMainDocument

    $document.Content.Text = 'outside'
    $table = $document.Tables.Add($document.Range(0, 0), 2, 2)
    $cell = $table.Cell(1, 1)
    $cell.Range.Text = 'cell AbC'
    $cell.Range.Select()
    $before = $sequenceMethod.Invoke($null,@())
    [TestKeys]::Symbol(0x39)
    Assert (Wait-Condition { $cell.Range.Text.TrimEnd("`r`a") -cin @('(cell AbC)', '（cell AbC）') } 2200) "Word wraps a table cell without moving its terminator (actual $($cell.Range.Text.TrimEnd("`r`a")))"
    Assert ($table.Rows.Count -eq 2 -and $table.Columns.Count -eq 2) 'Word table structure preserved'
    Assert ($sequenceMethod.Invoke($null,@()) -eq $before) 'Word table wrap leaves clipboard untouched'

    $table.Delete()
    $document.Content.Text = 'locked editable'
    $control = $document.ContentControls.Add(0, $document.Range(0, 6))
    $control.LockContents = $true
    $controlStart = $control.Range.Start
    $document.Range($controlStart + 1, $controlStart + 4).Select()
    [TestKeys]::Symbol(0x39)
    Pump 350
    Assert ($document.Content.Text -ceq "locked editable`r") 'Word locked content is unchanged'
    $control.LockContents = $false
    $document.Range($controlStart + 1, $controlStart + 4).Select()
    Assert ($word.Selection.Text -ceq 'ock') 'Word fixture selects text inside the editable content control'
    [TestKeys]::Symbol(0x39)
    $ok = Wait-Condition { $document.Content.Text -cin @("l(ock)ed editable`r", "l（ock）ed editable`r") } 2200
    Assert $ok "Word editable content control wraps in place (actual $($document.Content.Text.TrimEnd("`r")))"
    $control.Delete($false)

    $document.Content.Text = 'protected'
    $document.Protect(3, $true)
    $document.Range(0, 9).Select()
    [TestKeys]::Symbol(0x39)
    Pump 350
    Assert ($document.Content.Text -ceq "protected`r") 'Word protected document is unchanged'
    $document.Unprotect()

    $document.Range(0, 0).Select()
    $before = $sequenceMethod.Invoke($null,@())
    [TestKeys]::Symbol(0x39)
    Pump 350
    Assert ($document.Content.Text -ceq "protected`r" -and $sequenceMethod.Invoke($null,@()) -eq $before) 'Word empty selection preserves both text and clipboard'

    $document.Content.Text = 'revision'
    $document.TrackRevisions = $true
    $document.Range(0, 8).Select()
    [TestKeys]::Symbol(0x39)
    Assert (Wait-Condition { $document.Content.Text -cin @("(revision)`r", "（revision）`r") } 2200) 'Word wraps with Track Changes enabled'
    Assert ($document.Revisions.Count -ge 1) 'Word records delimiter insertion as a revision'
    $document.TrackRevisions = $false
    $document.AcceptAllRevisions()

    $other = $word.Documents.Add()
    $other.Content.Text = 'second window'
    $other.Range(0, 6).Select()
    $document.Activate()
    Pump 250
    $document.Content.Text = 'first window'
    $document.Range(0, 5).Select()
    [TestKeys]::Symbol(0x39)
    Assert (Wait-Condition { $document.Content.Text -cin @("(first) window`r", "（first） window`r") } 2200) 'Word wraps the active document with another window open'
    Assert ($other.Content.Text -ceq "second window`r") 'Word background document is unchanged'

    $document.Range(0, 7).Select()
    [TestKeys]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    [TestKeys]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)
    try {
        [TestKeys]::Stroke(0x39)
        Pump 120
        $other.Activate()
        Pump 200
    }
    finally {
        [TestKeys]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)
        [TestKeys]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
    }
    Pump 300
    Assert ($document.Content.Text -cin @("(first) window`r", "（first） window`r") -and
        $other.Content.Text -ceq "second window`r") 'Word cancels a shortcut when focus changes before the modifiers are released'
    $document.Activate()
    Pump 200

    $before = $sequenceMethod.Invoke($null,@())
    $timings = for ($i = 0; $i -lt 100; $i++) {
        $sample = "文本$i AbC"
        $document.Content.Text = $sample
        $document.Range(0, $document.Content.End - 1).Select()
        $clock = [Diagnostics.Stopwatch]::StartNew()
        [TestKeys]::Symbol(@(0x39, 0x30)[$i % 2])
        if (!(Wait-Condition { $document.Content.Text -cin @("($sample)`r", "（$sample）`r") } 2200)) {
            throw "Word stress iteration $i failed: $($document.Content.Text)"
        }
        $clock.ElapsedMilliseconds
        Pump 35
    }
    $sorted = @($timings | Sort-Object)
    Assert ($sequenceMethod.Invoke($null,@()) -eq $before) 'Word stress: 100 operations leave the clipboard sequence unchanged'
    Write-Output "TIMING Word stress: 100/100, median $($sorted[49]) ms, p95 $($sorted[94]) ms, max $($sorted[99]) ms"
}
finally {
    if ($null -ne $other) { $other.Close(0) }
    if ($null -ne $document) { $document.Close(0) }
    $word.Quit(0)
}
