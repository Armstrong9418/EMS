Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports ATSCADA
Imports ATSCADA.ToolExtensions.ExtensionMethods
Imports ATSCADA.ToolExtensions.TagCollection

''' <summary>
''' Tableau de bord.
'''  - TmrSum : calcule les totaux d'energie (zones 1, 2, 3 et total general).
'''  - TmrLog : enregistre dans MySQL (tables Money_DH1..Money_DH6) l'energie et le cout
'''             de chaque compteur. Remplace les composants iThreeRatePriceLogger (sans licence).
''' </summary>
Public Class frmLayout

    ' ====== PARAMETRES D'ENREGISTREMENT (a modifier si besoin) ======
    Private Const LogIntervalSeconds As Integer = 60     ' frequence d'enregistrement
    Private Const PeakStartHour As Integer = 18          ' heures de pointe : 18h -> 22h
    Private Const PeakEndHour As Integer = 22
    Private Const OffPeakStartHour As Integer = 22       ' heures creuses : 22h -> 6h
    Private Const OffPeakEndHour As Integer = 6          ' (le reste de la journee = tarif standard)

    ' Compteur (DH) -> tache ATSCADA qui contient son tag "Power"
    Private Shared ReadOnly MoneyDevices As String(,) = New String(,) {
        {"DH1", "KV1DH1"}, {"DH2", "KV1DH2"},
        {"DH3", "KV2DH3"}, {"DH4", "KV2DH4"},
        {"DH5", "KV3DH5"}, {"DH6", "KV3DH6"}}

    ''' Instance courante, utilisee par frmReport pour lire les valeurs en direct.
    Public Shared Current As frmLayout

    ''' Dernier etat de l'enregistrement MySQL (visible dans la fenetre Sortie de Visual Studio).
    Public Shared MoneyLogStatus As String = "Pas encore d'enregistrement"

    Private WithEvents TmrSum As New System.Windows.Forms.Timer With {.Interval = 2000}
    Private WithEvents TmrLog As New System.Windows.Forms.Timer With {.Interval = LogIntervalSeconds * 1000}

    Private lastLog As DateTime = DateTime.MinValue
    Private logBusy As Integer = 0
    Private errorShown As Boolean = False
    Private Shared dbReady As Boolean = False

    Private Class MoneyRow
        Public Device As String
        Public TagName As String
        Public EnergyKwh As Double
        Public Cost As Double
    End Class

    Public Sub New()
        InitializeComponent()
        Current = Me
    End Sub

    Private Sub frmLayout_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If Me.DesignMode Then Return
        TmrSum.Start()
        TmrLog.Start()
        LogNow()   ' premier enregistrement immediat : cree la base, les tables et les premieres lignes
    End Sub

    ' ===================================================================
    '  1) TOTAUX D'ENERGIE
    ' ===================================================================
    Private Sub TmrSum_Tick(sender As Object, e As EventArgs) Handles TmrSum.Tick
        Dim kv1, kv2, kv3 As Double

        ' Chaque zone est calculee separement : une erreur sur l'une ne bloque pas les autres.
        Try
            kv1 = ReadTag("KV1DH1", "Energy") + ReadTag("KV1DH2", "Energy")
            WriteTag("EnergyTotalTotal", "KV1", kv1)
        Catch ex As Exception
            Debug.WriteLine("Zone 1 : " & ex.Message)
        End Try

        Try
            kv2 = ReadTag("KV2DH3", "Energy") + ReadTag("KV2DH4", "Energy")
            WriteTag("EnergyTotalTotal", "KV2", kv2)
        Catch ex As Exception
            Debug.WriteLine("Zone 2 : " & ex.Message)
        End Try

        Try
            kv3 = ReadTag("KV3DH5", "Energy") + ReadTag("KV3DH6", "Energy")
            WriteTag("EnergyTotalTotal", "KV3", kv3)
        Catch ex As Exception
            Debug.WriteLine("Zone 3 : " & ex.Message)
        End Try

        Try
            WriteTag("EnergyTotalTotal", "Total", kv1 + kv2 + kv3)
        Catch ex As Exception
            Debug.WriteLine("Total : " & ex.Message)
        End Try
    End Sub

    Private Function ReadTag(taskName As String, tagName As String) As Double
        Dim t = IDriver1.Task(taskName)
        If t Is Nothing Then Return 0
        Dim g = t.Tag(tagName)
        If g Is Nothing Then Return 0
        Dim v As String = CStr(g.Value)
        Dim d As Double
        If Double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, d) Then Return d
        If Double.TryParse(v, NumberStyles.Any, CultureInfo.CurrentCulture, d) Then Return d
        Return 0
    End Function

    Private Sub WriteTag(taskName As String, tagName As String, value As Double)
        Dim t = IDriver1.Task(taskName)
        If t Is Nothing Then Return
        Dim g = t.Tag(tagName)
        If g Is Nothing Then Return
        g.SynWrite(value.ToString("0.##", CultureInfo.InvariantCulture))
    End Sub

    ''' Lit un tag au format "Tache.Tag" (ex. "KV1DH1.Energy"). Renvoie "n/d" si indisponible.
    Public Function ReadTagText(fullName As String) As String
        Try
            Dim p As String() = fullName.Split("."c)
            If p.Length < 2 Then Return "n/d"
            Dim t = IDriver1.Task(p(0))
            If t Is Nothing Then Return "n/d"
            Dim g = t.Tag(p(1))
            If g Is Nothing Then Return "n/d"
            Dim v As Object = g.Value
            If v Is Nothing Then Return "n/d"
            Return CStr(v)
        Catch
            Return "n/d"
        End Try
    End Function

    ' ===================================================================
    '  2) ENREGISTREMENT MYSQL (energie + cout par compteur)
    '     Table Money_DHx, meme structure que celle des composants ATSCADA :
    '       une ligne par heure et par type, mise a jour a chaque enregistrement.
    '       data_type = 0 : energie consommee (kWh)
    '       data_type = 1 : cout (energie x tarif en vigueur)
    ' ===================================================================
    Private Sub TmrLog_Tick(sender As Object, e As EventArgs) Handles TmrLog.Tick
        LogNow()
    End Sub

    Private Function CurrentRateName(hour As Integer) As String
        If hour >= PeakStartHour AndAlso hour < PeakEndHour Then Return "PeakHourRate"
        If hour >= OffPeakStartHour OrElse hour < OffPeakEndHour Then Return "OffPeakHourRate"
        Return "StandardHourRate"
    End Function

    Private Sub LogNow()
        If Interlocked.Exchange(logBusy, 1) = 1 Then Return   ' un enregistrement est deja en cours

        Dim rows As New List(Of MoneyRow)()
        Dim stamp As DateTime = DateTime.Now
        Try
            Dim hours As Double = 0
            If lastLog <> DateTime.MinValue Then hours = (stamp - lastLog).TotalSeconds / 3600.0
            lastLog = stamp

            Dim rate As Double = 0
            Try
                rate = ReadTag("MoneyPriceRate", CurrentRateName(stamp.Hour))
            Catch
            End Try

            For i As Integer = 0 To MoneyDevices.GetLength(0) - 1
                Dim power As Double = 0
                Try
                    power = ReadTag(MoneyDevices(i, 1), "Power")   ' kW
                Catch
                End Try
                Dim kwh As Double = power * hours
                Dim r As New MoneyRow()
                r.Device = MoneyDevices(i, 0)
                r.TagName = MoneyDevices(i, 1) & ".Power"
                r.EnergyKwh = kwh
                r.Cost = kwh * rate
                rows.Add(r)
            Next
        Catch ex As Exception
            Interlocked.Exchange(logBusy, 0)
            MoneyLogStatus = "Lecture des tags impossible : " & ex.Message
            Debug.WriteLine(MoneyLogStatus)
            Return
        End Try

        ' L'ecriture MySQL se fait en arriere-plan pour ne jamais figer l'interface.
        System.Threading.Tasks.Task.Run(Sub() WriteMoneyRows(stamp, rows))
    End Sub

    Private Sub WriteMoneyRows(stamp As DateTime, rows As List(Of MoneyRow))
        Try
            EnsureMoneyDb()
            Dim bucket As New DateTime(stamp.Year, stamp.Month, stamp.Day, stamp.Hour, 0, 0)
            Using conn As IDbConnection = OpenMoneyConnection(True)
                For Each r As MoneyRow In rows
                    UpsertMoney(conn, r.Device, r.TagName, bucket, 0, r.EnergyKwh)
                    UpsertMoney(conn, r.Device, r.TagName, bucket, 1, r.Cost)
                Next
            End Using
            MoneyLogStatus = "Enregistrement OK : " & stamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            Debug.WriteLine(MoneyLogStatus)
        Catch ex As Exception
            MoneyLogStatus = "Erreur MySQL : " & ex.Message
            Debug.WriteLine(MoneyLogStatus)
            ShowErrorOnce(MoneyLogStatus)
        Finally
            Interlocked.Exchange(logBusy, 0)
        End Try
    End Sub

    Private Sub ShowErrorOnce(message As String)
        If errorShown Then Return
        errorShown = True
        Try
            If Me.IsHandleCreated Then
                Dim texte As String = message & vbCrLf & vbCrLf &
                    "Verifiez que MySQL est demarre et que DbConfig.vb contient le bon mot de passe."
                Me.BeginInvoke(Sub() MessageBox.Show(texte, "Enregistrement MySQL",
                                                     MessageBoxButtons.OK, MessageBoxIcon.Warning))
            End If
        Catch
        End Try
    End Sub

    Private Shared Sub EnsureMoneyDb()
        If dbReady Then Return
        Using c As IDbConnection = OpenMoneyConnection(False)
            ExecMoney(c, "CREATE DATABASE IF NOT EXISTS `" & DbConfig.Database & "`")
        End Using
        Using c As IDbConnection = OpenMoneyConnection(True)
            For i As Integer = 1 To 6
                ExecMoney(c, "CREATE TABLE IF NOT EXISTS `Money_DH" & i & "` (" &
                    "`start_time` datetime NOT NULL, `end_time` datetime NOT NULL, " &
                    "`group_name` varchar(200) NOT NULL, `device_name` varchar(200) NOT NULL, " &
                    "`device_tag_name` varchar(200) NOT NULL, `value` double NOT NULL DEFAULT 0, " &
                    "`direction` tinyint NOT NULL DEFAULT 0, `data_type` tinyint NOT NULL DEFAULT 0, " &
                    "`time_unit` tinyint NOT NULL DEFAULT 0, " &
                    "PRIMARY KEY (`start_time`, `end_time`, `group_name`, `device_name`, " &
                    "`device_tag_name`, `direction`, `data_type`, `time_unit`))")
            Next
        End Using
        dbReady = True
    End Sub

    Private Shared Sub UpsertMoney(conn As IDbConnection, device As String, tagName As String,
                                   bucket As DateTime, dataType As Integer, value As Double)
        Using cmd As IDbCommand = conn.CreateCommand()
            cmd.CommandText = "INSERT INTO `Money_" & device & "` " &
                "(`start_time`, `end_time`, `group_name`, `device_name`, `device_tag_name`, " &
                "`value`, `direction`, `data_type`, `time_unit`) " &
                "VALUES (@st, @et, @g, @d, @tag, @v, 1, @dt, 1) " &
                "ON DUPLICATE KEY UPDATE `value` = `value` + VALUES(`value`)"
            AddParam(cmd, "@st", bucket)
            AddParam(cmd, "@et", bucket.AddHours(1))
            AddParam(cmd, "@g", "Group")
            AddParam(cmd, "@d", device)
            AddParam(cmd, "@tag", tagName)
            AddParam(cmd, "@v", value)
            AddParam(cmd, "@dt", dataType)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Shared Sub AddParam(cmd As IDbCommand, name As String, value As Object)
        Dim p As IDbDataParameter = cmd.CreateParameter()
        p.ParameterName = name
        p.Value = value
        cmd.Parameters.Add(p)
    End Sub

    Private Shared Sub ExecMoney(conn As IDbConnection, sql As String)
        Using cmd As IDbCommand = conn.CreateCommand()
            cmd.CommandText = sql
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Shared Function OpenMoneyConnection(withDatabase As Boolean) As IDbConnection
        Dim asm As Assembly = LoadMySqlAssembly()
        Dim connType As Type = asm.GetType("MySql.Data.MySqlClient.MySqlConnection", True)
        Dim cs As String
        If withDatabase Then
            cs = DbConfig.ConnectionString
        Else
            cs = "Server=" & DbConfig.Server & ";Uid=" & DbConfig.UserId &
                 ";Pwd=" & DbConfig.Password & ";SslMode=none;Connection Timeout=5;"
        End If
        Dim conn As IDbConnection = CType(Activator.CreateInstance(connType, New Object() {cs}), IDbConnection)
        conn.Open()
        Return conn
    End Function

    Private Shared Function LoadMySqlAssembly() As Assembly
        For Each a As Assembly In AppDomain.CurrentDomain.GetAssemblies()
            If a.GetName().Name = "MySql.Data" Then Return a
        Next
        Try
            Return Assembly.Load("MySql.Data")
        Catch
            Return Assembly.LoadFrom(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MySql.Data.dll"))
        End Try
    End Function

End Class
