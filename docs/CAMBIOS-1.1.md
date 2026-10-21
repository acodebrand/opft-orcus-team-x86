# Cambios solicitados · versión 1.1

| Petición | Implementación |
|---|---|
| Quitar los blancos | Las tres paletas y los controles son oscuros. Perla conserva su nombre con tonos azul/gris oscuro. |
| Quitar la imagen | Eliminada del XAML, del proyecto y del ejecutable actual. |
| Letras pequeñas arriba a la derecha | Marca de texto Orcus Team con fuente gótica integrada; no se instala en Windows. La fuente original no estaba disponible, por lo que no se afirma coincidencia exacta. |
| Terminal → Default | Cambiado; Obsidiana y Perla conservan sus nombres. |
| Crear restauración desde el principal | Inicio contiene Crear punto, Verificar punto reciente y Configurar protección. Se comprueba el recibo antes de permitir aplicar. |
| Ordenar recuperación | Recuperación queda dedicada a deshacer cambios y consultar respaldos de OPTF. |
| Aclarar opciones personalizables | Encabezado “Opciones extra que puedes personalizar”, explicación breve y búsqueda con indicación visible. |
| Distinguir seleccionadas | Fondo, borde, casilla y etiqueta SELECCIONADA según el tema. |
| Textos pequeños y claros | Jerarquía ajustada; descripción y efectos visibles, información técnica desplegable. |
| Animaciones fluidas | Navegación con opacidad/desplazamiento suaves; respuesta breve al cursor, sin animaciones permanentes. |
| Optimizar el programa | Sin imagen pesada, sin análisis al abrir, scripts integrados, actualizaciones de selección por lotes y sin notificaciones redundantes. |
| No probar cambios en la PC | Núcleo e interfaz probados con sistema ficticio; modo de pruebas bloquea acceso al backend Windows. |
| Ordenar archivos y código completo | Distribución con Iniciar.cmd, app/ y docs/. Framework y dependencias dentro del ejecutable. Fuentes divididas en interfaz, temas, servicios, pruebas y núcleo; ZIP de código incluido. |

No se promete riesgo cero. Los efectos reales en Windows se deben validar en una máquina virtual antes de distribuir la herramienta ampliamente.
