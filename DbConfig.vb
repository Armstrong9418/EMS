''' <summary>
''' Parametres MySQL (memes valeurs que les composants iThreeRatePriceLogger).
''' Modifier ici si votre mot de passe ou votre serveur sont differents.
''' </summary>
Friend Module DbConfig
    Public Const Server As String = "localhost"
    Public Const Database As String = "ems"
    Public Const UserId As String = "root"
    Public Const Password As String = "femgne2005"

    Public ReadOnly Property ConnectionString As String
        Get
            Return "Server=" & Server & ";Database=" & Database & ";Uid=" & UserId &
                   ";Pwd=" & Password & ";SslMode=none;Connection Timeout=5;"
        End Get
    End Property
End Module
