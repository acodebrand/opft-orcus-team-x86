# Distribución y reputación de Windows

El binario debe firmarse con una identidad estable antes de publicarse. Un certificado autofirmado sirve para pruebas internas, pero no establece confianza pública.

Opciones recomendadas:

1. Microsoft Store, que vuelve a firmar el paquete y evita advertencias de descarga de SmartScreen.
2. Microsoft Artifact Signing o un certificado Authenticode OV/EV emitido por una autoridad incluida en Windows Root Certificate Program.
3. Firmar aplicación e instalador, añadir sello de tiempo y no modificar los archivos después de firmarlos.
4. Usar siempre la misma identidad de editor para que la reputación pueda acumularse entre versiones.
5. Si Defender clasifica el archivo limpio como malware, enviarlo como desarrollador al portal de Microsoft Security Intelligence.

La firma válida muestra el nombre del editor y permite acumular reputación, pero una publicación nueva todavía puede mostrar SmartScreen hasta obtener reputación. EV ya no elimina automáticamente esa advertencia.

Referencias oficiales:

- https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation
- https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options
- https://learn.microsoft.com/defender-xdr/developer-faq
