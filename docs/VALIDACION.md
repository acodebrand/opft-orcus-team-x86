# Validación de la versión 1.5

## Pruebas realizadas

- Compilación Release de la solución con cero errores y cero advertencias.
- **33 pruebas del núcleo con sistema simulado**: permisos, catálogo protegido, reaplicación verificada, perfiles, tipos de datos, diario previo a escritura, recuperación de fallos, conflictos y restauración inversa.
- **40 comprobaciones de interfaz con datos ficticios**: análisis automático, acción directa de aplicar, nueva confirmación, temas, selección, restauración simulada y diseño compacto.
- Renderizado de nueve pantallas simuladas, incluidos los selectores y ventanas de 800 × 620. Revisión visual de jerarquía, tamaño de textos, contraste y ausencia de la imagen anterior.
- Análisis de sintaxis de los scripts PowerShell sin ejecutarlos.
- La publicación portable se comprueba con el mismo modo simulado, para verificar que el empaquetado encuentra las fuentes y scripts integrados.
- El instalador se compila como EXE único con la aplicación integrada. Su modo `--self-test` confirmó que el payload está presente sin instalar ni modificar Windows. No se ejecutó una instalación real en esta PC.

Resultados y capturas: `artifacts/qa-redesign` y `artifacts/qa-portable`. Los valores de CPU, RAM, servicios y procesos son inventados y están etiquetados como simulación.

## Lo que no se hizo

En esta revisión no se ejecutó el inventario real, no se elevaron privilegios, no se crearon puntos reales de restauración y no se leyeron o escribieron configuraciones de servicios/registro de Windows como parte de las pruebas. No se aplicaron optimizaciones al equipo del usuario. El código fuente, los binarios y las capturas se generan dentro del proyecto.

El modo simulado no puede certificar el comportamiento real de SCM, UAC, PowerShell/System Restore, la frecuencia de creación de puntos, las dependencias de servicios o los efectos de un reinicio. Tampoco se afirma una tasa de FPS medida para las animaciones.

## Matriz pendiente para una VM de laboratorio

1. Windows 10/11 x64, Home y Pro, con snapshot previo.
2. Crear un punto con protección habilitada; verificarlo y comprobar que Windows lo enumera.
3. Protección desactivada y límite de frecuencia: comprobar el error y el bloqueo de aplicación. Verificar un punto reciente sin presentarlo como recién creado.
4. Eliminar o dejar expirar un punto verificado: confirmar que aplicar continúa usando el respaldo transaccional interno.
5. Aplicar un ajuste visual y un servicio opcional, reiniciar y restaurar; comprobar red, audio, inicio y Store.
6. Probar cada método de Update por separado, restaurarlo y buscar parches.
7. Probar UAC cancelado, permisos insuficientes y otra cuenta administradora.
8. Revisar DPI 125/150/200%, teclado y preferencias de movimiento reducido.

Las pruebas de esta lista no se ejecutaron en la PC del usuario.
