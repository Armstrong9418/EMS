Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports System.Windows.Forms

''' <summary>
''' Page Rapport : affiche les enregistrements de cout (tables MySQL Money_DH1..6)
''' et genere un rapport PDF (energies en direct + derniers enregistrements).
''' L'interface est creee par le code, aucun controle a poser dans le concepteur.
''' </summary>
Public Class frmReport

    Private ReadOnly cboTable As New ComboBox()
    Private ReadOnly grid As New DataGridView()
    Private ReadOnly btnRefresh As New Button()
    Private ReadOnly btnPdf As New Button()
    Private ReadOnly lblStatus As New Label()

    Private Sub frmReport_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Me.Text = "Rapport"
        Me.BackColor = System.Drawing.Color.White

        Dim top As New Panel() With {.Dock = DockStyle.Top, .Height = 46}
        Dim lbl As New Label() With {.Text = "Table :", .AutoSize = True, .Left = 12, .Top = 15}
        cboTable.DropDownStyle = ComboBoxStyle.DropDownList
        cboTable.SetBounds(65, 11, 130, 24)
        For i As Integer = 1 To 6
            cboTable.Items.Add("Money_DH" & i)
        Next
        cboTable.SelectedIndex = 0
        btnRefresh.Text = "Actualiser"
        btnRefresh.SetBounds(210, 9, 100, 28)
        btnPdf.Text = "Generer le rapport PDF"
        btnPdf.SetBounds(320, 9, 190, 28)
        top.Controls.AddRange(New Control() {lbl, cboTable, btnRefresh, btnPdf})

        lblStatus.Dock = DockStyle.Bottom
        lblStatus.Height = 24
        lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft

        grid.Dock = DockStyle.Fill
        grid.ReadOnly = True
        grid.AllowUserToAddRows = False
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells

        Me.Controls.Add(grid)
        Me.Controls.Add(top)
        Me.Controls.Add(lblStatus)

        AddHandler btnRefresh.Click, AddressOf OnRefresh
        AddHandler btnPdf.Click, AddressOf OnPdf
        AddHandler cboTable.SelectedIndexChanged, AddressOf OnRefresh

        OnRefresh(Nothing, EventArgs.Empty)
    End Sub

    Private Sub OnRefresh(sender As Object, e As EventArgs)
        Try
            grid.DataSource = QueryTable(CStr(cboTable.SelectedItem), 200)
            lblStatus.Text = "  " & grid.Rows.Count & " ligne(s) dans " & CStr(cboTable.SelectedItem)
        Catch ex As Exception
            grid.DataSource = Nothing
            lblStatus.Text = "  Base de donnees indisponible : " & ex.Message
        End Try
    End Sub

    ''' Lit une table MySQL (colonnes inconnues = lecture generique), via MySql.Data charge par reflexion.
    Private Shared Function QueryTable(tableName As String, maxRows As Integer) As DataTable
        Dim asm As Assembly = LoadMySql()
        Dim connType As Type = asm.GetType("MySql.Data.MySqlClient.MySqlConnection", True)
        Dim adapType As Type = asm.GetType("MySql.Data.MySqlClient.MySqlDataAdapter", True)

        Dim conn As IDbConnection = CType(Activator.CreateInstance(connType, New Object() {DbConfig.ConnectionString}), IDbConnection)
        Try
            conn.Open()
            Dim sql As String = "SELECT * FROM `" & tableName & "` ORDER BY 1 DESC LIMIT " & maxRows
            Dim adapter As DbDataAdapter = CType(Activator.CreateInstance(adapType, New Object() {sql, conn}), DbDataAdapter)
            Dim dt As New DataTable(tableName)
            adapter.Fill(dt)
            Return dt
        Finally
            conn.Dispose()
        End Try
    End Function

    Private Shared Function LoadMySql() As Assembly
        For Each a As Assembly In AppDomain.CurrentDomain.GetAssemblies()
            If a.GetName().Name = "MySql.Data" Then Return a
        Next
        Try
            Return Assembly.Load("MySql.Data")
        Catch
            Return Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MySql.Data.dll"))
        End Try
    End Function

    Private Sub OnPdf(sender As Object, e As EventArgs)
        Using dlg As New SaveFileDialog()
            dlg.Filter = "Fichier PDF (*.pdf)|*.pdf"
            dlg.FileName = "Rapport_EMS_" & DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) & ".pdf"
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                BuildPdf(dlg.FileName)
                lblStatus.Text = "  Rapport enregistre : " & dlg.FileName
                If MessageBox.Show("Rapport genere. Ouvrir le fichier ?", "Rapport EMS",
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                    Process.Start(New ProcessStartInfo(dlg.FileName) With {.UseShellExecute = True})
                End If
            Catch ex As Exception
                MessageBox.Show("Echec de la generation du PDF : " & ex.Message, "Rapport EMS",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Sub BuildPdf(path As String)
        Dim pdf As New SimplePdf()
        pdf.AddLine("RAPPORT DE GESTION D'ENERGIE - EMS", 18, "F2")
        pdf.AddLine("Genere le " & DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture), 10)
        pdf.Space(12)

        ' 1. Valeurs en direct
        pdf.AddLine("1. Energies et puissances en direct (kWh / kW)", 13, "F2")
        pdf.Space(4)
        Dim layout As frmLayout = frmLayout.Current
        If layout Is Nothing Then
            pdf.AddLine("Tableau de bord non charge : valeurs en direct indisponibles.", 10)
        Else
            Dim dh As String() = {"KV1DH1", "KV1DH2", "KV2DH3", "KV2DH4", "KV3DH5", "KV3DH6"}
            pdf.AddLine(Pad("Compteur", 12) & Pad("Energie", 16) & "Puissance", 10, "F3")
            For Each d As String In dh
                pdf.AddLine(Pad(d, 12) & Pad(layout.ReadTagText(d & ".Energy"), 16) & layout.ReadTagText(d & ".Power"), 10, "F3")
            Next
            pdf.Space(6)
            pdf.AddLine(Pad("Total zone KV1", 18) & layout.ReadTagText("EnergyTotalTotal.KV1"), 10, "F3")
            pdf.AddLine(Pad("Total zone KV2", 18) & layout.ReadTagText("EnergyTotalTotal.KV2"), 10, "F3")
            pdf.AddLine(Pad("Total zone KV3", 18) & layout.ReadTagText("EnergyTotalTotal.KV3"), 10, "F3")
            pdf.AddLine(Pad("TOTAL GENERAL", 18) & layout.ReadTagText("EnergyTotalTotal.Total"), 10, "F3")
        End If
        pdf.Space(14)

        ' 2. Derniers enregistrements de cout
        pdf.AddLine("2. Derniers enregistrements de cout (10 par table)", 13, "F2")
        For i As Integer = 1 To 6
            Dim tn As String = "Money_DH" & i
            pdf.Space(6)
            pdf.AddLine(tn, 11, "F2")
            Try
                Dim dt As DataTable = QueryTable(tn, 10)
                If dt.Rows.Count = 0 Then
                    pdf.AddLine("(aucun enregistrement)", 10)
                Else
                    Dim head As New System.Text.StringBuilder()
                    For Each c As DataColumn In dt.Columns
                        head.Append(Pad(c.ColumnName, 20))
                    Next
                    pdf.AddLine(head.ToString(), 8, "F3")
                    For Each r As DataRow In dt.Rows
                        Dim line As New System.Text.StringBuilder()
                        For Each c As DataColumn In dt.Columns
                            line.Append(Pad(Convert.ToString(r(c), CultureInfo.InvariantCulture), 20))
                        Next
                        pdf.AddLine(line.ToString(), 8, "F3")
                    Next
                End If
            Catch ex As Exception
                pdf.AddLine("Table indisponible : " & ex.Message, 10)
            End Try
        Next

        pdf.Save(path)
    End Sub

    Private Shared Function Pad(s As String, width As Integer) As String
        If s Is Nothing Then s = ""
        If s.Length > width - 1 Then s = s.Substring(0, width - 1)
        Return s.PadRight(width)
    End Function

End Class
