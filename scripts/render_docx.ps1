# render_docx.ps1 - update Word fields in the report .docx (TOC, List of Tables,
# List of Figures, page numbers), save the editable .docx with live fields, then
# export a PDF from a flattened copy.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\render_docx.ps1 `
#       -Docx out\Deutchezz_Final_Report_Revised\Deutchezz_Final_Report_Revised.docx `
#       -Pdf  out\Deutchezz_Final_Report_Revised\Deutchezz_Final_Report_Revised.pdf
param(
    [Parameter(Mandatory = $true)][string]$Docx,
    [Parameter(Mandatory = $true)][string]$Pdf
)

$ErrorActionPreference = 'Stop'

function New-Word {
    $w = New-Object -ComObject Word.Application
    $w.Visible = $false
    $w.DisplayAlerts = 0
    $w.Options.UpdateFieldsAtPrint = $false
    $w.Options.UpdateLinksAtPrint = $false
    return $w
}

# --- step 1: update fields and save the editable document (live fields kept) ---
$word = New-Word
try {
    $doc = $word.Documents.Open($Docx)
    $doc.Fields.Update() | Out-Null
    for ($i = 1; $i -le $doc.TablesOfContents.Count; $i++) {
        $doc.TablesOfContents.Item($i).Update() | Out-Null
    }
    $doc.Repaginate()
    $doc.Save()
    Write-Output ("fields updated; pages=" + $doc.ComputeStatistics(2))
    $doc.Close($false)
}
finally { $word.Quit() }

# --- step 1b: strip the dotted tab leaders from the TOC/LOT/LOF entries -------
# Word writes a dotted leader into the cached field result when the fields are
# updated; strip it so the lists render without leader lines. The fields remain
# live, so the entries can still be updated later if the document changes.
$strip = Join-Path $PSScriptRoot 'strip_toc_leaders.py'
if (Test-Path $strip) {
    try {
        & python $strip --docx $Docx
    }
    catch {
        Write-Output ("leader strip failed: " + $_.Exception.Message)
    }
}

# --- step 2: export a PDF from a flattened copy (field codes replaced by text) ---
$tmp = Join-Path $env:TEMP ("render_" + [guid]::NewGuid().ToString('N') + ".docx")
Copy-Item $Docx $tmp -Force
$word = New-Word
try {
    $doc = $word.Documents.Open($tmp)
    foreach ($f in $doc.Fields) { $f.Unlink() | Out-Null }
    $doc.Save()
    $doc.ExportAsFixedFormat($Pdf, 17)
    Write-Output "pdf exported"
    $doc.Close($false)
}
finally {
    $word.Quit()
    Remove-Item $tmp -ErrorAction SilentlyContinue
}
