# Abiertos recientemente

Aparece debajo de Historial en Configuración. Registra las carpetas reales visitadas en la pestaña activa del Explorador de archivos de Windows en primer plano, sin necesidad de buscar. No incluye archivos, navegación en segundo plano, otros gestores, diálogos de archivos ni ubicaciones virtuales.

Está activado por defecto. Guarda 200 entradas (1–5000) y muestra 20 en el menú (1–100, sin superar las guardadas). Cada ruta aparece una vez, por visita más reciente. No caducan por antigüedad. Desactivarlo conserva los datos y detiene las adiciones y las actualizaciones de fecha.

Las exclusiones aceptan una ruta absoluta por línea, variables de entorno y selección de carpetas. Solo excluyen las carpetas indicadas, no sus subcarpetas; Aplicar también elimina las entradas existentes que coincidan. No se admiten comodines. Eliminar y vaciar tienen efecto inmediato; las demás opciones se guardan con Aplicar. Una visita real posterior puede volver a registrar una carpeta eliminada.

Navegación rápida muestra Historial, un separador y Abiertos recientemente. Permite desplegar subcarpetas en cascada. La opción de visualización de Folder Cascader es independiente del registro.

Al iniciar solo se captura la carpeta en primer plano. Los eventos se agrupan durante unos 200 ms, por lo que pueden omitirse ubicaciones transitorias. Los datos se guardan en `recent-folders.json` mediante escrituras atómicas agrupadas aproximadamente cada segundo y al salir normalmente. Una terminación inesperada puede perder las últimas actualizaciones sin guardar. Las carpetas temporalmente inaccesibles se conservan, pero se omiten en el menú.
