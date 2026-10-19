Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Fenêtre principale : en-tête (pnlHeader), menu latéral (pnlMenu),
''' zone de contenu (pnlMain) et pied de page (pnlFooter).
''' Les formulaires frmLayout, frmReport et frmSettings s'affichent dans pnlMain.
''' </summary>
Public Class MainForm

    ' Largeurs du menu latéral (valeurs du document source)
    Private Const MenuExpandedWidth As Integer = 129   ' icônes + textes
    Private Const MenuCollapsedWidth As Integer = 43   ' icônes seules

    ' Couleur de fond du bouton actif (valeur du document source)
    Private Shared ReadOnly ActiveButtonColor As Color =
        Color.FromArgb(73, 78, 83)

    Private activeButton As Button   ' bouton de menu actuellement sélectionné
    Private isHide As Boolean        ' True = menu replié

    ' Formulaires enfants, créés une seule fois
    Private layoutForm As frmLayout
    Private reportForm As frmReport
    Private settingsForm As frmSettings

    Public Sub New()
        ' Crée les contrôles définis dans le concepteur (MainForm.Designer.vb)
        InitializeComponent()
        Init()
    End Sub

    Private Sub Init()
        ' État initial cohérent avec le concepteur : menu déployé
        pnlMenu.Width = MenuExpandedWidth
        isHide = False

        ' Le survol d'un bouton conserve sa couleur de fond courante
        ConfigureHover(btnLayout)
        ConfigureHover(btnReport)
        ConfigureHover(btnSettings)
    End Sub

    ' Premier affichage de la fenêtre (et non l'événement Paint, déclenché
    ' à chaque repeinture) : création des pages, puis page Layout.
    Private Sub MainForm_Shown(sender As Object, e As EventArgs) _
            Handles Me.Shown
        PreloadPages()
        NavigateToLayout()
    End Sub

    Private Sub ConfigureHover(button As Button)
        button.FlatAppearance.MouseOverBackColor = button.BackColor
        AddHandler button.BackColorChanged, AddressOf MenuButton_BackColorChanged
    End Sub

    Private Sub MenuButton_BackColorChanged(sender As Object, e As EventArgs)
        Dim button As Button = DirectCast(sender, Button)
        button.FlatAppearance.MouseOverBackColor = button.BackColor
    End Sub

    ''' <summary>
    ''' Exécute une action sur le thread de l'interface graphique.
    ''' Utile si l'appel provient d'un autre thread (minuterie, tâche...).
    ''' </summary>
    Private Sub RunOnUiThread(action As Action)
        If InvokeRequired Then
            Invoke(action)
        Else
            action.Invoke()
        End If
    End Sub

    ' ---------------------------------------------------------------
    ' Menu latéral : déployer / replier
    ' ---------------------------------------------------------------
    Private Sub BtnMenu_Click(sender As Object, e As EventArgs) _
            Handles btnMenu.Click
        RunOnUiThread(AddressOf ToggleMenu)
    End Sub

    Private Sub ToggleMenu()
        If isHide Then
            ExpandMenu()
        Else
            CollapseMenu()
        End If
    End Sub

    Private Sub ExpandMenu()
        pnlMenu.Width = MenuExpandedWidth
        isHide = False
    End Sub

    Private Sub CollapseMenu()
        pnlMenu.Width = MenuCollapsedWidth
        isHide = True
    End Sub

    ' ---------------------------------------------------------------
    ' Boutons de navigation
    ' ---------------------------------------------------------------
    Private Sub btnDashboard_Click(sender As Object, e As EventArgs) _
            Handles btnLayout.Click
        If activeButton Is btnLayout Then Return   ' déjà sur la page
        RunOnUiThread(AddressOf NavigateToLayout)
    End Sub

    Private Sub btnReport_Click(sender As Object, e As EventArgs) _
            Handles btnReport.Click
        If activeButton Is btnReport Then Return
        RunOnUiThread(AddressOf NavigateToReport)
    End Sub

    Private Sub btnSettings_Click(sender As Object, e As EventArgs) _
            Handles btnSettings.Click
        If activeButton Is btnSettings Then Return
        RunOnUiThread(AddressOf NavigateToSettings)
    End Sub

    ' ---------------------------------------------------------------
    ' Navigation : afficher un formulaire enfant dans pnlMain
    ' ---------------------------------------------------------------
    Private Sub NavigateToLayout()
        If layoutForm Is Nothing Then
            layoutForm = CreateChildForm(New frmLayout())
        End If
        ShowPage(btnLayout, "Layout", layoutForm)
    End Sub

    Private Sub NavigateToReport()
        If reportForm Is Nothing Then
            reportForm = CreateChildForm(New frmReport())
        End If
        ShowPage(btnReport, "Report", reportForm)
    End Sub

    Private Sub NavigateToSettings()
        If settingsForm Is Nothing Then
            settingsForm = CreateChildForm(New frmSettings())
        End If
        ShowPage(btnSettings, "Settings", settingsForm)
    End Sub

    ''' <summary>
    ''' Crée les trois pages dès le démarrage : les composants ATSCADA
    ''' qu'elles contiennent (iWebService, iThreeRatePriceLogger...) sont
    ''' ainsi actifs même si l'utilisateur n'a pas encore ouvert la page.
    ''' </summary>
    Private Sub PreloadPages()
        If settingsForm Is Nothing Then
            settingsForm = CreateChildForm(New frmSettings())
        End If
        If reportForm Is Nothing Then
            reportForm = CreateChildForm(New frmReport())
        End If
        If layoutForm Is Nothing Then
            layoutForm = CreateChildForm(New frmLayout())
        End If
    End Sub

    ''' <summary>
    ''' Prépare un formulaire pour qu'il s'affiche À L'INTÉRIEUR de pnlMain.
    ''' </summary>
    Private Function CreateChildForm(Of T As Form)(page As T) As T
        page.TopLevel = False                    ' non indépendant
        page.FormBorderStyle = Windows.Forms.FormBorderStyle.None
        page.Dock = DockStyle.Fill               ' occupe tout pnlMain
        AddHandler page.Click, AddressOf ChildForm_Click
        pnlMain.Controls.Add(page)
        page.Show()
        Return page
    End Function

    ' Un clic dans la page affichée replie le menu latéral
    Private Sub ChildForm_Click(sender As Object, e As EventArgs)
        CollapseMenu()
    End Sub

    ''' <summary>
    ''' Met à jour le titre et la couleur des boutons, puis place la page
    ''' demandée au premier plan.
    ''' </summary>
    Private Sub ShowPage(button As Button, title As String, page As Form)
        activeButton = button
        lblNavigate.Text = title

        btnLayout.BackColor = ButtonColor(button Is btnLayout)
        btnReport.BackColor = ButtonColor(button Is btnReport)
        btnSettings.BackColor = ButtonColor(button Is btnSettings)

        page.BringToFront()
    End Sub

    Private Shared Function ButtonColor(isActive As Boolean) As Color
        If isActive Then Return ActiveButtonColor
        Return Color.Transparent
    End Function

    ' ---------------------------------------------------------------
    ' Boutons de l'en-tête et lien du pied de page
    ' ---------------------------------------------------------------
    Private Sub btnMinimize_Click(sender As Object, e As EventArgs) _
            Handles btnMinimize.Click
        WindowState = FormWindowState.Minimized
    End Sub

    Private Sub btnClose_Click(sender As Object, e As EventArgs) _
            Handles btnClose.Click
        Dim result As DialogResult = MessageBox.Show(
            "Voulez-vous vraiment quitter l'application ?",
            "Quitter",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question)

        If result = Windows.Forms.DialogResult.Yes Then Close()
    End Sub

    Private Sub lblATSCADA_Click(sender As Object, e As EventArgs) _
            Handles lblATSCADA.Click
        System.Diagnostics.Process.Start("https://atscada.com/")
    End Sub

End Class
