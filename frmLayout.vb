Imports System.Globalization

''' <summary>
''' Tableau de bord. Les additionneurs d'energie (TotalKV1, TotalKV2, TotalKV3, Total)
''' sont des composants iAdder poses dans frmLayout.Designer.vb : aucun code n'est
''' necessaire pour eux. Ce fichier fournit seulement la lecture des tags pour le rapport.
''' </summary>
Public Class frmLayout

    ''' Instance courante, utilisee par frmReport pour lire les valeurs en direct.
    Public Shared Current As frmLayout

    Public Sub New()
        InitializeComponent()
        Current = Me
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

End Class
