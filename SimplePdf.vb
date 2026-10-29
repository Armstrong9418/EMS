Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text

''' <summary>
''' Mini generateur PDF (texte seul, A4, polices standard). Aucune bibliotheque externe.
''' Polices : "F1" Helvetica, "F2" Helvetica gras, "F3" Courier.
''' </summary>
Friend Class SimplePdf

    Private Const PageW As Integer = 595
    Private Const PageH As Integer = 842
    Private Const Margin As Integer = 50

    Private ReadOnly pages As New List(Of StringBuilder)
    Private cur As StringBuilder
    Private y As Single
    Private ReadOnly inv As CultureInfo = CultureInfo.InvariantCulture

    Public Sub New()
        NewPage()
    End Sub

    Private Sub NewPage()
        cur = New StringBuilder()
        pages.Add(cur)
        y = PageH - Margin
    End Sub

    Public Sub Space(h As Single)
        y -= h
        If y < Margin Then NewPage()
    End Sub

    Public Sub AddLine(text As String, Optional size As Single = 10, Optional font As String = "F1")
        If text Is Nothing Then text = ""
        Dim factor As Single = If(font = "F3", 0.6F, 0.52F)
        Dim maxChars As Integer = CInt((PageW - 2 * Margin) / (size * factor))
        If maxChars < 10 Then maxChars = 10

        Dim remaining As String = text.Replace(vbCr, " ").Replace(vbLf, " ")
        Do
            Dim part As String = remaining
            If part.Length > maxChars Then part = part.Substring(0, maxChars)
            remaining = remaining.Substring(part.Length)

            If y < Margin + size Then NewPage()
            cur.Append("BT /").Append(font).Append(" ").Append(size.ToString(inv)).Append(" Tf ")
            cur.Append(Margin.ToString(inv)).Append(" ").Append(y.ToString("0.##", inv))
            cur.Append(" Td (").Append(Esc(part)).Append(") Tj ET").Append(vbLf)
            y -= size * 1.45F
        Loop While remaining.Length > 0
    End Sub

    Private Shared Function Esc(s As String) As String
        Return s.Replace("\", "\\").Replace("(", "\(").Replace(")", "\)")
    End Function

    Public Sub Save(path As String)
        Dim enc As Encoding = Encoding.GetEncoding(1252)
        Using ms As New MemoryStream()
            Dim offsets As New List(Of Long)
            Dim n As Integer = pages.Count
            ' Numerotation : 1 catalogue, 2 pages, 3-5 polices, puis (page, contenu) par page
            WriteText(ms, enc, "%PDF-1.4" & vbLf)

            offsets.Add(ms.Position)
            WriteText(ms, enc, "1 0 obj" & vbLf & "<< /Type /Catalog /Pages 2 0 R >>" & vbLf & "endobj" & vbLf)

            Dim kids As New StringBuilder()
            For i As Integer = 0 To n - 1
                kids.Append(6 + i * 2).Append(" 0 R ")
            Next
            offsets.Add(ms.Position)
            WriteText(ms, enc, "2 0 obj" & vbLf & "<< /Type /Pages /Kids [" & kids.ToString() & "] /Count " & n & " >>" & vbLf & "endobj" & vbLf)

            Dim fonts As String() = {"Helvetica", "Helvetica-Bold", "Courier"}
            For i As Integer = 0 To 2
                offsets.Add(ms.Position)
                WriteText(ms, enc, (3 + i) & " 0 obj" & vbLf & "<< /Type /Font /Subtype /Type1 /BaseFont /" & fonts(i) &
                          " /Encoding /WinAnsiEncoding >>" & vbLf & "endobj" & vbLf)
            Next

            For i As Integer = 0 To n - 1
                Dim pageObj As Integer = 6 + i * 2
                Dim contObj As Integer = pageObj + 1
                offsets.Add(ms.Position)
                WriteText(ms, enc, pageObj & " 0 obj" & vbLf & "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " & PageW & " " & PageH &
                          "] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents " & contObj & " 0 R >>" & vbLf & "endobj" & vbLf)

                Dim body As String = pages(i).ToString()
                Dim bytes As Byte() = enc.GetBytes(body)
                offsets.Add(ms.Position)
                WriteText(ms, enc, contObj & " 0 obj" & vbLf & "<< /Length " & bytes.Length & " >>" & vbLf & "stream" & vbLf)
                ms.Write(bytes, 0, bytes.Length)
                WriteText(ms, enc, vbLf & "endstream" & vbLf & "endobj" & vbLf)
            Next

            Dim xref As Long = ms.Position
            WriteText(ms, enc, "xref" & vbLf & "0 " & (offsets.Count + 1) & vbLf)
            WriteText(ms, enc, "0000000000 65535 f " & vbLf)
            For Each o As Long In offsets
                WriteText(ms, enc, o.ToString("0000000000", inv) & " 00000 n " & vbLf)
            Next
            WriteText(ms, enc, "trailer" & vbLf & "<< /Size " & (offsets.Count + 1) & " /Root 1 0 R >>" & vbLf &
                      "startxref" & vbLf & xref & vbLf & "%%EOF" & vbLf)

            File.WriteAllBytes(path, ms.ToArray())
        End Using
    End Sub

    Private Shared Sub WriteText(ms As MemoryStream, enc As Encoding, s As String)
        Dim b As Byte() = enc.GetBytes(s)
        ms.Write(b, 0, b.Length)
    End Sub

End Class
