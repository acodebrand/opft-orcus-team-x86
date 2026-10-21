# OPTF ORCUS TEAM · 1.5

Aplicación de escritorio para configurar Windows 10/11. Interfaz nativa en C# / WPF, sin cuenta, servidor ni telemetría propia. Al abrirse no inicia un análisis: eliges cuándo analizar y qué aplicar.

## Entregables

- `OPTF-OrcusTeam-Setup.exe`: instalador con ruta predeterminada en Archivos de programa y selector de carpeta personalizada, sin código fuente.
- `OPTF-OrcusTeam-Portable.exe`: programa autocontenido sin instalación.
- `OPTF-ORCUS-TEAM-source.zip`: código editable completo.

```text
OPTF-ORCUS-TEAM-1.5/
├── Iniciar.cmd
├── app/
│   └── OPTF.OrcusTeam.exe
└── docs/
    ├── README.md
    ├── VALIDACION.md
    ├── CAMBIOS-1.5.md
    ├── FUENTES.md
    └── licenses/
```

.NET, PresentationFramework y las demás dependencias están integrados en el ejecutable. No tienes que instalar .NET ni mover DLL manualmente. El runtime puede extraer sus bibliotecas nativas en su caché de usuario durante el primer arranque; no las dispersa en la raíz de la distribución.

En el proyecto, `Iniciar.cmd` abre `artifacts/release/OPTF-OrcusTeam-Portable.exe`.

## Flujo de uso

1. Windows solicita permiso de administrador antes de mostrar el programa; al aceptar, abre directamente en **Inicio**.
2. El punto de restauración es opcional. Puedes crearlo o verificar uno reciente desde Inicio, pero no bloquea la aplicación de perfiles.
3. Si Protección del sistema está desactivada, puedes usar **Configurar protección** o continuar sin punto.
4. Al abrir, el programa analiza automáticamente el inventario del equipo. **Analizar equipo** permite actualizar la muestra manualmente.
5. En **Perfiles**, elige Normal, Media, Ultra o Ultra Max y el uso del equipo. Personaliza las opciones: fondo y borde de color, marca y etiqueta **SELECCIONADA** identifican las incluidas.
6. **Aplicar perfil**, **Aplicar extras** y **Aplicar cambio de desarrollador** ejecutan únicamente la selección de su sección. El motor valida el estado actual antes de escribir.
7. Al terminar aparece **Cambios aplicados**, el perfil o sección activa, el número de cambios verificados y la recomendación de reinicio. Puedes reiniciar ahora o después; no se reinicia sin pulsar **Sí**.
8. **Recuperación** permite deshacer operaciones de OPTF en orden inverso.

Windows puede limitar la frecuencia de creación de puntos, denegar la operación o tener la protección desactivada. OPTF informa el fallo, pero el punto es opcional. El motor siempre crea su propio respaldo transaccional antes de escribir. Un punto no sustituye el respaldo de tus archivos.

## Diseño

- Tres temas oscuros: **Default**, **Obsidiana** y **Perla**. También son oscuros los selectores, casillas, buscador, menús y barras de desplazamiento.
- Se retiró la imagen original del programa y de sus recursos compilados.
- La marca superior usa Grenze Gotisch Black. Aplicación e instalador tienen un emblema original Orcus Team generado para este proyecto.
- Descripciones y consecuencias siempre visibles; destinos de registro, servicios y estado en **Detalles técnicos**.
- Transiciones breves con suavizado para navegación y respuesta visual al pasar el cursor. Sin bucles de animación permanentes ni efectos de desenfoque.
- La confirmación aplicada usa una ventana oscura integrada: estado, métricas, recomendación de reinicio, respaldo plegable y acciones claras.
- Se redujeron tamaños y repeticiones de texto. Las selecciones se actualizan por lotes y no se notifica cuando un valor no ha cambiado.

## Modo desarrollador

Está separado del perfil **Programación**, permanece bloqueado al iniciar y no se activa desde un perfil.

- **Actualizaciones manuales**: configura `NoAutoUpdate=1` en ediciones Pro, Enterprise y Education. Conserva componentes y búsqueda manual.
- **Deshabilitar servicio Windows Update**: cambia únicamente el inicio de `wuauserv` a Deshabilitado. No detiene una instalación en curso: el efecto se completa al reiniciar.
- Elegir un método desmarca el otro. El motor también rechaza la combinación.
- Se bloquea si se detectan mantenimiento o reinicio pendiente. Los cambios de perfiles se bloquean en equipos administrados detectados.
- BITS, UsoSvc, WaaSMedicSvc, TrustedInstaller, Defender y Firewall no se ofrecen como servicios desactivables.

Desactivar Update retrasa parches y puede afectar Store, drivers o componentes opcionales. Windows puede reactivar su servicio. La recuperación devuelve el valor previo, que podría estar ya deshabilitado antes de usar OPTF; no repara recortes de otras herramientas.

## Respaldos de la aplicación

Se guardan en `%LOCALAPPDATA%\OPTF Orcus Team\Backups`, separados del programa. Las preferencias están en la misma carpeta de datos. No se copian archivos personales ni se suben datos.

El motor conserva el estado original antes de escribir, registra cada intento y verifica el resultado. Ante un fallo intenta revertir los cambios; una recuperación incompleta bloquea operaciones nuevas. No sobrescribe a ciegas valores cambiados por otra herramienta. Restaura primero la operación más reciente.

Los servicios no se detienen durante la optimización. Reinicia manualmente cuando hayas guardado tu trabajo, tanto al aplicar como al restaurar, para que recarguen su configuración. No se cambian cuentas, permisos, binarios ni dependencias de servicios.

Si elevas con credenciales de otra cuenta, los ajustes de usuario y respaldos corresponden a esa cuenta. No se eliminan aplicaciones, componentes del sistema, tareas programadas ni archivos por detrás de la interfaz. Inicio, desinstalación, limpieza y energía se gestionan desde las herramientas de Windows abiertas por los botones de Extras.

## Organización del código

```text
src/Orcus.App/
├── MainWindow.xaml                 Interfaz principal
├── MainWindow.xaml.cs              Flujos y coordinación de la interfaz
├── Themes/                         Paletas y controles oscuros
├── Assets/Fonts/                    Tipografía de la marca
├── Services/                       Adaptadores de Windows y scripts integrados
└── Testing/                        Interfaz con equipo y restauración ficticios
src/Orcus.Core/                      Catálogo, transacciones y protección
scripts/                            Compilación y pruebas simuladas
tests/Orcus.Tests/                   Pruebas del núcleo con dobles de prueba
docs/                               Documentación, validación y licencias
artifacts/release/                   Ejecutable portable y ZIP de código
artifacts/archive/                   Distribuciones anteriores, fuera del paquete actual
```

## Compilar y verificar

SDK .NET 10 y Windows para compilar WPF:

```powershell
dotnet build Orcus.sln -c Release
.\scripts\Test-Simulated.ps1
.\build.ps1
```

Las pruebas del núcleo usan valores ficticios. `--ui-test` ejecuta la interfaz con inventario y restauración simulados, bloquea el backend nativo y no lee preferencias personales. `--smoke-test` es un alias del mismo modo simulado: ya no ejecuta el inventario real.

```powershell
.\artifacts\release\OPTF-OrcusTeam-Portable.exe --ui-test artifacts/qa-portable
```

Las capturas muestran datos ficticios. Las pruebas reales de servicios, restauración y reinicios quedan reservadas para una VM.

## Protección del código

El instalador no distribuye `.cs`, `.xaml`, proyectos ni símbolos. La aplicación se publica autocontenida, en un archivo y con ReadyToRun para dificultar la inspección casual. Ningún ejecutable .NET puede garantizar que sea imposible de descompilar o analizar con acceso local; no se promete una protección irreal.
