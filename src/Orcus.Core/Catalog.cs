namespace Orcus.Core;

public enum Profile { Normal, Media, Ultra, UltraMax }
public enum Usage { Diario, Gaming, Programacion, Diseno }
public enum TargetKind { Service, Registry }
public sealed record Tweak(string Id, string Title, string Category, string Description,
    string Impact, TargetKind Kind, string Path, string ValueName, int Desired,
    int Level = 99, bool Developer = false, bool ProOnly = false)
{
    public string Target => Kind == TargetKind.Service ? $"Servicio: {Path} → Deshabilitado (próximo inicio)" : $"{Path} · {ValueName} = {Desired}";
    public string Risk => Developer ? "EXPERIMENTAL" : Level >= 2 ? "REVISAR COMPATIBILIDAD" : "REVERSIBLE";
}

public static class Catalog
{
    // Defense in depth: nothing supplied by a UI, import or backup may expand this list.
    public static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "PlugPlay", "Power", "Dhcp", "Dnscache", "nsi", "Winmgmt",
        "CryptSvc", "BFE", "mpssvc", "EventLog", "Schedule", "AudioEndpointBuilder", "Audiosrv",
        "msiserver", "TrustedInstaller", "BITS", "AppReadiness", "AppXSvc", "Appinfo",
        "BrokerInfrastructure", "CoreMessagingRegistrar", "StateRepository", "ClipSVC", "InstallService",
        "ProfSvc", "UserManager", "LSM", "SamSs", "gpsvc", "seclogon", "RpcEptMapper", "KeyIso",
        "VaultSvc", "NgcSvc", "NgcCtnrSvc", "wlidsvc", "TokenBroker", "EventSystem", "SENS",
        "SystemEventsBroker", "TimeBrokerSvc", "camsvc", "DsSvc", "DusmSvc", "DeviceInstall",
        "DsmSvc", "DevQueryBroker", "NetSetupSvc", "Netman", "LanmanWorkstation", "W32Time",
        "WaaSMedicSvc", "UsoSvc", "DoSvc", "WpnService", "SecurityHealthService", "wscsvc",
        "WinDefend", "WdNisSvc", "sppsvc", "BDESVC", "EFS", "FontCache", "SysMain", "WerSvc",
        "Wecsvc", "RemoteRegistry", "WinHttpAutoProxySvc", "WlanSvc", "Netlogon", "iphlpsvc",
        "RasMan", "EapHost", "WbioSrvc", "TabletInputService"
    };
    public static IReadOnlyList<Tweak> All { get; } = Build();
    public static Tweak Get(string id) => All.SingleOrDefault(x => x.Id == id)
        ?? throw new InvalidOperationException($"Opción fuera del catálogo: {id}");

    public static bool DefaultSelected(Tweak t, Profile p, Usage usage)
    {
        if (t.Developer) return false;
        if (usage == Usage.Gaming && t.Id == "game-mode") return true;
        if (t.Level > (int)p) return false;
        if (usage == Usage.Programacion && t.Id is "svc-WSearch" or "svc-WebClient") return false;
        if (usage == Usage.Diseno && t.Id is "svc-WSearch" or "dvr") return false;
        if (usage == Usage.Gaming && t.Id == "game-mode") return true;
        return true;
    }

    private static List<Tweak> Build()
    {
        var a = new List<Tweak>();
        void S(string name, string title, string impact, int level = 99, string group = "Servicios") =>
            a.Add(new("svc-" + name, title, group, "Evita que este servicio se cargue en próximos inicios. Puede reducir actividad cuando la función estaba en uso; no garantiza ahorro de RAM.", impact, TargetKind.Service, name, "Start", 4, level));
        void R(string id, string title, string group, string description, string impact, string path, string value, int desired, int level = 99, bool dev = false, bool pro = false) =>
            a.Add(new(id, title, group, description, impact, TargetKind.Registry, path, value, desired, level, dev, pro));
        S("DiagTrack", "Reducir telemetría del sistema", "Deja de enviar parte de los diagnósticos. No elimina toda la telemetría de Windows.", 0, "Privacidad");
        S("MapsBroker", "Mapas sin conexión", "Las aplicaciones no podrán acceder a mapas descargados.", 0);
        S("RetailDemo", "Modo de demostración", "Desactiva la experiencia de exhibición para tiendas.", 0);
        S("Fax", "Servicio de fax", "Se pierde el envío y recepción de fax.", 0);
        S("WMPNetworkSvc", "Biblioteca multimedia compartida", "Windows Media no compartirá su biblioteca por red.", 0);
        S("wisvc", "Windows Insider", "No seleccionar si recibes versiones Insider.", 99);
        S("WalletService", "Cartera digital", "Funciones de cartera no disponibles.", 0);
        S("SEMgrSvc", "Pagos y NFC", "Puede afectar pagos con NFC.");
        S("PhoneSvc", "Telefonía", "Puede afectar integración telefónica.");
        S("PeerDistSvc", "BranchCache", "Se pierde caché de contenido empresarial.");
        S("RpcLocator", "Localizador RPC heredado", "Puede afectar aplicaciones antiguas que usan este localizador.");
        S("PNRPsvc", "Nombres P2P heredados", "Afecta aplicaciones P2P antiguas.", 1);
        S("p2psvc", "Agrupación P2P heredada", "Se pierde agrupación de pares de Windows.", 1);
        S("p2pimsvc", "Identidades P2P", "Afecta identidades en redes P2P antiguas.", 1);
        S("PNRPAutoReg", "Publicación de nombres P2P", "No publica el nombre PNRP del equipo.", 1);
        S("AJRouter", "AllJoyn", "Dejan de funcionar dispositivos que requieren AllJoyn.");
        S("TrkWks", "Seguimiento de vínculos NTFS", "Accesos a archivos movidos pueden dejar de actualizarse.", 1);
        S("PushToInstall", "Instalaciones remotas de Store", "No podrás enviar instalaciones de Store a este equipo.", 1);
        S("CscService", "Archivos de red sin conexión", "Pierdes acceso offline a recursos de red.");
        S("WebClient", "Acceso WebDAV", "Puede romper unidades WebDAV y flujos de trabajo.");
        S("WwanSvc", "Datos móviles LTE / 5G", "No habrá conexión por módem celular.");
        S("wlpasvc", "Perfiles SIM", "Afecta perfiles de SIM y conectividad celular.");
        S("autotimesvc", "Hora desde red celular", "No obtiene la hora desde la red móvil.");
        S("icssvc", "Punto de acceso móvil", "No podrás compartir conexión mediante hotspot.");
        // SharedAccess is deliberately excluded: WSL2/Docker/HNS can depend on it.
        S("SSDPSRV", "Descubrimiento UPnP", "Puede perderse descubrimiento de televisores y dispositivos.");
        S("upnphost", "Host UPnP", "Afecta alojamiento de dispositivos UPnP.");
        S("FDResPub", "Publicar el equipo en la red", "Otros equipos podrían no descubrir sus carpetas e impresoras.");
        S("fdPHost", "Descubrir recursos de red", "Puede afectar impresoras y recursos compartidos.");
        S("lltdsvc", "Mapa de red", "No participa en mapas de topología de red.", 1);
        S("Wcncsvc", "Configuración WPS", "No podrás configurar dispositivos usando WPS.");
        S("workfolderssvc", "Work Folders", "Detiene sincronización empresarial de carpetas.");
        S("SmsRouter", "Mensajes SMS", "No enruta mensajes SMS del equipo.");
        S("spectrum", "Percepción espacial", "Puede afectar dispositivos de realidad mixta.");
        S("perceptionsimulation", "Simulación espacial", "Afecta pruebas de realidad mixta.");
        S("VacSvc", "Audio de realidad mixta", "Puede afectar audio espacial de Mixed Reality.");
        S("shpamsvc", "Administración de Shared PC", "No usar en equipos compartidos o administrados.");
        S("WpcMonSvc", "Controles parentales", "Puede dejar sin supervisión cuentas infantiles. Solo selección manual.");
        S("RmSvc", "Administración de radios", "Puede afectar modo avión y radios inalámbricos.");
        S("TapiSrv", "Telefonía heredada", "Puede afectar llamadas y módems.");
        S("SNMPTRAP", "Monitorización SNMP", "No recibirá notificaciones SNMP.");
        // Storage, sign-in, Wi-Fi and VPN services are not offered as optimizations.
        S("WSearch", "Pausar la indexación de búsqueda", "Buscar archivos y correo será más lento. Conservar si dependes de Outlook o índices.", 2, "Memoria y búsqueda");
        S("PcaSvc", "Compatibilidad de aplicaciones antiguas", "No detectará ni corregirá ciertos fallos de programas antiguos.", 2);
        S("NcdAutoSetup", "Configuración de dispositivos de red", "Se pierde instalación automática de algunos dispositivos de red.");
        S("AppMgmt", "Despliegue empresarial", "Puede impedir instalación de software mediante directivas.");
        S("EntAppSvc", "Aplicaciones empresariales", "No usar con aplicaciones administradas por una organización.");
        S("AxInstSV", "Controles ActiveX", "No instala controles ActiveX heredados.", 2);
        S("NcaSvc", "Estado de DirectAccess", "Afecta conectividad y diagnóstico empresarial.");
        S("lmhosts", "Nombres NetBIOS", "Afecta recursos SMB heredados y algunas redes.");
        S("QWAVE", "QoS multimedia", "Puede afectar streaming multimedia por red.");
        S("lfsvc", "Ubicación del dispositivo", "Dejan de funcionar geocercas y ubicación para algunas apps.", 2, "Privacidad");
        S("NcbService", "Conexiones de aplicaciones Store", "Puede afectar conectividad y notificaciones de apps. Revisar por separado.");
        S("WPDBusEnum", "Dispositivos portátiles", "Puede afectar importación y sincronización con teléfonos.");
        S("NaturalAuthentication", "Bloqueo dinámico", "Windows no se bloqueará automáticamente al alejar tu teléfono.");
        S("WFDSConMgrSvc", "Pantalla y docks inalámbricos", "Puede afectar Miracast, Wi-Fi Direct y docks.");
        S("DeviceAssociationService", "Asociación de dispositivos", "No podrás emparejar algunos dispositivos nuevos.");
        S("CDPSvc", "Experiencias entre dispositivos", "Afecta funciones compartidas con dispositivos cercanos.", 3);
        S("ShellHWDetection", "Reproducción automática", "No mostrará ciertos avisos al insertar medios o conectar dispositivos.", 3);
        foreach (var (n, title, impact) in new[] {
            ("Spooler", "Cola de impresión", "No podrás imprimir, incluido Imprimir a PDF."),
            ("PrintNotify", "Notificaciones de impresoras", "Se pierden avisos y extensiones de impresoras."),
            ("bthserv", "Bluetooth", "Dejarán de funcionar detección y asociación Bluetooth."),
            ("BTAGService", "Audio manos libres Bluetooth", "Puede dejar de funcionar el micrófono de audífonos Bluetooth."),
            ("BthAvctpSvc", "Controles de audio Bluetooth", "Puede afectar controles y reproducción Bluetooth."),
            ("FrameServer", "Cámara compartida", "Puede impedir videollamadas y uso de webcam."),
            ("stisvc", "Cámaras y escáneres", "Puede impedir captura e importación de imágenes."),
            ("WiaRpc", "Eventos de escáner", "Puede afectar flujos de escaneo."),
            ("SensorDataService", "Datos de sensores", "Se pierden datos de sensores."),
            ("SensrSvc", "Brillo adaptativo", "Puede dejar de funcionar el brillo automático."),
            ("SensorService", "Orientación de pantalla", "Se pierde autorrotación."),
            ("LanmanServer", "Compartir carpetas e impresoras", "Esta PC dejará de compartir por SMB; puede afectar herramientas de desarrollo."),
            ("XboxGipSvc", "Accesorios Xbox", "Puede afectar controles Xbox."),
            ("XblAuthManager", "Inicio de sesión Xbox", "Puede romper Xbox Live y Game Pass."),
            ("XblGameSave", "Partidas Xbox en la nube", "Se pierde sincronización de partidas."),
            ("XboxNetApiSvc", "Red Xbox Live", "Puede afectar juegos en línea de Xbox.") }) S(n, title, impact, 99, "Hardware y funciones opcionales");
        R("transparency", "Reducir transparencias", "Apariencia", "Desactiva transparencias de Windows; reduce composición visual.", "Cambia la apariencia. No modifica fuentes ni miniaturas.", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 0);
        R("suggestions", "Reducir sugerencias de contenido", "Privacidad", "Desactiva parte del contenido sugerido en Windows.", "La aplicación del ajuste varía con la versión de Windows.", @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0, 0);
        R("dvr", "Desactivar grabación de Game DVR", "Gaming", "Desactiva Game DVR para el usuario actual.", "Las capturas de Xbox pueden dejar de estar disponibles; OBS es independiente.", @"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0, 1);
        R("game-mode", "Activar Modo Juego", "Gaming", "Permite que Windows priorice la experiencia de juego.", "No garantiza un incremento de FPS.", @"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
        R("widgets", "Ocultar Widgets en la barra", "Apariencia", "Oculta el acceso a Widgets en versiones que respetan TaskbarDa.", "No desinstala Widgets ni garantiza detener sus procesos.", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0, 1);
        R("restart-apps", "No reabrir apps al iniciar sesión", "Inicio", "Desactiva el reinicio automático de apps compatibles al volver a iniciar sesión.", "Tendrás que abrir esas aplicaciones manualmente.", @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "RestartApps", 0, 1);
        R("update-manual", "Windows Update · actualizaciones manuales", "Modo desarrollador", "Desactiva actualizaciones automáticas mediante NoAutoUpdate. Conserva los componentes y la búsqueda manual.", "Deberás buscar e instalar parches. Las directivas de una organización pueden prevalecer. Requiere edición Pro, Enterprise o Education.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1, 99, true, true);
        a.Add(new("update-service", "Windows Update · deshabilitar servicio", "Modo desarrollador",
            "Cambia solo el inicio de wuauserv a Deshabilitado. No detiene una instalación en curso; se aplica al siguiente inicio.",
            "Bloquea búsqueda y descarga de parches; puede afectar drivers, Store y funciones opcionales. Windows puede reactivarlo. No hay garantía de ahorro ni riesgo cero.",
            TargetKind.Service, "wuauserv", "Start", 4, 99, true));
        return a;
    }
}
