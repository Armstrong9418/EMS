Imports System.Collections.Generic
Imports System.Globalization
Imports ATSCADA
Imports ATSCADA.ToolExtensions.ExtensionMethods
Imports ATSCADA.ToolExtensions.TagCollection

''' <summary>
''' Tableau de bord. Les additionneurs d'énergie (TotalKV1 à TotalKV3 dans le TP)
''' sont réalisés ici par la classe EnergySummer, définie plus bas.
''' </summary>
Public Class frmLayout

    Private ReadOnly totals As New List(Of EnergySummer)

    Public Sub New()
        InitializeComponent()

        ' Énergie totale de chaque zone = somme des deux compteurs de la zone
        totals.Add(New EnergySummer(IDriver1, "EnergyTotalTotal.KV1",
                                    "KV1DH1.Energy", "KV1DH2.Energy"))
        totals.Add(New EnergySummer(IDriver1, "EnergyTotalTotal.KV2",
                                    "KV2DH3.Energy", "KV2DH4.Energy"))
        totals.Add(New EnergySummer(IDriver1, "EnergyTotalTotal.KV3",
                                    "KV3DH5.Energy", "KV3DH6.Energy"))

        ' Total général (additionneur proposé « TotalAll » dans le TP)
        totals.Add(New EnergySummer(IDriver1, "EnergyTotalTotal.Total",
                                    "EnergyTotalTotal.KV1",
                                    "EnergyTotalTotal.KV2",
                                    "EnergyTotalTotal.KV3"))
    End Sub

End Class

''' <summary>
''' Additionne la valeur de plusieurs tags et écrit le résultat dans un tag
''' de destination, à chaque changement d'un des tags sources.
''' </summary>
Friend Class EnergySummer

    Private ReadOnly _driver As iDriver
    Private ReadOnly _targetName As String
    Private ReadOnly _sourceNames As String()
    Private ReadOnly _sources As New List(Of ITag)
    Private _target As ITag

    Public Sub New(driver As iDriver, targetName As String, ParamArray sourceNames As String())
        _driver = driver
        _targetName = targetName
        _sourceNames = sourceNames
        ' Les tags ne sont accessibles qu'une fois la construction du pilote terminée
        AddHandler _driver.ConstructionCompleted, Sub() Connect()
    End Sub

    Private Sub Connect()
        _target = _driver.GetTagByName(_targetName)
        If _target Is Nothing Then Return

        For Each name As String In _sourceNames
            Dim tag As ITag = _driver.GetTagByName(name)
            If tag IsNot Nothing Then
                _sources.Add(tag)
                AddHandler tag.TagValueChanged, Sub() Recalculate()
            End If
        Next
    End Sub

    Private Sub Recalculate()
        Dim total As Double = 0
        For Each tag As ITag In _sources
            Dim value As Double
            If Double.TryParse(CStr(tag.Value), NumberStyles.Float,
                               CultureInfo.InvariantCulture, value) Then
                total += value
            End If
        Next
        _target.ASynWrite(total.ToString(CultureInfo.InvariantCulture))
    End Sub

End Class
